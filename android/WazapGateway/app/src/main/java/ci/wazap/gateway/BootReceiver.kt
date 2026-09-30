package ci.wazap.gateway

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context?, intent: Intent?) {
        if (intent?.action == Intent.ACTION_BOOT_COMPLETED) {
            GatewayLogger.log("🔄 Démarrage de l'appareil détecté (BOOT_COMPLETED).")
        }
    }
}
