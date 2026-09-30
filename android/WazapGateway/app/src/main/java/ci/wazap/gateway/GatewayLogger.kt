package ci.wazap.gateway

import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

object GatewayLogger {

    private val listeners = CopyOnWriteArrayList<(String) -> Unit>()
    private val buffer = mutableListOf<String>()
    private val timeFormat = SimpleDateFormat("HH:mm:ss", Locale.getDefault())

    @Synchronized
    fun log(message: String) {
        val timestamp = timeFormat.format(Date())
        val formatted = "[$timestamp] $message"
        if (buffer.size >= 200) {
            buffer.removeAt(0)
        }
        buffer.add(formatted)
        listeners.forEach { it.invoke(formatted) }
    }

    @Synchronized
    fun getFullLog(): String {
        return buffer.joinToString("\n")
    }

    @Synchronized
    fun clear() {
        buffer.clear()
        listeners.forEach { it.invoke("__CLEAR__") }
    }

    fun addListener(listener: (String) -> Unit) {
        listeners.add(listener)
    }

    fun removeListener(listener: (String) -> Unit) {
        listeners.remove(listener)
    }
}
