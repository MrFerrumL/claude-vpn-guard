$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'ClaudeVpnGuard.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build first: .\Build.ps1' }
$results = Join-Path $PSScriptRoot 'test-results'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$report = Join-Path $results 'policy.txt'
$process = Start-Process -FilePath $exe -ArgumentList ('--self-test "' + $report + '"') -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Tests failed with exit code $($process.ExitCode)" }
$text = Get-Content -LiteralPath $report -Raw -Encoding UTF8
if ($text -notmatch '^PASS: 16 checks') { throw "Unexpected test result: $text" }
Write-Output $text
foreach ($name in @('Build.ps1','Install.ps1','Uninstall.ps1','Package.ps1')) {
    $parseErrors=$null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$null,[ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
}
Write-Output 'PASS: PowerShell script syntax.'
