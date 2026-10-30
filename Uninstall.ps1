$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ClaudeVpnGuard'))
$exe = Join-Path $destination 'ClaudeVpnGuard.exe'
foreach ($process in @(Get-Process -Name ClaudeVpnGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) {
    Stop-Process -InputObject $process
    if (!$process.WaitForExit(5000)) { throw 'The running guard did not stop; retry removal.' }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClaudeVpnGuard -ErrorAction SilentlyContinue
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $link = Join-Path $folder 'Claude VPN.lnk'
    if ((Test-Path -LiteralPath $link) -and ($shell.CreateShortcut($link).TargetPath -eq $exe)) { Remove-Item -LiteralPath $link }
}
# Remove only this utility's named files; do not recursively delete directories.
foreach ($name in @('ClaudeVpnGuard.exe','settings.xml','guard.log','README.txt','Remove.cmd','Uninstall.ps1')) {
    $file = Join-Path $destination $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
if ((Test-Path -LiteralPath $destination) -and !(Get-ChildItem -LiteralPath $destination -Force)) { Remove-Item -LiteralPath $destination }
Write-Output 'Claude VPN Guard removed.'
