# TestCommander

A Windows desktop companion for Travian built with C#, WPF and Microsoft Edge WebView2.

## Current scope

- Embedded, visible WebView2 browser with a persistent profile.
- Passive observation of successful JSON responses from the currently displayed HTTPS origin.
- Local SQLite storage for response metadata and cautiously extracted resource snapshots.
- A small activity view and village snapshot overview.
- No game credentials are stored by TestCommander; WebView2 keeps its normal browser session data in the current Windows user profile.

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

The first run downloads the WebView2 runtime if required by the machine. On most Windows 10/11 installations the Evergreen WebView2 Runtime is already present. If missing, install the Microsoft Edge WebView2 Runtime.

## Local data

Data is stored under:

``%LOCALAPPDATA%\TestCommander\``

- `BrowserProfile\` — persistent WebView2 profile/cookies managed by WebView2.
- `testcommander.db` — SQLite metadata and extracted snapshots.

The application deliberately does not persist response bodies, cookies, authorization headers, or request payloads. Treat the browser profile as sensitive because it contains your normal signed-in web session.

## Monitoring limitations

Travian's response formats can vary by game world, language, and release. The initial parser is heuristic and intentionally conservative: it only recognizes common resource field names in JSON. It does not claim to understand every village, queue, troop movement, or attack format yet. Use the activity log to confirm that responses are being observed. Future parser improvements should be based on sanitized sample payloads and remain read-only.

## Project layout

- `src/TestCommander.Desktop/` — WPF desktop app.
- `Models/` — local view models for observed data.
- `Services/` — SQLite persistence and passive WebView2 response monitoring.
- `ViewModels/` — UI state.

## Security notes

- Browser monitoring is limited to the exact host currently open in the app.
- Only successful JSON responses are inspected; HTML, scripts, images, and other resources are ignored.
- Response bodies are held in memory only long enough to parse; the raw body is not written to disk.
- The app never reads or exports browser cookies or authentication headers.
- The application does not contain an action dispatcher or gameplay automation subsystem.
