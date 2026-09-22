import java.util.Properties

plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Keep credentials in android/key.properties (gitignored) or CI environment variables.
val releaseKeys = Properties().apply {
    val keyProperties = rootProject.file("key.properties")
    if (keyProperties.exists()) keyProperties.inputStream().use { load(it) }
}
fun signingValue(name: String): String? =
    System.getenv(name) ?: releaseKeys.getProperty(name) ?: providers.gradleProperty(name).orNull

android {
    namespace = "net.garagepro.garage_pro_service_ops"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = JavaVersion.VERSION_17.toString()
    }

    defaultConfig {
        applicationId = "net.garagepro.garage_pro_service_ops"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = 36
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        create("release") {
            // Paths are relative to android/app, matching the supplied keystore path.
            storeFile = signingValue("KEYSTORE_FILE")?.let { file(it) }
            storePassword = signingValue("KEYSTORE_PASSWORD")
            keyAlias = signingValue("KEY_ALIAS")
            keyPassword = signingValue("KEY_PASSWORD")
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("release")
        }
    }
}

flutter {
    source = "../.."
}
