# Renders art/preview.html to preview.png (512x512) with headless Microsoft Edge.
# The game requires a square PNG of at least 512x512 and at most 1 MB.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$html = Join-Path $PSScriptRoot "preview.html"
$png = Join-Path $root "preview.png"

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge not found." }

$uri = ([Uri]$html).AbsoluteUri
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --window-size=512,512 --screenshot="$png" $uri | Out-Null
Start-Sleep -Milliseconds 500

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($png)
"preview.png: $($img.Width)x$($img.Height), $([Math]::Round((Get-Item $png).Length / 1KB)) KB"
$img.Dispose()
