package ci.wazap.gateway

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.widget.ScrollView
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.button.MaterialButton
import com.google.android.material.switchmaterial.SwitchMaterial
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var switchAutoReply: SwitchMaterial
    private lateinit var tvNotificationStatus: TextView
    private lateinit var btnGrantNotification: MaterialButton
    private lateinit var tvBatteryStatus: TextView
    private lateinit var btnIgnoreBattery: MaterialButton
    private lateinit var tvApiStatus: TextView
    private lateinit var btnPingApi: MaterialButton
    private lateinit var tvStorageStatus: TextView
    private lateinit var btnGrantStorage: MaterialButton
    private lateinit var btnTestDispo: MaterialButton
    private lateinit var btnClearLogs: MaterialButton
    private lateinit var tvLogs: TextView
    private lateinit var scrollLogs: ScrollView

    private val storagePermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) {
        updatePermissionsStatus()
    }

    private val logListener: (String) -> Unit = { logLine ->
        runOnUiThread {
            if (logLine == "__CLEAR__") {
                tvLogs.text = ""
            } else {
                tvLogs.append(logLine + "\n")
                scrollLogs.post { scrollLogs.fullScroll(ScrollView.FOCUS_DOWN) }
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        initViews()
        setupListeners()
        updatePermissionsStatus()

        // Initial Ping
        doPing()
    }

    override fun onResume() {
        super.onResume()
        updatePermissionsStatus()
        GatewayLogger.addListener(logListener)
        tvLogs.text = GatewayLogger.getFullLog() + if (GatewayLogger.getFullLog().isNotEmpty()) "\n" else ""
        scrollLogs.post { scrollLogs.fullScroll(ScrollView.FOCUS_DOWN) }
    }

    override fun onPause() {
        super.onPause()
        GatewayLogger.removeListener(logListener)
    }

    private fun initViews() {
        switchAutoReply = findViewById(R.id.switchAutoReply)
        tvNotificationStatus = findViewById(R.id.tvNotificationStatus)
        btnGrantNotification = findViewById(R.id.btnGrantNotification)
        tvBatteryStatus = findViewById(R.id.tvBatteryStatus)
        btnIgnoreBattery = findViewById(R.id.btnIgnoreBattery)
        tvApiStatus = findViewById(R.id.tvApiStatus)
        btnPingApi = findViewById(R.id.btnPingApi)
        tvStorageStatus = findViewById(R.id.tvStorageStatus)
        btnGrantStorage = findViewById(R.id.btnGrantStorage)
        btnTestDispo = findViewById(R.id.btnTestDispo)
        btnClearLogs = findViewById(R.id.btnClearLogs)
        tvLogs = findViewById(R.id.tvLogs)
        scrollLogs = findViewById(R.id.scrollLogs)

        val prefs = getSharedPreferences("wazap_gateway_prefs", Context.MODE_PRIVATE)
        switchAutoReply.isChecked = prefs.getBoolean("auto_reply_enabled", true)
    }

    @SuppressLint("BatteryLife")
    private fun setupListeners() {
        switchAutoReply.setOnCheckedChangeListener { _, isChecked ->
            getSharedPreferences("wazap_gateway_prefs", Context.MODE_PRIVATE)
                .edit()
                .putBoolean("auto_reply_enabled", isChecked)
                .apply()

            if (isChecked) {
                GatewayLogger.log("🟢 Réponses automatiques activées.")
            } else {
                GatewayLogger.log("⚪ Réponses automatiques désactivées (veille).")
            }
        }

        btnGrantNotification.setOnClickListener {
            val intent = Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)
            startActivity(intent)
        }

        btnIgnoreBattery.setOnClickListener {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                try {
                    val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
                        data = Uri.parse("package:$packageName")
                    }
                    startActivity(intent)
                } catch (_: Exception) {
                    val fallback = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                    startActivity(fallback)
                }
            }
        }

        btnGrantStorage.setOnClickListener {
            requestStoragePermissions()
        }

        btnPingApi.setOnClickListener {
            doPing()
        }

        btnTestDispo.setOnClickListener {
            doSimulateTestDispo()
        }

        btnClearLogs.setOnClickListener {
            GatewayLogger.clear()
        }
    }

    private fun requestStoragePermissions() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            storagePermissionLauncher.launch(arrayOf(Manifest.permission.READ_MEDIA_IMAGES))
        } else {
            storagePermissionLauncher.launch(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE))
        }
    }

    private fun hasStoragePermission(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            ContextCompat.checkSelfPermission(
                this,
                Manifest.permission.READ_MEDIA_IMAGES
            ) == PackageManager.PERMISSION_GRANTED
        } else {
            ContextCompat.checkSelfPermission(
                this,
                Manifest.permission.READ_EXTERNAL_STORAGE
            ) == PackageManager.PERMISSION_GRANTED
        }
    }

    private fun updatePermissionsStatus() {
        // Notification Listener
        val isNotifEnabled = NotificationManagerCompat.getEnabledListenerPackages(this).contains(packageName)
        if (isNotifEnabled) {
            tvNotificationStatus.text = "Accès Notifications : Autorisé 🟢"
            tvNotificationStatus.setTextColor(getColor(R.color.status_green))
            btnGrantNotification.isEnabled = false
            btnGrantNotification.alpha = 0.5f
        } else {
            tvNotificationStatus.text = "Accès Notifications : Requis 🔴"
            tvNotificationStatus.setTextColor(getColor(R.color.status_red))
            btnGrantNotification.isEnabled = true
            btnGrantNotification.alpha = 1.0f
        }

        // Battery Optimization
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            val powerManager = getSystemService(Context.POWER_SERVICE) as PowerManager
            val isIgnoring = powerManager.isIgnoringBatteryOptimizations(packageName)
            if (isIgnoring) {
                tvBatteryStatus.text = "Batterie : Exemption active ⚡"
                tvBatteryStatus.setTextColor(getColor(R.color.status_green))
                btnIgnoreBattery.isEnabled = false
                btnIgnoreBattery.alpha = 0.5f
            } else {
                tvBatteryStatus.text = "Batterie : À exempter ⚠️"
                tvBatteryStatus.setTextColor(getColor(R.color.gold))
                btnIgnoreBattery.isEnabled = true
                btnIgnoreBattery.alpha = 1.0f
            }
        }

        // Storage / Photos WhatsApp
        if (hasStoragePermission()) {
            tvStorageStatus.text = "Photos WhatsApp : Autorisé 🟢"
            tvStorageStatus.setTextColor(getColor(R.color.status_green))
            btnGrantStorage.isEnabled = false
            btnGrantStorage.alpha = 0.5f
        } else {
            tvStorageStatus.text = "Photos WhatsApp : Requis 🔴"
            tvStorageStatus.setTextColor(getColor(R.color.status_red))
            btnGrantStorage.isEnabled = true
            btnGrantStorage.alpha = 1.0f
        }
    }

    private fun doPing() {
        tvApiStatus.text = "Serveur : Test en cours..."
        tvApiStatus.setTextColor(getColor(R.color.gold))

        lifecycleScope.launch {
            val result = GatewayApiClient.ping()
            if (result.success) {
                tvApiStatus.text = "Serveur : En ligne (${result.latencyMs}ms) 🟢"
                tvApiStatus.setTextColor(getColor(R.color.status_green))
                GatewayLogger.log("🌐 Serveur connecté : ${result.urlUsed} (${result.latencyMs}ms)")
            } else {
                tvApiStatus.text = "Serveur : Injoignable 🔴"
                tvApiStatus.setTextColor(getColor(R.color.status_red))
                GatewayLogger.log("⚠️ Échec test serveur : ${result.message}")
            }
        }
    }

    private fun doSimulateTestDispo() {
        GatewayLogger.log("🧪 Test simulation envoi mot-clé [DISPO]...")
        lifecycleScope.launch {
            val result = GatewayApiClient.processMessage(
                sender = "+2250700000000",
                senderName = "Livreur Test",
                text = "DISPO"
            )

            if (result.success && result.shouldReply) {
                GatewayLogger.log("✅ Réponse simulée avec succès :\n${result.replyText}")
            } else {
                GatewayLogger.log("❌ Résultat simulation : ${result.reason} (success=${result.success})")
            }
        }
    }
}
