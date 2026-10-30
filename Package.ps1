param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric x.y.z version' }
$dist = Join-Path $PSScriptRoot 'dist'
$bundle = Join-Path $dist "ClaudeVpnGuard-$Version-windows"
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
foreach ($name in @('ClaudeVpnGuard.exe','Install.ps1','Install.cmd','Uninstall.ps1','Remove.cmd','README.txt','README.md','README.en.md','LICENSE','CHANGELOG.md','settings.xml')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $bundle $name) -Force
}
$zip = Join-Path $dist "ClaudeVpnGuard-$Version-windows.zip"
New-Item -ItemType Directory -Path (Join-Path $bundle 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\banner.svg') -Destination (Join-Path $bundle 'docs\banner.svg') -Force
Compress-Archive -LiteralPath $bundle -DestinationPath $zip -Force
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output $zip
