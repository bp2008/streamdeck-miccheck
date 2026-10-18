# Renders the Elgato Marketplace media into dist\marketplace:
#   thumbnail.png + gallery-1..3.png (1920x960 PNG) and app icons.
# Needs a built MicCheck.exe (run build.ps1 first) and Microsoft Edge.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'dist\com.bp2008.miccheck.sdPlugin\MicCheck.exe'
$work = Join-Path $root 'dist\marketplace-work'
$out = Join-Path $root 'dist\marketplace'
foreach ($d in $work, $out) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null }

# Key images, the plugin icon, and a settings-panel demo, all next to media.html
& $exe --svg $work | Out-Null
& (Join-Path $root 'make-images.ps1') -OutDir $work -Sizes 256, 512
Copy-Item (Join-Path $root 'marketplace\media.html') $work
$demo = @'
<script>document.body.style.background = "#2d2d2d"; document.body.style.zoom = "1.45";
devices = [{ id: "a", name: "Headset Mic" }, { id: "b", name: "USB Mic" }, { id: "c", name: "Webcam Mic" }];
defaultComm = "Headset Mic"; settings = { device: "auto", testSeconds: 10 }; render();</script></body>
'@
$pi = [IO.File]::ReadAllText((Join-Path $root 'plugin\pi\index.html')).Replace('</body>', $demo)
[IO.File]::WriteAllText((Join-Path $work 'pi-demo.html'), $pi)

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
$profileDir = Join-Path $env:TEMP 'miccheck-edge-render'
$pages = [ordered]@{ thumb = 'thumbnail.png'; follow = 'gallery-1.png'; failures = 'gallery-2.png'; settings = 'gallery-3.png' }
foreach ($page in $pages.Keys) {
    $url = ([Uri](Join-Path $work 'media.html')).AbsoluteUri + "?page=$page"
    $png = Join-Path $out $pages[$page]
    # Edge reports progress on stderr, so run it through Start-Process rather than as a PowerShell native command
    $edgeArgs = @('--headless=new', '--disable-gpu', '--no-first-run', '--hide-scrollbars', '--allow-file-access-from-files',
        "--user-data-dir=`"$profileDir`"", '--window-size=1920,960', '--virtual-time-budget=2000', "--screenshot=`"$png`"", "`"$url`"")
    Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -WindowStyle Hidden
    if (-not (Test-Path $png)) { throw "Edge did not render $png" }
}

& (Join-Path $root 'make-images.ps1') -OutDir $out -Sizes 288, 512
# The README shows the thumbnail
Copy-Item (Join-Path $out 'thumbnail.png') (Join-Path $root 'marketplace\preview.png') -Force
Get-ChildItem $out | Select-Object Name, Length
