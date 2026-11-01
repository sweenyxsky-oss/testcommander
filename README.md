# TestCommander

A Windows desktop companion for Travian built with C#, WPF and Microsoft Edge WebView2.

## Current scope

- Embedded, visible WebView2 browser with a persistent browser profile.
- Passive observation of successful JSON responses from the currently displayed origin.
- Local SQLite storage for response metadata and cautiously extracted resource snapshots.
- A small activity view and village snapshot overview.
- A conservative resource parser with unit tests.

**This is a read-only companion.** It does not submit game actions, replay requests, automate clicks, randomize interaction timing, or attempt to evade game rules or bot detection. Any in-game action remains a manual action by the user.

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK.
- Microsoft Edge WebView2 Runtime.

## Build, test and run

```powershell
dotnet restore .\TestCommander.sln
dotnet build .\TestCommander.sln -c Release
dotnet test .\TestCommander.sln -c Release
dotnet run --project .\src\TestCommander.Desktop\TestCommander.Desktop.csproj
```

## Local data

Data is stored under `%LOCALAPPDATA%\TestCommander\`:
- `BrowserProfile\` — persistent WebView2 profile and signed-in session data.
- `testcommander.db` — SQLite response metadata and extracted snapshots.

The app deliberately does not persist response bodies, cookies, authorization headers, or request payloads. Treat the browser profile as sensitive because it contains your normal signed-in web session.

## Monitoring limitations

Travian response formats can vary by world, language and release. The parser only accepts explicitly named resource objects (for example, `resources`) or resource fields at the top level. It deliberately avoids recursively matching arbitrary fields such as building costs, queue rewards or troop payloads. This reduces false positives but means some game responses will not be recognized until the parser is extended using sanitized samples.

Only JSON responses from the exact origin currently displayed in the browser are inspected. Response bodies are limited to 1 MB and held in memory only long enough to parse. No request payloads, cookies, authorization headers or response bodies are stored.

## Project layout

- `src/TestCommander.Desktop/` — WPF application.
- `src/TestCommander.Desktop/Models/` — observed data models.
- `src/TestCommander.Desktop/Services/` — SQLite persistence, passive monitoring and parser.
- `src/TestCommander.Desktop/ViewModels/` — UI state.
- `tests/TestCommander.Tests/` — parser unit tests.
