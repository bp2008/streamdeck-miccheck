# Builds Mic Check (Native AOT) and assembles the Stream Deck plugin folder.
#   .\build.ps1                  -> dist\com.bp2008.miccheck.sdPlugin
#   .\build.ps1 -Pack            -> also validates and packs dist\com.bp2008.miccheck.streamDeckPlugin with the Stream Deck CLI
#   .\build.ps1 -Pack -Install   -> also installs that package into Stream Deck (closes and restarts the app)
# Requirements: .NET 10 SDK, Visual Studio C++ build tools (for Native AOT), Node.js (for the CLI via npx).
param([switch]$Pack, [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$uuid = 'com.bp2008.miccheck'
$cliVersion = '1.10.2'
$out = Join-Path $root "dist\$uuid.sdPlugin"
$package = Join-Path $root "dist\$uuid.streamDeckPlugin"

# Native AOT's linker step locates Visual Studio with vswhere.exe
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'MicCheck.csproj') -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Get-ChildItem $out -Filter *.pdb | Remove-Item
Copy-Item (Join-Path $root 'plugin\*') $out -Recurse -Force
& (Join-Path $root 'make-images.ps1') -OutDir (Join-Path $out 'imgs')
# The default key image is drawn by the plugin's own renderer
$tmp = Join-Path $root 'dist\key-tmp'
& (Join-Path $out 'MicCheck.exe') --svg $tmp | Out-Null
Copy-Item (Join-Path $tmp 'key.svg') (Join-Path $out 'imgs\key.svg')
Remove-Item $tmp -Recurse -Force
"Built $out"

if ($Pack -or $Install) {
    if (Test-Path $package) { Remove-Item $package -Force }
    npx --yes "@elgato/cli@$cliVersion" validate $out --no-update-check
    if ($LASTEXITCODE -ne 0) { throw "Stream Deck CLI validation failed" }
    npx --yes "@elgato/cli@$cliVersion" pack $out --output (Join-Path $root 'dist') --force --no-update-check
    if ($LASTEXITCODE -ne 0) { throw "Stream Deck CLI pack failed" }
    "Packed $package"
}

if ($Install) {
    $sdExe = 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe'
    $sd = Join-Path $env:APPDATA 'Elgato\StreamDeck'
    $dest = Join-Path $sd "Plugins\$uuid.sdPlugin"
    Get-Process -Name StreamDeck -ErrorAction SilentlyContinue | Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Process -Name StreamDeck, MicCheck -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Get-Process -Name MicCheck -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    # Install exactly what was packed (a .streamDeckPlugin is a zip of the .sdPlugin folder)
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $sd 'Plugins'))
    Start-Process $sdExe
    "Installed $package and restarted Stream Deck"
}
