package ci.wazap.gateway

import android.app.Notification
import android.app.RemoteInput
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.util.concurrent.ConcurrentHashMap

class WazapNotificationListenerService : NotificationListenerService() {

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    // Deduplication cache: key -> timestamp ms
    private val processedMessages = ConcurrentHashMap<String, Long>()

    companion object {
        var isConnected: Boolean = false
            private set

        private val SUPPORTED_PACKAGES = setOf(
            "com.whatsapp.w4b", // WhatsApp Business
            "com.whatsapp"      // WhatsApp Standard
        )
    }

    override fun onListenerConnected() {
        super.onListenerConnected()
        isConnected = true
        GatewayLogger.log("🟢 Service d'écoute WAZAP connecté et opérationnel.")
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
        if (packageName !in SUPPORTED_PACKAGES) return

        val notification = sbn.notification ?: return

        // Skip summary notifications (e.g. "3 new messages") to avoid duplicate processing
        if ((notification.flags and Notification.FLAG_GROUP_SUMMARY) != 0) {
            return
        }

        // Check if auto-reply is enabled in SharedPreferences
        val prefs = getSharedPreferences("wazap_gateway_prefs", Context.MODE_PRIVATE)
        val autoReplyEnabled = prefs.getBoolean("auto_reply_enabled", true)
        if (!autoReplyEnabled) {
            return
        }

        val extras = notification.extras ?: return

        // Extract title (sender contact name or number)
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim() ?: ""

        // Extract message body
        var messageText = extras.getCharSequence(Notification.EXTRA_TEXT)?.toString()?.trim() ?: ""

        // Try extracting from MessagingStyle if available
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
            // Fallback to EXTRA_TEXT
        }

        if (title.isBlank() || messageText.isBlank()) return

        // Filter out WhatsApp system notifications
        val lowerText = messageText.lowercase()
        if (lowerText.contains("messages de") ||
            lowerText.contains("checking for new messages") ||
            lowerText.contains("recherche de nouveaux messages") ||
            lowerText.contains("whatsapp web") ||
            lowerText.contains("sauvegarde")
        ) {
            return
        }

        // Deduplication: prevent reprocessing within 15 seconds
        val dedupeKey = "$title::$messageText"
        val now = System.currentTimeMillis()
        val lastSeen = processedMessages[dedupeKey] ?: 0L
        if (now - lastSeen < 15_000L) {
            return
        }
        processedMessages[dedupeKey] = now
        cleanOldCache(now)

        GatewayLogger.log("📩 Reçu de [$title] : \"$messageText\"")

        // Find inline reply action
        val replyAction = findReplyAction(notification)
        if (replyAction == null) {
            GatewayLogger.log("⚠️ Aucune action de réponse rapide trouvée dans la notification de [$title].")
            return
        }

        // Send to Backend API asynchronously
        serviceScope.launch {
            try {
                val result = GatewayApiClient.processMessage(
                    sender = title,
                    senderName = title,
                    text = messageText
                )

                if (!result.success) {
                    GatewayLogger.log("❌ Échec API pour [$title] : ${result.reason}")
                    return@launch
                }

                if (!result.shouldReply || result.replyText.isBlank()) {
                    GatewayLogger.log("ℹ️ Pas de réponse requise pour [$title] (Catégorie: ${result.category})")
                    return@launch
                }

                GatewayLogger.log("🤖 Réponse API (${result.category}) prête pour [$title]")

                // Dispatch reply via Android Notification RemoteInput
                val sent = sendReply(replyAction.first, replyAction.second, result.replyText)
                if (sent) {
                    GatewayLogger.log("✅ Réponse envoyée avec succès à [$title] via WhatsApp !")
                } else {
                    GatewayLogger.log("❌ Erreur lors de l'envoi de la réponse à [$title].")
                }
            } catch (e: Exception) {
                GatewayLogger.log("💥 Erreur traitement pour [$title] : ${e.message}")
            }
        }
    }

    private fun findReplyAction(notification: Notification): Pair<Notification.Action, RemoteInput>? {
        val actions = notification.actions ?: return null
        for (action in actions) {
            val remoteInputs = action.remoteInputs ?: continue
            for (ri in remoteInputs) {
                if (ri.resultKey != null) {
                    return Pair(action, ri)
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
            val replyIntent = Intent()
            RemoteInput.addResultsToIntent(arrayOf(remoteInput), replyIntent, replyBundle)
            action.actionIntent.send(this, 0, replyIntent)
            true
        } catch (e: Exception) {
            GatewayLogger.log("Erreur sendReply: ${e.message}")
            false
        }
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
