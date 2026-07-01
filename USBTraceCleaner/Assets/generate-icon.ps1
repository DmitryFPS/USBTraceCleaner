# app-icon.png -> app.ico (один размер 256, формат Windows Icon.Save)
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$png = Join-Path $dir 'app-icon.png'
$ico = Join-Path $dir 'app.ico'

if (-not (Test-Path $png)) { throw "Not found: $png" }

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $png))
try {
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $fs = [System.IO.File]::Create($ico)
    try { $icon.Save($fs) } finally { $fs.Close() }
    $icon.Dispose()
    $bmp.Dispose()
}
finally {
    $src.Dispose()
}

Write-Host "Created $ico ($((Get-Item $ico).Length) bytes)"
