$ErrorActionPreference = 'Stop'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ "/out:$PSScriptRoot\ClaudeVpnGuard.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\ClaudeVpnGuard.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
