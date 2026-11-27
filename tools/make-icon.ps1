# Membuat ikon aplikasi My Driver! (MyDriver.ico, logo-64.png, splash.png) di MyDriver.App\Assets.
# Jalankan ulang bila desain ikon diubah:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\MyDriver.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Gambar logo pada kanvas persegi berukuran $size piksel.
function Draw-Logo([System.Drawing.Graphics]$g, [float]$size, [float]$ox = 0, [float]$oy = 0) {
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 256.0

    # Latar: persegi biru membulat dengan gradasi
    $bg = New-RoundedPath ($ox + 8 * $s) ($oy + 8 * $s) (240 * $s) (240 * $s) (52 * $s)
    $rect = New-Object System.Drawing.RectangleF ($ox), ($oy), ($size), ($size)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(59, 130, 246)), ([System.Drawing.Color]::FromArgb(29, 78, 216)), 90
    $g.FillPath($grad, $bg)

    $white = [System.Drawing.Brushes]::White
    $penWidth = [Math]::Max(1.5, 14 * $s)

    # Kaki chip: 3 di tiap sisi
    $pinLen = 22 * $s; $pinW = [Math]::Max(1, 12 * $s)
    foreach ($i in 0..2) {
        $c = $oy + (98 + $i * 30) * $s
        $g.FillRectangle($white, $ox + 50 * $s, $c - $pinW / 2, $pinLen, $pinW)                 # kiri
        $g.FillRectangle($white, $ox + (206 * $s) - $pinLen, $c - $pinW / 2, $pinLen, $pinW)   # kanan
        $cx = $ox + (98 + $i * 30) * $s
        $g.FillRectangle($white, $cx - $pinW / 2, $oy + 50 * $s, $pinW, $pinLen)                 # atas
        $g.FillRectangle($white, $cx - $pinW / 2, $oy + (206 * $s) - $pinLen, $pinW, $pinLen)   # bawah
    }

    # Badan chip (garis tepi putih)
    $body = New-RoundedPath ($ox + 70 * $s) ($oy + 70 * $s) (116 * $s) (116 * $s) (18 * $s)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $body)

    # Tanda centang di dalam chip
    $check = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.8, 16 * $s))
    $check.StartCap = 'Round'; $check.EndCap = 'Round'; $check.LineJoin = 'Round'
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($ox + 100 * $s), ($oy + 130 * $s)),
        (New-Object System.Drawing.PointF ($ox + 120 * $s), ($oy + 150 * $s)),
        (New-Object System.Drawing.PointF ($ox + 158 * $s), ($oy + 108 * $s)))
    $g.DrawLines($check, $pts)
}

function New-LogoPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Logo $g $size
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# --- MyDriver.ico: entri PNG untuk setiap ukuran ---
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @($sizes | ForEach-Object { , (New-LogoPng $_) })
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $bw.Write([byte]($sz % 256)); $bw.Write([byte]($sz % 256))   # 256 ditulis sebagai 0
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'MyDriver.ico'), $out.ToArray())

# --- logo-64.png untuk menu samping ---
[System.IO.File]::WriteAllBytes((Join-Path $assets 'logo-64.png'), (New-LogoPng 64))

# --- splash.png: tampil seketika saat aplikasi dibuka ---
$w = 420; $h = 220
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(30, 33, 38))
Draw-Logo $g 88 36 66
$title = New-Object System.Drawing.Font 'Segoe UI Semibold', 26
$sub = New-Object System.Drawing.Font 'Segoe UI', 12
$g.DrawString('My Driver!', $title, [System.Drawing.Brushes]::White, 140, 72)
$g.DrawString('Membuka aplikasi' + [char]0x2026, $sub, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(154, 163, 175))), 144, 122)
$g.Dispose()
$bmp.Save((Join-Path $assets 'splash.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Get-ChildItem $assets | ForEach-Object { '{0} ({1:N0} byte)' -f $_.Name, $_.Length }
