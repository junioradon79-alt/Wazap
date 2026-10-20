package ci.wazap.gateway

import android.app.Application
import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import okhttp3.Interceptor
import okhttp3.Response
import okio.Buffer
import java.io.IOException
import java.security.KeyStore
import java.security.MessageDigest
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.Mac
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

class GatewayApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        GatewayCredentials.initialize(this)
    }
}

/** Le secret d'enrôlement est protégé au repos par Android Keystore, jamais inclus dans l'APK. */
object GatewayCredentials : Interceptor {
    private lateinit var context: Context
    fun initialize(applicationContext: Context) { context = applicationContext.applicationContext }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("wazap.gateway", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("wazap.gateway", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }

    fun save(device: String, secret: String) {
        require(device.matches(Regex("[A-Za-z0-9-]{1,64}")) && secret.toByteArray(Charsets.UTF_8).size >= 32)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val encrypted = cipher.doFinal(secret.toByteArray(Charsets.UTF_8))
        context.getSharedPreferences("wazap_gateway_auth", Context.MODE_PRIVATE).edit()
            .putString("device", device).putString("iv", Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .putString("secret", Base64.encodeToString(encrypted, Base64.NO_WRAP)).apply()
    }

    private fun credentials(): Pair<String, ByteArray> {
        val prefs = context.getSharedPreferences("wazap_gateway_auth", Context.MODE_PRIVATE)
        val device = prefs.getString("device", null) ?: throw IOException("Appareil non enrôlé : configurez les accès.")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply {
            init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, Base64.decode(prefs.getString("iv", ""), Base64.NO_WRAP)))
        }
        return device to cipher.doFinal(Base64.decode(prefs.getString("secret", ""), Base64.NO_WRAP))
    }

    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it.toInt() and 255) }

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (request.method != "POST") return chain.proceed(request)
        try {
            val (device, secret) = credentials()
            val buffer = Buffer()
            request.body?.writeTo(buffer)
            val timestamp = (System.currentTimeMillis() / 1000).toString()
            val nonce = request.header("X-Gateway-Nonce") ?: UUID.randomUUID().toString().replace("-", "")
            val bodyHash = hex(MessageDigest.getInstance("SHA-256").digest(buffer.readByteArray()))
            val canonical = "${request.method}\n${request.url.encodedPath}\n$timestamp\n$nonce\n$bodyHash"
            val mac = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(secret, "HmacSHA256")) }
            val signed = request.newBuilder().header("X-Gateway-Device", device)
                .header("X-Gateway-Timestamp", timestamp).header("X-Gateway-Nonce", nonce)
                .header("X-Gateway-Signature", hex(mac.doFinal(canonical.toByteArray(Charsets.UTF_8)))).build()
            return chain.proceed(signed)
        } catch (e: IOException) { throw e }
        catch (e: Exception) { throw IOException("Accès de la passerelle indisponibles : réenrôlez cet appareil.", e) }
    }
}
