# GPService — Google Play release

## Signing

Use Flutter with Dart >= 3.10.7 (this release was built with Flutter 3.41.2).
Keep the existing application ID: `net.garagepro.garage_pro_service_ops`.

The release build uses `android/key.properties`, not the debug signing key.
The prepared local credentials have been moved from `android/gradle.properties`
into this ignored file. On a new machine, copy `android/key.properties.example`
to `android/key.properties` and fill in the credentials. Keystore paths are
relative to `android/app`; the supplied key is `android/app/key/key.jks`.
CI can supply the same four property names as environment variables.
Never commit the keystore or passwords. Back them up securely; subsequent uploads
must use the upload key registered for this app in Google Play Console.

## Build

From `mobile`, using the compatible Flutter SDK:

```powershell
& 'C:\Users\kusum\fvm\versions\stable\bin\flutter.bat' build appbundle --release --dart-define=API_BASE_URL=https://gpservice-api.garage-pro.net
```

Output: `build/app/outputs/bundle/release/app-release.aab`.
The app name is GPService and the target SDK is Android 36.
Version name/code come from `pubspec.yaml` (currently `1.0.0+1`).
For every subsequent Play upload, use a higher build number, for example:

```powershell
flutter build appbundle --release --build-name=1.0.1 --build-number=2 --dart-define=API_BASE_URL=https://gpservice-api.garage-pro.net
```

Upload the AAB to the internal testing track first and verify login, branch/shift
selection, job navigation and image attachments with an authorized test account.
Play Console must also have the required listing, app access instructions,
privacy policy and Data safety declarations completed before publication.

References:
- https://docs.flutter.dev/deployment/android
- https://support.google.com/googleplay/android-developer/answer/11926878
