package ci.wazap.gateway

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.RemoteInput
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.io.File
import java.util.concurrent.ConcurrentHashMap

class WazapNotificationListenerService : NotificationListenerService() {

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    // Deduplication cache: key -> timestamp ms
    private val processedMessages = ConcurrentHashMap<String, Long>()

    companion object {
        var isConnected: Boolean = false
            private set

        private const val CHANNEL_ID = "wazap_gateway_service_channel"
        private const val NOTIFICATION_ID = 1001

        // SEUL et UNIQUE package autorisé : WhatsApp Business (Règle Canonique Inviolable)
        private const val PACKAGE_WHATSAPP_BUSINESS = "com.whatsapp.w4b"

        private val BLACKLISTED_NUMBERS = setOf(
            "0708323366",   // Téléphone personnel du propriétaire / admin
            "708323366",
            "2250708323366",
            "0544051972",   // Ligne officielle WAZAP Business (anti-boucle)
            "544051972",
            "2250544051972"
        )
    }

    override fun onListenerConnected() {
        super.onListenerConnected()
        isConnected = true
        GatewayLogger.log("🟢 Service d'écoute WAZAP connecté (WhatsApp Business uniquement).")
        createNotificationChannel()
        showStatusNotification()
    }

    override fun onListenerDisconnected() {
        super.onListenerDisconnected()
        isConnected = false
        GatewayLogger.log("🔴 Service d'écoute WAZAP déconnecté.")
    }

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        super.onNotificationPosted(sbn)
        if (sbn == null) return

        val packageName = sbn.packageName ?: return
        if (packageName != PACKAGE_WHATSAPP_BUSINESS) {
            // STRICT : Seul WhatsApp Business est écouté. WhatsApp standard est 100% exclu.
            return
        }

        val notification = sbn.notification ?: return

        // Ignorer les notifications de résumé général (ex : "3 nouveaux messages")
        if ((notification.flags and Notification.FLAG_GROUP_SUMMARY) != 0) {
            return
        }

        // Vérifier si la réponse automatique est activée
        val prefs = getSharedPreferences("wazap_gateway_prefs", Context.MODE_PRIVATE)
        val autoReplyEnabled = prefs.getBoolean("auto_reply_enabled", true)
        if (!autoReplyEnabled) {
            return
        }

        // Filtrer strictement les messages de groupes WhatsApp (@g.us)
        if (isGroupMessage(sbn, notification)) {
            return
        }

        val extras = notification.extras ?: return

        // Titre de la notification (Nom de contact ou numéro brut)
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim() ?: ""

        // Corps du message
        var messageText = extras.getCharSequence(Notification.EXTRA_TEXT)?.toString()?.trim() ?: ""

        // Extraction avancée via MessagingStyle
        try {
            val messagingStyle = NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification(notification)
            val lastMsg = messagingStyle?.messages?.lastOrNull()
            if (lastMsg != null) {
                val extracted = lastMsg.text?.toString()?.trim()
                if (!extracted.isNullOrBlank()) {
                    messageText = extracted
                }
            }
        } catch (_: Exception) {
            // Repli sur EXTRA_TEXT
        }

        if (title.isBlank() && messageText.isBlank()) return

        // Extraction robuste du numéro de téléphone E.164
        val senderPhone = extractSenderPhone(sbn, notification, title)
        if (senderPhone.isBlank()) {
            GatewayLogger.log("⚠️ Ignoré : numéro introuvable pour la notification [$title].")
            return
        }

        // GARDE-FOU LOCAL : Rejet immédiat du numéro personnel du propriétaire / admin
        val cleanPhoneDigits = senderPhone.filter { it.isDigit() }
        if (BLACKLISTED_NUMBERS.any { cleanPhoneDigits.endsWith(it) }) {
            GatewayLogger.log("🛑 Ignoré localement (numéro personnel/protégé exclu) : [$senderPhone]")
            return
        }

        val senderName = if (title.filter { it.isDigit() }.length >= 8) "Livreur" else title

        // Filtrer les messages de service internes de WhatsApp
        val lowerText = messageText.lowercase()
        if (lowerText.contains("messages de") ||
            lowerText.contains("checking for new messages") ||
            lowerText.contains("recherche de nouveaux messages") ||
            lowerText.contains("whatsapp web") ||
            lowerText.contains("sauvegarde")
        ) {
            return
        }

        // Déduplication anti-rebond (15 secondes max)
        val dedupeKey = "$senderPhone::$messageText"
        val now = System.currentTimeMillis()
        val lastSeen = processedMessages[dedupeKey] ?: 0L
        if (now - lastSeen < 15_000L) {
            return
        }
        processedMessages[dedupeKey] = now
        cleanOldCache(now)

        GatewayLogger.log("📩 Reçu de [$senderPhone] ($senderName) : \"$messageText\"")

        // Recherche de l'action de réponse directe (RemoteInput)
        val replyAction = findReplyAction(notification)
        if (replyAction == null) {
            GatewayLogger.log("⚠️ Aucune action de réponse directe trouvée pour [$senderPhone].")
            return
        }

        val isPhotoNotification = messageText.contains("photo", ignoreCase = true) ||
                messageText.contains("📷") ||
                messageText.contains("image", ignoreCase = true)

        serviceScope.launch {
            try {
                // Si une photo est détectée, tenter la lecture et l'envoi direct au serveur OCR
                if (isPhotoNotification) {
                    var photoFile = findLatestWhatsAppImage()
                    if (photoFile == null) {
                        kotlinx.coroutines.delay(600)
                        photoFile = findLatestWhatsAppImage()
                    }
                    if (photoFile == null) {
                        kotlinx.coroutines.delay(1000)
                        photoFile = findLatestWhatsAppImage()
                    }

                    if (photoFile != null) {
                        GatewayLogger.log("📸 Photo CNI/Permis détectée (${photoFile.name}), envoi OCR pour [$senderPhone]...")
                        val uploadResult = GatewayApiClient.uploadPhoto(
                            sender = senderPhone,
                            senderName = senderName,
                            imageFile = photoFile
                        )

                        if (uploadResult.success && uploadResult.shouldReply && uploadResult.replyText.isNotBlank()) {
                            GatewayLogger.log("🤖 Réponse certification OCR prête pour [$senderPhone]")
                            val sent = sendReply(replyAction.first, replyAction.second, uploadResult.replyText)
                            if (sent) {
                                GatewayLogger.log("✅ Confirmation certification envoyée à [$senderPhone] via WhatsApp !")
                            } else {
                                GatewayLogger.log("❌ Erreur lors de l'envoi de la confirmation OCR à [$senderPhone].")
                            }
                            return@launch
                        } else {
                            GatewayLogger.log("ℹ️ Résultat téléversement OCR : ${uploadResult.reason}")
                        }
                    } else {
                        GatewayLogger.log("ℹ️ Notification photo détectée mais fichier non encore synchronisé sur le disque après délai.")
                    }
                }

                // Traitement standard (Mots-clés DISPO, Choix de commune, etc.)
                val result = GatewayApiClient.processMessage(
                    sender = senderPhone,
                    senderName = senderName,
                    text = messageText
                )

                if (!result.success) {
                    GatewayLogger.log("❌ Échec API pour [$senderPhone] : ${result.reason}")
                    return@launch
                }

                if (!result.shouldReply || result.replyText.isBlank()) {
                    GatewayLogger.log("ℹ️ Pas de réponse requise pour [$senderPhone] (Catégorie: ${result.category})")
                    return@launch
                }

                GatewayLogger.log("🤖 Réponse API (${result.category}) prête pour [$senderPhone]")

                // Expédition de la réponse via WhatsApp RemoteInput
                val sent = sendReply(replyAction.first, replyAction.second, result.replyText)
                if (sent) {
                    GatewayLogger.log("✅ Réponse envoyée avec succès à [$senderPhone] via WhatsApp !")
                } else {
                    GatewayLogger.log("❌ Erreur lors de l'envoi de la réponse à [$senderPhone].")
                }
            } catch (e: Exception) {
                GatewayLogger.log("💥 Erreur traitement pour [$senderPhone] : ${e.message}")
            }
        }
    }

    private fun extractSenderPhone(sbn: StatusBarNotification, notification: Notification, title: String): String {
        // 1. shortcutId (Android 11+ / API 30+) : WhatsApp y place toujours "<phone>@s.whatsapp.net"
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val shortcutId = notification.shortcutId
            if (!shortcutId.isNullOrBlank() && shortcutId.contains("@s.whatsapp.net")) {
                val digits = shortcutId.substringBefore("@s.whatsapp.net").filter { it.isDigit() }
                if (digits.length >= 8) return "+$digits"
            }
        }

        // 2. sbn.tag : Identifiant de conversation WhatsApp
        val tag = sbn.tag
        if (!tag.isNullOrBlank() && tag.contains("@s.whatsapp.net")) {
            val digits = tag.substringBefore("@s.whatsapp.net").filter { it.isDigit() }
            if (digits.length >= 8) return "+$digits"
        }

        // 3. Person key ou uri dans MessagingStyle
        try {
            val messagingStyle = NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification(notification)
            val person = messagingStyle?.messages?.lastOrNull()?.person
            val key = person?.key
            if (!key.isNullOrBlank() && key.contains("@s.whatsapp.net")) {
                val digits = key.substringBefore("@s.whatsapp.net").filter { it.isDigit() }
                if (digits.length >= 8) return "+$digits"
            }
            val uri = person?.uri
            if (!uri.isNullOrBlank()) {
                val digits = uri.filter { it.isDigit() }
                if (digits.length >= 8) return "+$digits"
            }
        } catch (_: Exception) {}

        // 4. Notification.EXTRA_PEOPLE
        val extras = notification.extras
        if (extras != null) {
            val people = extras.getStringArray(Notification.EXTRA_PEOPLE)
            if (people != null) {
                for (p in people) {
                    val digits = p.filter { it.isDigit() }
                    if (digits.length >= 8) return "+$digits"
                }
            }
        }

        // 5. Chiffres dans le titre de la notification
        val digitsInTitle = title.filter { it.isDigit() }
        if (digitsInTitle.length >= 8) {
            return "+$digitsInTitle"
        }

        return ""
    }

    private fun isGroupMessage(sbn: StatusBarNotification, notification: Notification): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val shortcutId = notification.shortcutId
            if (shortcutId?.contains("@g.us") == true) return true
        }
        val tag = sbn.tag
        if (tag?.contains("@g.us") == true) return true
        try {
            val messagingStyle = NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification(notification)
            if (messagingStyle?.isGroupConversation == true) return true
        } catch (_: Exception) {}
        return false
    }

    private fun findLatestWhatsAppImage(): File? {
        val candidateDirs = listOf(
            File("/storage/emulated/0/Android/media/com.whatsapp.w4b/WhatsApp Business/Media/WhatsApp Business Images"),
            File("/storage/emulated/0/Android/media/com.whatsapp.w4b/WhatsApp Business/Media/WhatsApp Business Documents"),
            File("/storage/emulated/0/WhatsApp Business/Media/WhatsApp Business Images"),
            File("/storage/emulated/0/WhatsApp Business/Media/WhatsApp Business Documents")
        )

        val now = System.currentTimeMillis()
        var latestFile: File? = null
        var latestTime = 0L

        for (dir in candidateDirs) {
            if (!dir.exists() || !dir.isDirectory) continue
            val files = dir.listFiles { f ->
                f.isFile && (f.name.endsWith(".jpg", ignoreCase = true) ||
                        f.name.endsWith(".jpeg", ignoreCase = true) ||
                        f.name.endsWith(".png", ignoreCase = true)) &&
                        !f.name.startsWith(".nomedia") &&
                        !f.name.startsWith("Sent")
            } ?: continue

            for (f in files) {
                val mod = f.lastModified()
                // Image modifiée dans les 3 dernières minutes
                if (now - mod < 180_000L && mod > latestTime) {
                    latestTime = mod
                    latestFile = f
                }
            }
        }

        return latestFile
    }

    private fun findReplyAction(notification: Notification): Pair<Notification.Action, RemoteInput>? {
        // 1. Notification standard
        val actions = notification.actions
        if (actions != null) {
            for (action in actions) {
                val remoteInputs = action.remoteInputs ?: continue
                for (ri in remoteInputs) {
                    if (!ri.resultKey.isNullOrBlank()) {
                        return Pair(action, ri)
                    }
                }
            }
        }

        // 2. NotificationCompat actions (inclut extensions Wearable et car)
        val actionCount = NotificationCompat.getActionCount(notification)
        for (i in 0 until actionCount) {
            val compatAction = NotificationCompat.getAction(notification, i) ?: continue
            val remoteInputs = compatAction.remoteInputs ?: continue
            for (ri in remoteInputs) {
                if (!ri.resultKey.isNullOrBlank()) {
                    // Trouver l'action native correspondante
                    if (actions != null && i < actions.size) {
                        val nativeRi = actions[i].remoteInputs?.firstOrNull { it.resultKey == ri.resultKey }
                        if (nativeRi != null) {
                            return Pair(actions[i], nativeRi)
                        }
                    }
                }
            }
        }

        return null
    }

    private fun sendReply(action: Notification.Action, remoteInput: RemoteInput, replyText: String): Boolean {
        return try {
            val replyBundle = Bundle().apply {
                putCharSequence(remoteInput.resultKey, replyText)
            }
            val replyIntent = Intent().addFlags(Intent.FLAG_RECEIVER_FOREGROUND)
            RemoteInput.addResultsToIntent(arrayOf(remoteInput), replyIntent, replyBundle)
            action.actionIntent.send(this, 0, replyIntent)
            true
        } catch (e: Exception) {
            GatewayLogger.log("Erreur sendReply: ${e.message}")
            false
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Passerelle WAZAP WhatsApp",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Maintient la passerelle active pour le recrutement et les courses"
                setShowBadge(false)
            }
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            manager.createNotificationChannel(channel)
        }
    }

    private fun showStatusNotification() {
        val launchIntent = packageManager.getLaunchIntentForPackage(packageName)
        val pendingIntent = if (launchIntent != null) {
            PendingIntent.getActivity(
                this,
                0,
                launchIntent,
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
            )
        } else null

        val notification = NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentTitle("Passerelle WAZAP Active 🟢")
            .setContentText("En écoute automatique sur WhatsApp Business")
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setContentIntent(pendingIntent)
            .build()

        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(NOTIFICATION_ID, notification)
    }

    private fun cleanOldCache(now: Long) {
        val iterator = processedMessages.entries.iterator()
        while (iterator.hasNext()) {
            val entry = iterator.next()
            if (now - entry.value > 60_000L) {
                iterator.remove()
            }
        }
    }
}
