plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "ci.wazap.gateway"
    compileSdk = 35

    defaultConfig {
        applicationId = "ci.wazap.gateway"
        minSdk = 26
        targetSdk = 35
        versionCode = 3
        versionName = "1.2.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    val signingPath = System.getenv("ANDROID_KEYSTORE_PATH")
    if (!signingPath.isNullOrBlank()) {
        signingConfigs.create("production") {
            storeFile = file(signingPath)
            storePassword = requireNotNull(System.getenv("ANDROID_KEYSTORE_PASSWORD"))
            keyAlias = requireNotNull(System.getenv("ANDROID_KEY_ALIAS"))
            keyPassword = requireNotNull(System.getenv("ANDROID_KEY_PASSWORD"))
        }
    }
    buildTypes {
        release {
            if (!signingPath.isNullOrBlank()) signingConfig = signingConfigs.getByName("production")
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }
    kotlinOptions {
        jvmTarget = "21"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
}
