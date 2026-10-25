$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $repo 'assets') | Out-Null
$image = New-Object Drawing.Bitmap 512,512
$graphics = [Drawing.Graphics]::FromImage($image)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::FromArgb(8,127,108))
$pen = New-Object Drawing.Pen ([Drawing.Color]::White),18
$graphics.DrawRectangle($pen,100,122,312,266)
$graphics.DrawLine($pen,100,310,412,310)
$graphics.DrawLine($pen,256,310,256,388)
$brush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(249,188,76))
$graphics.FillEllipse($brush,270,185,58,58)
$image.Save((Join-Path $repo 'assets\lantouchpad.png'),[Drawing.Imaging.ImageFormat]::Png)
$iconBitmap = New-Object Drawing.Bitmap $image,256,256
$icon = [Drawing.Icon]::FromHandle($iconBitmap.GetHicon())
$stream = [IO.File]::Create((Join-Path $repo 'assets\lantouchpad.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $iconBitmap.Dispose() }
$densities = @{ mdpi=48; hdpi=72; xhdpi=96; xxhdpi=144; xxxhdpi=192 }
foreach ($entry in $densities.GetEnumerator()) {
    $resized = New-Object Drawing.Bitmap $image,$entry.Value,$entry.Value
    try { $resized.Save((Join-Path $repo "mobile\android\app\src\main\res\mipmap-$($entry.Key)\ic_launcher.png"),[Drawing.Imaging.ImageFormat]::Png) } finally { $resized.Dispose() }
}
$pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $image.Dispose()
