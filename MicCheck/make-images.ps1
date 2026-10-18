# Draws the plugin icon PNGs (Elgato requires PNG at 256 and 512 px). The action and category icons are
# hand-written SVGs in plugin\imgs, and the key image is drawn by MicCheck.exe itself.
param([Parameter(Mandatory)][string]$OutDir, [int[]]$Sizes = @(256, 512))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function Draw-PluginIcon([int]$size, [string]$path) {
    $b = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'
    $s = $size / 256.0
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Color '#1f2937'), (Color '#0b0f17'), 90.0
    $g.FillRectangle($bg, $rect)

    # Microphone: capsule, cradle, stem, base
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float](12 * $s)); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $cap = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cap.AddArc([float](58*$s), [float](40*$s), [float](56*$s), [float](56*$s), 180, 180)
    $cap.AddArc([float](58*$s), [float](98*$s), [float](56*$s), [float](56*$s), 0, 180)
    $cap.CloseFigure(); $g.FillPath($white, $cap)
    $g.DrawArc($pen, [float](40*$s), [float](86*$s), [float](92*$s), [float](86*$s), 0, 180)
    $g.DrawLine($pen, [float](86*$s), [float](172*$s), [float](86*$s), [float](200*$s))
    $g.DrawLine($pen, [float](60*$s), [float](206*$s), [float](112*$s), [float](206*$s))

    # Level bars, like the key's meter
    $bars = @(@{ x = 150; h = 46; c = '#22c55e' }, @{ x = 176; h = 82; c = '#22c55e' }, @{ x = 202; h = 120; c = '#eab308' })
    foreach ($bar in $bars) {
        $br = New-Object System.Drawing.SolidBrush (Color $bar.c)
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $x = $bar.x * $s; $w = 18 * $s; $y = (206 - $bar.h) * $s; $h = $bar.h * $s; $r = 6 * $s
        $bp.AddArc($x, $y, 2*$r, 2*$r, 180, 90); $bp.AddArc($x+$w-2*$r, $y, 2*$r, 2*$r, 270, 90)
        $bp.AddLine($x+$w, $y+$h, $x, $y+$h); $bp.CloseFigure()
        $g.FillPath($br, $bp)
    }
    $g.Dispose(); $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

foreach ($size in $Sizes) {
    $name = switch ($size) { 256 { 'plugin.png' } 512 { 'plugin@2x.png' } default { "icon-$size.png" } }
    Draw-PluginIcon $size (Join-Path $OutDir $name)
}
