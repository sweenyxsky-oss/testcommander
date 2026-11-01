# TestCommander

A Windows-first Travian companion desktop app. This project is an independent community tool and is not affiliated with or endorsed by Travian Games.

## Stack
- Electron desktop shell
- React + TypeScript renderer
- SQLite database stored in the current Windows user's application data folder
- Secure IPC bridge (renderer has no direct Node.js access)

## Requirements
- Windows 10/11 (64-bit)
- Node.js 22 LTS or newer
- npm

## Run in development
```powershell
npm install
npm run dev
```

## Build a Windows installer
```powershell
npm run dist:win
```
The NSIS installer is written to `release/`. The application database is kept separately in Electron's `userData` folder, so reinstalling an update does not overwrite it.

## Initial features
- Local dashboard
- Create and view Travian accounts
- Create and view villages linked to accounts
- SQLite persistence
- No Docker, PostgreSQL, Python, or hosted service required

## Data and security
All data is stored locally on the Windows machine. The renderer uses a restricted preload bridge and does not receive direct filesystem or database access. Do not store your Travian password in this app. Account records currently store a label and server address only.

## Project layout
- `src/main` — Electron main process and SQLite access
- `src/preload` — typed, restricted IPC bridge
- `src/renderer` — React user interface

## Roadmap
1. Improve account and village editing/deletion
2. Add village resource and troop tracking
3. Add build queue and production planning
4. Add map, distance, and travel-time tools
5. Add import/export and scheduled reminders

The initial version is a local organizer and planning foundation. It does not automate gameplay or interact with Travian servers.
