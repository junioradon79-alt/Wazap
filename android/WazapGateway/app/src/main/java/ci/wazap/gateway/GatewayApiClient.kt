package ci.wazap.gateway

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

data class PingResult(
    val success: Boolean,
    val latencyMs: Long,
    val message: String,
    val urlUsed: String
)

data class ProcessResult(
    val success: Boolean,
    val shouldReply: Boolean,
    val replyText: String,
    val category: String,
    val reason: String,
    val urlUsed: String
)

object GatewayApiClient {

    private const val PRIMARY_BASE_URL = "https://wazap.ci/api/gateway/whatsapp"
    private const val FALLBACK_BASE_URL = "https://junioradon79gm-001-site1.jtempurl.com/api/gateway/whatsapp"

    private val jsonMediaType = "application/json; charset=utf-8".toMediaType()

    private val httpClient = OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .writeTimeout(8, TimeUnit.SECONDS)
        .build()

    suspend fun ping(): PingResult = withContext(Dispatchers.IO) {
        val urls = listOf(PRIMARY_BASE_URL, FALLBACK_BASE_URL)
        var lastError: Exception? = null

        for (base in urls) {
            val start = System.currentTimeMillis()
            try {
                val request = Request.Builder()
                    .url("$base/ping")
                    .get()
                    .build()

                httpClient.newCall(request).execute().use { response ->
                    val elapsed = System.currentTimeMillis() - start
                    if (response.isSuccessful) {
                        val body = response.body?.string().orEmpty()
                        val json = JSONObject(body)
                        val status = json.optString("status", "Healthy")
                        return@withContext PingResult(
                            success = true,
                            latencyMs = elapsed,
                            message = "$status (${response.code})",
                            urlUsed = base
                        )
                    }
                }
            } catch (e: Exception) {
                lastError = e
            }
        }

        PingResult(
            success = false,
            latencyMs = -1,
            message = lastError?.message ?: "Erreur de connexion",
            urlUsed = PRIMARY_BASE_URL
        )
    }

    suspend fun processMessage(
        sender: String,
        senderName: String,
        text: String
    ): ProcessResult = withContext(Dispatchers.IO) {
        val payload = JSONObject().apply {
            put("sender", sender)
            put("senderName", senderName)
            put("text", text)
            put("timestamp", System.currentTimeMillis())
        }

        val requestBody = payload.toString().toRequestBody(jsonMediaType)
        val urls = listOf(PRIMARY_BASE_URL, FALLBACK_BASE_URL)
        var lastError: Exception? = null

        for (base in urls) {
            try {
                val request = Request.Builder()
                    .url("$base/process")
                    .post(requestBody)
                    .build()

                httpClient.newCall(request).execute().use { response ->
                    if (response.isSuccessful) {
                        val bodyStr = response.body?.string().orEmpty()
                        val json = JSONObject(bodyStr)
                        return@withContext ProcessResult(
                            success = json.optBoolean("success", false),
                            shouldReply = json.optBoolean("shouldReply", false),
                            replyText = json.optString("replyText", ""),
                            category = json.optString("category", "UNKNOWN"),
                            reason = json.optString("reason", ""),
                            urlUsed = base
                        )
                    }
                }
            } catch (e: Exception) {
                lastError = e
            }
        }

        ProcessResult(
            success = false,
            shouldReply = false,
            replyText = "",
            category = "ERROR",
            reason = lastError?.message ?: "Connexion échouée",
            urlUsed = PRIMARY_BASE_URL
        )
    }
}
