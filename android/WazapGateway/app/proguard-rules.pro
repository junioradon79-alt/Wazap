# ProGuard rules for Wazap Gateway
# Keep model classes or OkHttp / Coroutines rules if minified in release
-keepattributes *Annotation*
-dontwarn okhttp3.**
-dontwarn okio.**
