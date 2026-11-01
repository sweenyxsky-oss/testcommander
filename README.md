# TestCommander

A Windows desktop companion for Travian built with C#, WPF and Microsoft Edge WebView2.

## Current scope

- Embedded, visible WebView2 browser with a persistent browser profile.
- Passive observation of successful JSON responses from the currently displayed origin.
- Local SQLite storage for response metadata and cautiously extracted resource snapshots.
- A small activity view and village snapshot overview.
- TestCommander does not copy passwords or authentication headers into its own database. WebView2 retains the normal signed-in browser session in its separate local profile.

**This is a read-only companion.** It does not submit game actions, replay requests, automate clicks, randomize interaction timing, or attempt to evade game rules or bot detection. Any in-game action remains a manual action by the user.

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK (WPF target: `net10.0-windows`).
- Visual Studio 2026 with the .NET desktop development workload, or the .NET SDK.

## Build and run

From the repository root:

```powershell
dotnet restore .\TestCommander.sln
dotnet build .\TestCommander.sln -c Release
dotnet run --project .\src\TestCommander.Desktop\TestCommander.Desktop.csproj
```

The first run requires the Microsoft Edge WebView2 Runtime. It is preinstalled on most Windows 10/11 machines; otherwise install the Evergreen WebView2 Runtime.

## Local data

Data is stored under:

`%LOCALAPPDATA%\TestCommander\`

- `BrowserProfile\` — persistent WebView2 profile and signed-in session data.
- `testcommander.db` — SQLite response metadata and extracted snapshots.

The application deliberately does not persist response bodies, cookies, authorization headers, or request payloads. Treat the browser profile as sensitive because it contains your normal signed-in web session.

## Monitoring limitations

Travian's response formats can vary by game world, language, and release. The initial parser is heuristic and intentionally conservative: it only recognizes common resource field names in JSON. It does not yet understand every village, queue, troop movement, or attack format. The parser may need to be adapted using sanitized sample payloads; keep all future parsing read-only.

Only JSON responses from the exact origin currently displayed in the browser are inspected. HTTPS is required for remote sites; HTTP is permitted only for loopback development addresses. Response bodies are limited to 1 MB and are held in memory only long enough to parse.

## Project layout

- `src/TestCommander.Desktop/` — WPF desktop app.
- `Models/` — local models for observed data.
- `Services/` — SQLite persistence and passive WebView2 response monitoring.
- `ViewModels/` — UI state.

## Security notes

- The app never reads or exports browser cookies or authentication headers.
- It does not store raw response bodies or request payloads.
- Browser session data remains in the WebView2 profile under the current Windows user.
- The application contains no gameplay action dispatcher or automation subsystem.
