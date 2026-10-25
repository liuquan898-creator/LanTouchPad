param([string]$Flutter = 'C:\Users\34425\Flutter\flutter\bin\flutter.bat')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$android = Join-Path $repo 'mobile\android'
$propertiesPath = Join-Path $android 'key.properties'
if (!(Test-Path $propertiesPath)) {
    $keyPath = Join-Path $android 'app\lantouchpad-release.jks'
    if (Test-Path $keyPath) { throw 'Signing key exists without key.properties. Restore its password before building.' }
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $signingPassword = [Convert]::ToBase64String($bytes)
    $env:LANTOUCHPAD_SIGNING_PASSWORD = $signingPassword
    try {
        & keytool -genkeypair -v -keystore $keyPath -alias lantouchpad -keyalg RSA -keysize 3072 -validity 10000 -storepass:env LANTOUCHPAD_SIGNING_PASSWORD -keypass:env LANTOUCHPAD_SIGNING_PASSWORD -dname 'CN=LanTouchPad, OU=Development, O=YuXi, C=CN'
        if ($LASTEXITCODE -ne 0) { throw 'keytool failed' }
        $properties = "storeFile=lantouchpad-release.jks`nstorePassword=$signingPassword`nkeyPassword=$signingPassword`nkeyAlias=lantouchpad`n"
        [IO.File]::WriteAllText($propertiesPath, $properties)
    } finally { Remove-Item Env:LANTOUCHPAD_SIGNING_PASSWORD -ErrorAction SilentlyContinue }
}
Push-Location (Join-Path $repo 'mobile')
try {
    & $Flutter build apk --release
    if ($LASTEXITCODE -ne 0) { throw 'APK build failed' }
    New-Item -ItemType Directory -Force (Join-Path $repo 'dist') | Out-Null
    Copy-Item 'build\app\outputs\flutter-apk\app-release.apk' (Join-Path $repo 'dist\LanTouchPad-1.0.0.apk')
} finally { Pop-Location }
