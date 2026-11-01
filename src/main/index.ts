import { app, BrowserWindow, ipcMain } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Database from 'better-sqlite3';

const here = path.dirname(fileURLToPath(import.meta.url));
let db: Database.Database;

type AccountInput = { name: string; server: string };
type VillageInput = { accountId: number; name: string; x: number; y: number };

function initializeDatabase() {
  const dbPath = path.join(app.getPath('userData'), 'testcommander.db');
  db = new Database(dbPath);
  db.pragma('journal_mode = WAL');
  db.pragma('foreign_keys = ON');
  db.exec(`
    CREATE TABLE IF NOT EXISTS accounts (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT NOT NULL,
      server TEXT NOT NULL,
      created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS villages (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
      name TEXT NOT NULL,
      x INTEGER NOT NULL,
      y INTEGER NOT NULL,
      created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
    );
  `);
}

function registerIpc() {
  ipcMain.handle('dashboard:summary', () => ({
    accounts: (db.prepare('SELECT COUNT(*) AS count FROM accounts').get() as { count: number }).count,
    villages: (db.prepare('SELECT COUNT(*) AS count FROM villages').get() as { count: number }).count
  }));

  ipcMain.handle('accounts:list', () =>
    db.prepare('SELECT id, name, server, created_at AS createdAt FROM accounts ORDER BY id DESC').all()
  );

  ipcMain.handle('accounts:create', (_event, input: AccountInput) => {
    const name = String(input?.name ?? '').trim();
    const server = String(input?.server ?? '').trim();
    if (!name || !server) throw new Error('Account name and server are required.');
    const result = db.prepare('INSERT INTO accounts (name, server) VALUES (?, ?)').run(name, server);
    return db.prepare('SELECT id, name, server, created_at AS createdAt FROM accounts WHERE id = ?').get(result.lastInsertRowid);
  });

  ipcMain.handle('villages:list', () =>
    db.prepare(`SELECT v.id, v.account_id AS accountId, a.name AS accountName,
      v.name, v.x, v.y, v.created_at AS createdAt
      FROM villages v JOIN accounts a ON a.id = v.account_id ORDER BY v.id DESC`).all()
  );

  ipcMain.handle('villages:create', (_event, input: VillageInput) => {
    const accountId = Number(input?.accountId);
    const name = String(input?.name ?? '').trim();
    const x = Number(input?.x);
    const y = Number(input?.y);
    if (!Number.isInteger(accountId) || accountId < 1 || !name ||
        !Number.isInteger(x) || !Number.isInteger(y) || x < -400 || x > 400 || y < -400 || y > 400) {
      throw new Error('Choose an account, enter a village name, and use valid map coordinates (-400 to 400).');
    }
    const result = db.prepare('INSERT INTO villages (account_id, name, x, y) VALUES (?, ?, ?, ?)').run(accountId, name, x, y);
    return db.prepare('SELECT id, account_id AS accountId, name, x, y FROM villages WHERE id = ?').get(result.lastInsertRowid);
  });
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 980,
    minHeight: 680,
    backgroundColor: '#10151d',
    title: 'TestCommander',
    webPreferences: {
      preload: path.join(here, '../preload/index.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  if (process.env.ELECTRON_RENDERER_URL) {
    void window.loadURL(process.env.ELECTRON_RENDERER_URL);
  } else {
    void window.loadFile(path.join(here, '../renderer/index.html'));
  }
}

app.whenReady().then(() => {
  initializeDatabase();
  registerIpc();
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

app.on('before-quit', () => {
  db?.close();
});
