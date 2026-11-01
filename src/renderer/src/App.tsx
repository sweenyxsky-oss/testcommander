import { FormEvent, useEffect, useState } from 'react';

type Section = 'overview' | 'accounts' | 'villages';

export default function App() {
  const [section, setSection] = useState<Section>('overview');
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [villages, setVillages] = useState<Village[]>([]);
  const [summary, setSummary] = useState({ accounts: 0, villages: 0 });
  const [error, setError] = useState('');
  const [accountName, setAccountName] = useState('');
  const [server, setServer] = useState('');
  const [villageName, setVillageName] = useState('');
  const [accountId, setAccountId] = useState('');
  const [x, setX] = useState('0');
  const [y, setY] = useState('0');

  async function refresh() {
    const [a, v, s] = await Promise.all([
      window.commander.accounts.list(),
      window.commander.villages.list(),
      window.commander.dashboard.summary()
    ]);
    setAccounts(a); setVillages(v); setSummary(s);
    if (!accountId && a.length) setAccountId(String(a[0].id));
  }

  useEffect(() => { refresh().catch((e) => setError(e.message)); }, []);

  async function addAccount(event: FormEvent) {
    event.preventDefault(); setError('');
    try {
      await window.commander.accounts.create({ name: accountName, server });
      setAccountName(''); setServer(''); await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not add account.'); }
  }

  async function addVillage(event: FormEvent) {
    event.preventDefault(); setError('');
    try {
      await window.commander.villages.create({ accountId: Number(accountId), name: villageName, x: Number(x), y: Number(y) });
      setVillageName(''); setX('0'); setY('0'); await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not add village.'); }
  }

  return <div className="shell">
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark">TC</div><div><strong>TestCommander</strong><span>TRAVIAN COMPANION</span></div></div>
      <div className="nav-label">WORKSPACE</div>
      <button className={section === 'overview' ? 'nav active' : 'nav'} onClick={() => setSection('overview')}>▦ <span>Overview</span></button>
      <button className={section === 'accounts' ? 'nav active' : 'nav'} onClick={() => setSection('accounts')}>◉ <span>Accounts</span><small>{accounts.length}</small></button>
      <button className={section === 'villages' ? 'nav active' : 'nav'} onClick={() => setSection('villages')}>⌂ <span>Villages</span><small>{villages.length}</small></button>
      <div className="sidebar-bottom"><span className="status-dot"/> Local database connected<span className="version">VERSION 0.1.0</span></div>
    </aside>
    <main className="main">
      <header className="topbar"><div><span className="eyebrow">COMMAND CENTER / {section.toUpperCase()}</span><h1>{section === 'overview' ? 'Overview' : section === 'accounts' ? 'Accounts' : 'Villages'}</h1></div><div className="local-pill"><span className="status-dot"/> Running locally</div></header>
      {error && <div className="error">{error}</div>}
      {section === 'overview' && <>
        <section className="welcome"><div><span className="eyebrow accent">YOUR STRATEGIC WORKSPACE</span><h2>Plan smarter.<br/><em>Play your way.</em></h2><p>Your local command center for organizing Travian accounts and villages. More planning tools are coming next.</p><button className="primary" onClick={() => setSection('accounts')}>Manage accounts <span>→</span></button></div><div className="globe"><div className="orbit orbit-one"/><div className="orbit orbit-two"/><div className="globe-core">✦</div><span className="coord coord-a">X: 42</span><span className="coord coord-b">Y: -18</span></div></section>
        <div className="section-heading"><div><span className="eyebrow">AT A GLANCE</span><h3>Workspace summary</h3></div><span className="muted">Stored on this device</span></div>
        <div className="stats"><div className="stat-card"><span className="stat-icon">◉</span><span className="stat-label">ACCOUNTS</span><strong>{summary.accounts}</strong><span className="stat-foot">Tracked game accounts</span></div><div className="stat-card"><span className="stat-icon">⌂</span><span className="stat-label">VILLAGES</span><strong>{summary.villages}</strong><span className="stat-foot">Villages in your workspace</span></div><div className="stat-card"><span className="stat-icon">⌖</span><span className="stat-label">STORAGE</span><strong>Local</strong><span className="stat-foot">SQLite · Private to this PC</span></div></div>
        <div className="section-heading"><div><span className="eyebrow">GET STARTED</span><h3>Build your workspace</h3></div></div>
        <div className="quick-grid"><button onClick={() => setSection('accounts')}><b>01</b><strong>Add an account</strong><span>Save a label and game server</span><i>→</i></button><button onClick={() => setSection('villages')}><b>02</b><strong>Register a village</strong><span>Keep coordinates organized</span><i>→</i></button></div>
      </>}
      {section === 'accounts' && <div className="content-grid"><section className="panel"><div className="panel-title"><div><span className="eyebrow">ACCOUNT MANAGEMENT</span><h3>Add an account</h3></div></div><form onSubmit={addAccount}><label>Account label<input value={accountName} onChange={e => setAccountName(e.target.value)} placeholder="e.g. Main account" required /></label><label>Game server<input value={server} onChange={e => setServer(e.target.value)} placeholder="e.g. ts1.x1.international" required /></label><button className="primary" type="submit">Save account <span>→</span></button></form></section><section className="panel list-panel"><div className="panel-title"><div><span className="eyebrow">YOUR WORKSPACE</span><h3>Saved accounts</h3></div><span className="count">{accounts.length}</span></div>{accounts.length ? accounts.map(a => <div className="list-row" key={a.id}><div className="row-icon">◉</div><div><strong>{a.name}</strong><span>{a.server}</span></div><small>#{a.id}</small></div>) : <div className="empty">No accounts yet. Add your first account to get started.</div>}</section></div>}
      {section === 'villages' && <div className="content-grid"><section className="panel"><div className="panel-title"><div><span className="eyebrow">VILLAGE MANAGEMENT</span><h3>Add a village</h3></div></div>{accounts.length ? <form onSubmit={addVillage}><label>Linked account<select value={accountId} onChange={e => setAccountId(e.target.value)} required>{accounts.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label><label>Village name<input value={villageName} onChange={e => setVillageName(e.target.value)} placeholder="e.g. Capital" required /></label><div className="coordinate-fields"><label>X coordinate<input type="number" min="-400" max="400" value={x} onChange={e => setX(e.target.value)} required /></label><label>Y coordinate<input type="number" min="-400" max="400" value={y} onChange={e => setY(e.target.value)} required /></label></div><button className="primary" type="submit">Save village <span>→</span></button></form> : <div className="empty">Create an account first, then you can add villages to it.</div>}</section><section className="panel list-panel"><div className="panel-title"><div><span className="eyebrow">YOUR WORKSPACE</span><h3>Saved villages</h3></div><span className="count">{villages.length}</span></div>{villages.length ? villages.map(v => <div className="list-row" key={v.id}><div className="row-icon">⌂</div><div><strong>{v.name}</strong><span>{v.accountName} · ({v.x}|{v.y})</span></div><small>#{v.id}</small></div>) : <div className="empty">No villages registered yet.</div>}</section></div>}
      <footer>TESTCOMMANDER <span>Independent companion project · Not affiliated with Travian Games</span></footer>
    </main>
  </div>;
}
