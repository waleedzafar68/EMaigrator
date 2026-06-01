/* global React, I, Segmented, Badge */
const { useState: uSh } = React;

function Logo({ size = 26 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" fill="none" style={{ flexShrink: 0 }}>
      <rect x="1" y="1" width="30" height="30" rx="8" fill="var(--accent)" />
      <rect x="6.5" y="9.5" width="19" height="13" rx="2.5" fill="none" stroke="var(--accent-fg)" strokeWidth="1.8" />
      <path d="M7 11l9 6 9-6" fill="none" stroke="var(--accent-fg)" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M20.5 22.5h6m0 0-2.4-2.4m2.4 2.4-2.4 2.4" stroke="var(--accent)" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" transform="translate(0.2 0.2)" opacity="0" />
    </svg>
  );
}

const NAV = [
  { group: 'Overview', items: [{ key: 'dashboard', label: 'Dashboard', icon: I.dashboard }] },
  { group: 'Migrate', items: [
    { key: 'wizard', label: 'New migration', icon: I.migrate },
    { key: 'run', label: 'Live run', icon: I.runs, dot: true },
  ] },
  { group: 'Manage', items: [
    { key: 'batches', label: 'Batches', icon: I.batch, count: 3 },
    { key: 'audit', label: 'Results & audit', icon: I.audit },
  ] },
];

function Sidebar({ route, setRoute, persona, setPersona }) {
  const item = (it) => {
    const active = route === it.key || (it.key === 'run' && route === 'run');
    const [h, setH] = uSh ? [false] : [false];
    return (
      <button key={it.key} onClick={() => setRoute(it.key)}
        onMouseEnter={(e) => { if (!active) { e.currentTarget.style.background = 'var(--surface-2)'; e.currentTarget.style.color = 'var(--fg)'; } }}
        onMouseLeave={(e) => { if (!active) { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = 'var(--fg-muted)'; } }}
        style={{
          display: 'flex', alignItems: 'center', gap: 10, width: '100%',
          padding: '8px 9px', borderRadius: 'var(--radius)', border: 'none', cursor: 'pointer',
          fontSize: 13.5, fontWeight: active ? 600 : 500, textAlign: 'left',
          color: active ? 'var(--accent)' : 'var(--fg-muted)',
          background: active ? 'var(--accent-subtle)' : 'transparent',
          transition: 'background 150ms, color 150ms',
        }}>
        <span style={{ display: 'inline-flex', position: 'relative' }}>
          {it.icon({ size: 17 })}
          {it.dot && <span style={{ position: 'absolute', top: -2, right: -2, width: 7, height: 7, borderRadius: '50%', background: 'var(--accent)', border: '1.5px solid var(--surface)', animation: 'kk-pulse 1.6s infinite' }} />}
        </span>
        <span style={{ flex: 1 }}>{it.label}</span>
        {it.count != null && <Badge>{it.count}</Badge>}
      </button>
    );
  };

  return (
    <aside style={{
      width: 230, flexShrink: 0, background: 'var(--surface)', borderRight: '1px solid var(--border)',
      padding: 14, display: 'flex', flexDirection: 'column', gap: 2, height: '100%',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '2px 6px 16px', borderBottom: '1px solid var(--border)', marginBottom: 8 }}>
        <Logo size={28} />
        <div>
          <div style={{ fontWeight: 700, fontSize: 15.5, letterSpacing: '-0.02em' }}>EMaigrator</div>
          <div style={{ fontSize: 10.5, color: 'var(--fg-muted)', letterSpacing: '0.02em' }}>Email migration</div>
        </div>
      </div>

      {NAV.map((g) => (
        <div key={g.group}>
          <div style={{ fontSize: 10, fontWeight: 700, letterSpacing: '0.1em', textTransform: 'uppercase', color: 'var(--fg-subtle)', padding: '12px 8px 5px' }}>{g.group}</div>
          {g.items.map(item)}
        </div>
      ))}

      <div style={{ flex: 1 }} />

      {/* persona switch — the runtime expression of density-by-persona */}
      <div style={{ padding: 10, background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 'var(--radius)', marginBottom: 8 }}>
        <div style={{ fontSize: 10, fontWeight: 700, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-subtle)', marginBottom: 7 }}>Viewing as</div>
        <Segmented size="sm" value={persona} onChange={setPersona}
          options={[
            { value: 'individual', label: 'Just me', icon: I.user({ size: 13 }) },
            { value: 'msp', label: 'Admin', icon: I.users({ size: 13 }) },
          ]} />
      </div>

      <button onClick={() => setRoute('settings')}
        style={{
          display: 'flex', alignItems: 'center', gap: 10, width: '100%', padding: '8px 9px',
          borderRadius: 'var(--radius)', border: 'none', cursor: 'pointer', fontSize: 13.5, fontWeight: 500, textAlign: 'left',
          color: route === 'settings' ? 'var(--accent)' : 'var(--fg-muted)', background: route === 'settings' ? 'var(--accent-subtle)' : 'transparent',
        }}>
        <I.settings size={17} /><span>Settings</span>
      </button>

      <div style={{ borderTop: '1px solid var(--border)', marginTop: 8, paddingTop: 10, display: 'flex', alignItems: 'center', gap: 10 }}>
        <div style={{ width: 30, height: 30, borderRadius: '50%', background: 'var(--accent)', color: 'var(--accent-fg)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 700 }}>
          {persona === 'msp' ? 'TM' : 'HC'}
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{persona === 'msp' ? 'Tom Mercer' : 'Harold Conway'}</div>
          <div style={{ fontSize: 10.5, color: 'var(--fg-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{persona === 'msp' ? 'BrightStack MSP' : 'Conway Law'}</div>
        </div>
      </div>
    </aside>
  );
}

function ThemeToggle({ theme, setTheme }) {
  return (
    <Segmented size="sm" value={theme} onChange={setTheme}
      options={[
        { value: 'light', icon: I.sun({ size: 14 }), title: 'Light' },
        { value: 'dark', icon: I.moon({ size: 14 }), title: 'Dark' },
        { value: 'system', icon: I.monitor({ size: 14 }), title: 'System' },
      ]} />
  );
}

function TopBar({ title, subtitle, actions, theme, setTheme, density, setDensity }) {
  return (
    <div style={{
      minHeight: 58, borderBottom: '1px solid var(--border)', padding: '0 24px',
      display: 'flex', alignItems: 'center', gap: 16, background: 'color-mix(in oklab, var(--bg) 70%, transparent)',
      backdropFilter: 'blur(8px)', flexShrink: 0, position: 'sticky', top: 0, zIndex: 5,
    }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        <h1 style={{ margin: 0, fontSize: 'var(--fs-h2)', fontWeight: 600, letterSpacing: '-0.015em', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{title}</h1>
        {subtitle && <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-muted)', marginTop: 1 }}>{subtitle}</div>}
      </div>
      <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
        {actions}
        <div style={{ width: 1, height: 24, background: 'var(--border)' }} />
        <Segmented size="sm" value={density} onChange={setDensity}
          options={[
            { value: 'comfortable', icon: I.rows({ size: 14 }), title: 'Comfortable' },
            { value: 'compact', icon: I.grid({ size: 14 }), title: 'Compact' },
          ]} />
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </div>
    </div>
  );
}

Object.assign(window, { Sidebar, TopBar, Logo });
