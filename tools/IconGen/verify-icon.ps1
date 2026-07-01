Add-Type -AssemblyName System.Drawing

$ico = 'C:\Users\adm\Projects\USBTraceCleaner\USBTraceCleaner\USBTraceCleaner\Assets\app.ico'
$bytes = [IO.File]::ReadAllBytes($ico)
$count = [BitConverter]::ToUInt16($bytes, 4)
Write-Host "ICO entries: $count"
$off = 6
for ($i = 0; $i -lt $count; $i++) {
    $w = $bytes[$off]
    $h = $bytes[$off + 1]
    $bw = if ($w -eq 0) { 256 } else { $w }
    $bh = if ($h -eq 0) { 256 } else { $h }
    $size = [BitConverter]::ToUInt32($bytes, $off + 8)
    Write-Host "  #$i ${bw}x$bh bytes=$size"
    $off += 16
}

foreach ($s in 16, 32, 48, 128, 256) {
    $ic = [Drawing.Icon]::new($ico, $s, $s)
    $b = $ic.ToBitmap()
    Write-Host "Load ${s}x${s} -> $($b.Width)x$($b.Height)"
    $mid = [int]($b.Height / 2)
    $c0 = $b.GetPixel(0, $mid)
    Write-Host "  edge x=0: R=$($c0.R) G=$($c0.G) B=$($c0.B)"
    $out = Join-Path $env:TEMP "ico_check_$s.png"
    $b.Save($out)
}

$exe = 'C:\USBTraceCleaner\USBTraceCleaner.exe'
$ei = [Drawing.Icon]::ExtractAssociatedIcon($exe)
$eb = $ei.ToBitmap()
Write-Host "EXE icon: $($eb.Width)x$($eb.Height)"
$eb.Save((Join-Path $env:TEMP 'exe_icon_check.png'))
