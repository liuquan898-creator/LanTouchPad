param([string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
& dotnet publish (Join-Path $repo 'pc\LanTouchPad.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $repo 'dist\windows')
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
& $Iscc (Join-Path $repo 'installer\LanTouchPad.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
