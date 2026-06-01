/* global React, Card, CardTitle, Button, Segmented, StatusChip, Badge, I, Sidebar, TopBar, ThemeToggle, ToastProvider, Dashboard, Wizard, RunView, Batches, Audit, providerById,
   useTweaks, TweaksPanel, TweakSection, TweakRadio, TweakSelect, TweakToggle */
const { useState: uApp, useEffect: uAppE, useMemo: uAppM } = React;

const ACCENTS = {
  teal:    { light: { a: '#0d9488', h: '#0f766e', s: '#f0fdfa', l: '#99f6e4', fg: '#ffffff', halo: '#14b8a6' }, dark: { a: '#2dd4bf', h: '#5eead4', s: '#134e4a', l: '#115e59', fg: '#042f2e', halo: '#2dd4bf' } },
  emerald: { light: { a: '#059669', h: '#047857', s: '#ecfdf5', l: '#a7f3d0', fg: '#ffffff', halo: '#10b981' }, dark: { a: '#34d399', h: '#6ee7b7', s: '#064e3b', l: '#065f46', fg: '#022c22', halo: '#34d399' } },
};

const PAGES = {
  dashboard: { title: 'Dashboard' },
  wizard: { title: 'New migration' },
  run: { title: 'Live run' },
  batches: { title: 'Batches' },
  audit: { title: 'Results & audit' },
  settings: { title: 'Settings' },
};

function Settings({ t, setTweak }) {
  return (
    <div style={{ maxWidth: 640, margin: '0 auto', display: 'grid', gap: 'var(--section-gap)', animation: 'kk-fade-up 220ms ease-out' }}>
      <Card>
        <CardTitle sub="These mirror the Tweaks panel — change them anywhere.">Appearance</CardTitle>
        <div style={{ display: 'grid', gap: 16 }}>
          <Row label="Theme" hint="Light, dark, or follow your system.">
            <Segmented size="sm" value={t.theme} onChange={(v) => setTweak('theme', v)} options={[{ value: 'light', label: 'Light' }, { value: 'dark', label: 'Dark' }, { value: 'system', label: 'System' }]} />
          </Row>
          <Row label="Density" hint="Comfortable for everyday use; compact for power users.">
            <Segmented size="sm" value={t.density} onChange={(v) => setTweak('density', v)} options={[{ value: 'comfortable', label: 'Comfortable' }, { value: 'compact', label: 'Compact' }]} />
          </Row>
          <Row label="Surface style" hint="Calm is flat; vivid adds an accent halo and tint.">
            <Segmented size="sm" value={t.vibe} onChange={(v) => setTweak('vibe', v)} options={[{ value: 'calm', label: 'Calm' }, { value: 'vivid', label: 'Vivid' }]} />
          </Row>
        </div>
      </Card>
      <Card>
        <CardTitle sub="We email this address when migrations finish or need a decision.">Notifications</CardTitle>
        <div style={{ display: 'grid', gap: 4 }}>
          {[['Migration completed', true], ['Decision required', true], ['Throttling started', false], ['Weekly summary (admins)', true]].map(([l, on]) => (
            <Row key={l} label={l}><Toggle defaultOn={on} /></Row>
          ))}
        </div>
      </Card>
    </div>
  );
}
function Row({ label, hint, children }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16, padding: '8px 0', borderTop: '1px solid var(--border)' }}>
      <div><div style={{ fontSize: 13.5, fontWeight: 500 }}>{label}</div>{hint && <div style={{ fontSize: 12, color: 'var(--fg-muted)', marginTop: 2 }}>{hint}</div>}</div>
      {children}
    </div>
  );
}
function Toggle({ defaultOn }) {
  const [on, setOn] = uApp(!!defaultOn);
  return (
    <button onClick={() => setOn(!on)} style={{ width: 38, height: 22, borderRadius: 'var(--radius-full)', border: 'none', cursor: 'pointer', background: on ? 'var(--accent)' : 'var(--border-strong)', position: 'relative', transition: 'background 150ms', flexShrink: 0 }}>
      <span style={{ position: 'absolute', top: 2, left: on ? 18 : 2, width: 18, height: 18, borderRadius: '50%', background: '#fff', transition: 'left 150ms', boxShadow: '0 1px 2px rgba(0,0,0,0.3)' }} />
    </button>
  );
}

const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "theme": "system",
  "persona": "individual",
  "density": "comfortable",
  "vibe": "calm",
  "accent": "teal",
  "dashLayout": "cards",
  "wizardPattern": "stepped"
}/*EDITMODE-END*/;

function App() {
  const [t, setTweak] = useTweaks(TWEAK_DEFAULTS);
  const [route, setRoute] = uApp(() => { try { return localStorage.getItem('em-route') || 'dashboard'; } catch { return 'dashboard'; } });
  const [conn, setConn] = uApp('live');
  uAppE(() => { try { localStorage.setItem('em-route', route); } catch {} }, [route]);

  // resolve system theme
  const [sysDark, setSysDark] = uApp(() => window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  uAppE(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const fn = (e) => setSysDark(e.matches);
    mq.addEventListener('change', fn); return () => mq.removeEventListener('change', fn);
  }, []);
  const resolvedTheme = t.theme === 'system' ? (sysDark ? 'dark' : 'light') : t.theme;

  uAppE(() => {
    const r = document.documentElement;
    r.classList.add('theme-anim-off');
    r.dataset.theme = resolvedTheme;
    r.dataset.density = t.density;
    r.dataset.vibe = t.vibe;
    const ac = (ACCENTS[t.accent] || ACCENTS.teal)[resolvedTheme];
    r.style.setProperty('--accent', ac.a); r.style.setProperty('--accent-hover', ac.h);
    r.style.setProperty('--accent-subtle', ac.s); r.style.setProperty('--accent-line', ac.l);
    r.style.setProperty('--accent-fg', ac.fg); r.style.setProperty('--halo-color', ac.halo);
    const id = requestAnimationFrame(() => requestAnimationFrame(() => r.classList.remove('theme-anim-off')));
    return () => cancelAnimationFrame(id);
  }, [resolvedTheme, t.density, t.vibe, t.accent]);

  const go = (key) => setRoute(key);
  const persona = t.persona;
  const page = PAGES[route] || PAGES.dashboard;

  const subtitle = {
    dashboard: persona === 'msp' ? 'BrightStack MSP · 3 active batches' : 'Your mailbox migration',
    wizard: 'Connect, test, and start a migration',
    run: persona === 'msp' ? 'Streaming over SignalR · live' : 'Watch your mail move, live',
    batches: 'All client migrations',
    audit: 'Every event, exportable',
    settings: 'Preferences',
  }[route];

  const actions = route === 'dashboard' && persona === 'msp'
    ? <Button variant="primary" size="sm" icon={<I.plus size={14} />} onClick={() => go('wizard')}>New migration</Button>
    : route === 'run'
      ? <Button variant="ghost" size="sm" icon={conn === 'live' ? <I.wifi size={14} /> : <I.wifiOff size={14} />} onClick={() => { setConn('reconnecting'); setTimeout(() => setConn('live'), 2600); }}>{conn === 'live' ? 'Simulate drop' : 'Reconnecting…'}</Button>
      : null;

  let content;
  if (route === 'dashboard') content = <Dashboard persona={persona} layout={t.dashLayout} go={go} />;
  else if (route === 'wizard') content = <Wizard pattern={t.wizardPattern} persona={persona} go={go} />;
  else if (route === 'run') content = <RunView persona={persona} conn={conn} setConn={setConn} />;
  else if (route === 'batches') content = <Batches go={go} />;
  else if (route === 'audit') content = <Audit />;
  else if (route === 'settings') content = <Settings t={t} setTweak={setTweak} />;

  return (
    <div style={{ display: 'flex', height: '100vh', position: 'relative', overflow: 'hidden' }}>
      <div style={{ position: 'absolute', width: 520, height: 520, borderRadius: '50%', filter: 'blur(130px)', background: 'var(--halo-color)', opacity: 'var(--halo-opacity)', top: -260, left: -160, pointerEvents: 'none', zIndex: 0, transition: 'opacity 300ms' }} />
      <div style={{ position: 'absolute', width: 520, height: 520, borderRadius: '50%', filter: 'blur(130px)', background: 'var(--halo-color)', opacity: 'var(--halo-opacity)', bottom: -260, right: -160, pointerEvents: 'none', zIndex: 0, transition: 'opacity 300ms' }} />
      <div style={{ position: 'relative', zIndex: 1, display: 'flex', width: '100%' }}>
        <Sidebar route={route} setRoute={setRoute} persona={persona} setPersona={(v) => setTweak('persona', v)} />
        <main style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, overflow: 'hidden' }}>
          <TopBar title={page.title} subtitle={subtitle} actions={actions} theme={t.theme} setTheme={(v) => setTweak('theme', v)} density={t.density} setDensity={(v) => setTweak('density', v)} />
          <div style={{ flex: 1, overflowY: 'auto', overflowX: 'hidden', padding: 24 }}>{content}</div>
        </main>
      </div>

      <TweaksPanel>
        <TweakSection label="Who's using it" />
        <TweakRadio label="Persona" value={t.persona} options={[{ value: 'individual', label: 'Individual' }, { value: 'msp', label: 'Admin / MSP' }]} onChange={(v) => setTweak('persona', v)} />
        <TweakRadio label="Density" value={t.density} options={[{ value: 'comfortable', label: 'Comfy' }, { value: 'compact', label: 'Compact' }]} onChange={(v) => setTweak('density', v)} />
        <TweakSection label="Theme & style" />
        <TweakSelect label="Theme" value={t.theme} options={[{ value: 'system', label: 'System' }, { value: 'light', label: 'Light' }, { value: 'dark', label: 'Dark' }]} onChange={(v) => setTweak('theme', v)} />
        <TweakRadio label="Surface" value={t.vibe} options={[{ value: 'calm', label: 'Calm' }, { value: 'vivid', label: 'Vivid' }]} onChange={(v) => setTweak('vibe', v)} />
        <TweakRadio label="Accent" value={t.accent} options={[{ value: 'teal', label: 'Teal' }, { value: 'emerald', label: 'Emerald' }]} onChange={(v) => setTweak('accent', v)} />
        <TweakSection label="Layouts" />
        <TweakRadio label="Dashboard" value={t.dashLayout} options={[{ value: 'cards', label: 'Cards' }, { value: 'list', label: 'List' }]} onChange={(v) => setTweak('dashLayout', v)} />
        <TweakSelect label="Wizard pattern" value={t.wizardPattern} options={[{ value: 'stepped', label: 'Stepped (top)' }, { value: 'siderail', label: 'Side rail' }, { value: 'single', label: 'Single scroll' }]} onChange={(v) => setTweak('wizardPattern', v)} />
      </TweaksPanel>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<ToastProvider><App /></ToastProvider>);
Object.assign(window, { Settings });
