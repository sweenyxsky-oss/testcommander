import { contextBridge, ipcRenderer } from 'electron';

const api = {
  dashboard: {
    summary: () => ipcRenderer.invoke('dashboard:summary')
  },
  accounts: {
    list: () => ipcRenderer.invoke('accounts:list'),
    create: (input: { name: string; server: string }) => ipcRenderer.invoke('accounts:create', input)
  },
  villages: {
    list: () => ipcRenderer.invoke('villages:list'),
    create: (input: { accountId: number; name: string; x: number; y: number }) =>
      ipcRenderer.invoke('villages:create', input)
  }
};

contextBridge.exposeInMainWorld('commander', api);
