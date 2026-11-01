export {};

declare global {
  interface Window {
    commander: {
      dashboard: { summary: () => Promise<{ accounts: number; villages: number }> };
      accounts: {
        list: () => Promise<Account[]>;
        create: (input: { name: string; server: string }) => Promise<Account>;
      };
      villages: {
        list: () => Promise<Village[]>;
        create: (input: { accountId: number; name: string; x: number; y: number }) => Promise<Village>;
      };
    };
  }
  type Account = { id: number; name: string; server: string; createdAt?: string };
  type Village = { id: number; accountId: number; accountName: string; name: string; x: number; y: number; createdAt?: string };
}
