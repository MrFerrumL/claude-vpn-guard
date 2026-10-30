# Claude VPN Guard

A small Windows tray utility that checks a VPN tunnel before launching Claude Desktop and closes the app after a sustained VPN failure.

[Русский README](README.md) · [Downloads](https://github.com/MrFerrumL/claude-vpn-guard/releases/latest) · [MIT license](LICENSE)

## Features

- Checked launcher for the Microsoft Store version of Claude Desktop.
- Configurable 5–10 second grace period; recovery resets the timer.
- Multiple VPN clients, including v2RayTun and MantaRay in full-tunnel TUN/TAP mode.
- Automatic selection by adapter driver description and the active IPv4 route, plus explicit adapter names separated by `;`.
- Certificate-validated TLS 1.2 probe to `claude.ai:443`, bound to the selected tunnel address.
- Tray controls, per-user startup and local logs. No third-party dependencies or telemetry.

**This is an application watchdog, not a network kill switch.** Other launch methods may briefly start Claude before it is detected. Network traffic is not blocked during the grace period. Forced termination can lose unsaved input. Use the VPN's kill switch or firewall rules for network enforcement.

## Install

Download and extract the Windows ZIP from Releases, then run `Install.cmd`. Connect your VPN in full-tunnel mode and launch Claude using the new **Claude VPN** shortcut. A system proxy alone is unsupported.

Requires Windows 10/11 x64, .NET Framework 4.8 and Microsoft Store Claude Desktop. The guard does not require administrator privileges. The executable is unsigned.

Double-click the tray icon to launch Claude. Right-click for settings, logs or exit. Settings and logs live in `%LOCALAPPDATA%\ClaudeVpnGuard`. Run `Remove.cmd` to uninstall. The guard runs while the user is logged in; exiting or terminating it stops monitoring.

## Build

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1
```

The built-in .NET Framework C# compiler is used. CI runs on Windows. Automated tests do not disconnect a real VPN or terminate Claude. See [CHANGELOG.md](CHANGELOG.md) for manual validation status.

## Limits

The guard samples adapters and processes every 250 ms. A connectivity probe follows 1.5 seconds after the previous one completes. A stalled tunnel may take approximately 5 seconds to detect, in addition to the grace period. DNS, internet and TLS failures are treated as an unconfirmed VPN.

IPv4 probes do not enforce Claude's IPv6 routing or rules inside the VPN client. Standalone Claude Code is excluded. The TLS probe sends no HTTP requests, credentials or chat content; online certificate revocation checking is disabled for this probe. Driver recognition does not authenticate the VPN provider. This project is independent of Anthropic and VPN vendors.
