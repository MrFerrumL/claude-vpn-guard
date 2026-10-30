$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'ClaudeVpnGuard'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$exe = Join-Path $destination 'ClaudeVpnGuard.exe'
foreach ($process in @(Get-Process -Name ClaudeVpnGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) {
    Stop-Process -InputObject $process
    if (!$process.WaitForExit(5000)) { throw 'The running guard did not stop; retry installation.' }
}
foreach ($name in @('ClaudeVpnGuard.exe', 'Uninstall.ps1', 'Remove.cmd', 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name) -Force
}
if (!(Test-Path -LiteralPath (Join-Path $destination 'settings.xml'))) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'settings.xml') -Destination $destination
}
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Claude VPN.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.Arguments = '--launch'
    $shortcut.WorkingDirectory = $destination
    $shortcut.Description = 'Launch Claude after checking the VPN tunnel'
    $shortcut.Save()
}
New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClaudeVpnGuard -Value ('"' + $exe + '"') -PropertyType String -Force | Out-Null
Start-Process -FilePath $exe -WindowStyle Hidden
Write-Output "Installed: $exe"
