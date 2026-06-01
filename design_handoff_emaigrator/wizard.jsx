/* global React, Card, Button, Field, Input, Select, StatusChip, Badge, Progress, Spinner, Collapsible, I, PROVIDERS, providerById, ISSUES, useToast */
const { useState: uWz } = React;

const STEPS = [
  { key: 'source', label: 'Source', icon: I.server, desc: 'Where mail lives now' },
  { key: 'dest',   label: 'Destination', icon: I.cloud, desc: 'Where it’s going' },
  { key: 'test',   label: 'Test', icon: I.shield, desc: 'Verify the connection' },
  { key: 'review', label: 'Review', icon: I.eye, desc: 'Resolve issues' },
  { key: 'run',    label: 'Start', icon: I.play, desc: 'Confirm & migrate' },
];

/* ---- small Alert (visual form of the error pattern) ---- */
function Alert({ tone = 'info', title, children, action }) {
  const map = {
    success: { c: 'var(--success)', bg: 'var(--success-bg)', bd: 'var(--success-line)', Icon: I.checkCircle },
    error:   { c: 'var(--error)', bg: 'var(--error-bg)', bd: 'var(--error-line)', Icon: I.xCircle },
    warning: { c: 'var(--throttled)', bg: 'var(--throttled-bg)', bd: 'var(--throttled-line)', Icon: I.alert },
    info:    { c: 'var(--accent)', bg: 'var(--accent-subtle)', bd: 'var(--accent-line)', Icon: I.info },
  }[tone];
  const Ic = map.Icon;
  return (
    <div style={{ display: 'flex', gap: 11, padding: 14, background: map.bg, border: `1px solid ${map.bd}`, borderRadius: 'var(--radius)' }}>
      <span style={{ color: map.c, marginTop: 1, display: 'inline-flex' }}><Ic size={17} /></span>
      <div style={{ flex: 1, minWidth: 0 }}>
        {title && <div style={{ fontSize: 'var(--fs-sm)', fontWeight: 600, marginBottom: children ? 5 : 0 }}>{title}</div>}
        <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg)', lineHeight: 1.5 }}>{children}</div>
        {action && <div style={{ marginTop: 10 }}>{action}</div>}
      </div>
    </div>
  );
}

/* provider picker grid */
function ProviderGrid({ value, onChange }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(132px, 1fr))', gap: 10 }}>
      {PROVIDERS.map((p) => {
        const active = value === p.id;
        return (
          <button key={p.id} onClick={() => onChange(p.id)}
            style={{
              display: 'flex', alignItems: 'center', gap: 9, padding: '11px 12px', cursor: 'pointer', textAlign: 'left',
              color: 'var(--fg)',
              background: active ? 'var(--accent-subtle)' : 'var(--bg)',
              border: `1px solid ${active ? 'var(--accent)' : 'var(--border-strong)'}`,
              boxShadow: active ? '0 0 0 3px color-mix(in oklab, var(--accent) 16%, transparent)' : 'none',
              borderRadius: 'var(--radius)', transition: 'all 120ms',
            }}>
            <span style={{ width: 26, height: 26, borderRadius: 'var(--radius-sm)', background: p.color, color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}><I.mail size={14} /></span>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.short}</div>
              <div style={{ fontSize: 10, color: 'var(--fg-muted)' }}>{p.protocol}</div>
            </div>
          </button>
        );
      })}
    </div>
  );
}

function CredForm({ provider, addr, setAddr, role }) {
  const p = providerById(provider);
  return (
    <div style={{ display: 'grid', gap: 16, maxWidth: 460 }}>
      <Field label={`${role} email address`} required>
        <Input value={addr} onChange={setAddr} placeholder="name@company.com" mono icon={<I.mail size={14} />} />
      </Field>
      {p.auth === 'oauth' && (
        <Button variant="outline" size="lg" icon={<span style={{ width: 16, height: 16, borderRadius: 4, background: p.color, display: 'inline-block' }} />}>
          Sign in with {p.short}
        </Button>
      )}
      {p.auth === 'basic' && (
        <Field label="Password" hint="Stored encrypted, used only for this migration." required>
          <Input value="" onChange={() => {}} type="password" placeholder="••••••••••" />
        </Field>
      )}
      {p.auth === 'apppwd' && (
        <Field label="App password" hint={<>{p.short} requires an app-specific password, not your normal one. <span style={{ color: 'var(--accent)', fontWeight: 600, cursor: 'pointer' }}>How to create one →</span></>} required>
          <Input value="" onChange={() => {}} mono placeholder="xxxx-xxxx-xxxx-xxxx" icon={<I.key size={14} />} />
        </Field>
      )}
      {(p.protocol === 'IMAP' || p.protocol === 'EWS') && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 110px', gap: 12 }}>
          <Field label="Server host"><Input value={p.id === 'workmail' ? 'imap.mail.us-east-1.awsapps.com' : ''} onChange={() => {}} mono placeholder="imap.example.com" /></Field>
          <Field label="Port"><Input value="993" onChange={() => {}} mono /></Field>
        </div>
      )}
    </div>
  );
}

/* ---------- step bodies ---------- */
function StepBody({ stepKey, st, set }) {
  if (stepKey === 'source') return (
    <div style={{ display: 'grid', gap: 22 }}>
      <Field label="Which provider holds the mail today?"><ProviderGrid value={st.src} onChange={(v) => set({ src: v })} /></Field>
      <div style={{ height: 1, background: 'var(--border)' }} />
      <CredForm provider={st.src} addr={st.srcAddr} setAddr={(v) => set({ srcAddr: v })} role="Source" />
    </div>
  );
  if (stepKey === 'dest') return (
    <div style={{ display: 'grid', gap: 22 }}>
      <Field label="Where should the mail go?"><ProviderGrid value={st.dest} onChange={(v) => set({ dest: v })} /></Field>
      <div style={{ height: 1, background: 'var(--border)' }} />
      <CredForm provider={st.dest} addr={st.destAddr} setAddr={(v) => set({ destAddr: v })} role="Destination" />
    </div>
  );
  if (stepKey === 'test') return <StepTest st={st} set={set} />;
  if (stepKey === 'review') return <StepReview st={st} set={set} />;
  if (stepKey === 'run') return <StepRun st={st} />;
  return null;
}

function StepTest({ st, set }) {
  const run = () => {
    set({ test: 'testing' });
    setTimeout(() => set({ test: 'ok' }), 1600);
  };
  return (
    <div style={{ display: 'grid', gap: 16, maxWidth: 560 }}>
      <p style={{ margin: 0, fontSize: 'var(--fs-body)', color: 'var(--fg-muted)', lineHeight: 1.5 }}>
        We’ll sign in to both mailboxes and check folders, permissions, and limits before moving anything.
      </p>
      <div style={{ display: 'flex', gap: 10 }}>
        <Button variant="primary" size="lg" icon={st.test === 'testing' ? <Spinner size={15} color="var(--accent-fg)" /> : <I.shield size={16} />} onClick={run} disabled={st.test === 'testing'}>
          {st.test === 'testing' ? 'Testing…' : st.test === 'ok' ? 'Test again' : 'Test connection'}
        </Button>
        <Button variant="ghost" size="lg" onClick={() => set({ test: 'error' })}>Simulate failure</Button>
      </div>

      {st.test === 'ok' && (
        <Alert tone="success" title="Both connections look good.">
          <ul className="mono" style={{ margin: '4px 0 0', paddingLeft: 16, fontSize: 12.5, lineHeight: 1.7, color: 'var(--fg-muted)' }}>
            <li>{providerById(st.src).short}: signed in · 6 folders · 3,201 messages · 4.8 GB</li>
            <li>{providerById(st.dest).short}: signed in · write access confirmed · 30 GB free</li>
          </ul>
        </Alert>
      )}
      {st.test === 'error' && (
        <Alert tone="warning" title="We couldn’t sign in to WorkMail."
          action={<Collapsible trigger="Technical details">
            <pre className="mono" style={{ margin: 0, padding: 12, background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 'var(--radius-sm)', fontSize: 11.5, color: 'var(--fg-muted)', whiteSpace: 'pre-wrap', lineHeight: 1.5 }}>
{`a1 LOGIN harold@conway-law.com ****
a1 NO [AUTHENTICATIONFAILED] Invalid credentials (Failure)
trace: 4f9c-21a8`}
            </pre>
          </Collapsible>}>
          WorkMail needs an <strong>app password</strong>, not your normal password. <span style={{ color: 'var(--accent)', fontWeight: 600, cursor: 'pointer' }}>How to create one →</span>
        </Alert>
      )}
    </div>
  );
}

function StepReview({ st, set }) {
  return (
    <div style={{ display: 'grid', gap: 14, maxWidth: 640 }}>
      <p style={{ margin: 0, fontSize: 'var(--fs-body)', color: 'var(--fg-muted)', lineHeight: 1.5 }}>
        We found a few things to confirm before migrating. Pick how to handle each — defaults are safe.
      </p>
      {ISSUES.map((iss) => {
        const open = st.openIssue === iss.id;
        const sev = iss.severity === 'error' ? 'error' : 'warning';
        return (
          <Card key={iss.id} pad={false}>
            <button onClick={() => set({ openIssue: open ? null : iss.id })}
              style={{ width: '100%', display: 'flex', alignItems: 'center', gap: 12, padding: '13px 16px', background: 'transparent', border: 'none', cursor: 'pointer', textAlign: 'left' }}>
              <StatusChip status={sev} label={iss.group} size="sm" />
              <span style={{ flex: 1, fontSize: 13.5, fontWeight: 500 }}>{iss.title}</span>
              <Badge>{iss.count}</Badge>
              <span style={{ transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 150ms', color: 'var(--fg-muted)', display: 'inline-flex' }}><I.chevronR size={15} /></span>
            </button>
            {open && (
              <div className="kk-fade-up" style={{ padding: '0 16px 16px', borderTop: '1px solid var(--border)' }}>
                <div style={{ margin: '12px 0', display: 'grid', gap: 5 }}>
                  {iss.items.map((it) => (
                    <div key={it} className="mono" style={{ fontSize: 12, color: 'var(--fg-muted)', display: 'flex', alignItems: 'center', gap: 7 }}>
                      <I.chevronR size={11} />{it}
                    </div>
                  ))}
                </div>
                <Field label="How should we handle this?">
                  <Select value={(st.res && st.res[iss.id]) || iss.resolutions[0]}
                    onChange={(v) => set({ res: { ...(st.res || {}), [iss.id]: v } })}
                    options={iss.resolutions} />
                </Field>
              </div>
            )}
          </Card>
        );
      })}
    </div>
  );
}

function StepRun({ st }) {
  const recap = [
    { label: 'Source', value: `${providerById(st.src).name} · ${st.srcAddr || '—'}` },
    { label: 'Destination', value: `${providerById(st.dest).name} · ${st.destAddr || '—'}` },
    { label: 'To migrate', value: '6 folders · 3,201 messages · 4.8 GB' },
    { label: 'Estimated time', value: '≈ 2 hours' },
    { label: 'Source mailbox', value: 'Left untouched (copy only)' },
  ];
  return (
    <div style={{ display: 'grid', gap: 16, maxWidth: 540 }}>
      <Card style={{ background: 'var(--surface)' }}>
        {recap.map((r, i) => (
          <div key={r.label} style={{ display: 'flex', justifyContent: 'space-between', gap: 16, padding: '9px 0', borderTop: i ? '1px solid var(--border)' : 'none' }}>
            <span style={{ fontSize: 13, color: 'var(--fg-muted)' }}>{r.label}</span>
            <span className="mono" style={{ fontSize: 12.5, fontWeight: 500, textAlign: 'right' }}>{r.value}</span>
          </div>
        ))}
      </Card>
      <Alert tone="info" title="You’re ready to go.">Migration runs in the background. You’ll get an email when it’s done, and you can close this page any time.</Alert>
    </div>
  );
}

/* ---------- chrome variants ---------- */
function StepperTop({ idx, steps, setIdx, canGo }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 0, marginBottom: 6 }}>
      {steps.map((s, i) => {
        const done = i < idx, active = i === idx;
        return (
          <React.Fragment key={s.key}>
            <button onClick={() => i <= canGo && setIdx(i)} disabled={i > canGo}
              style={{ display: 'flex', alignItems: 'center', gap: 9, background: 'transparent', border: 'none', cursor: i <= canGo ? 'pointer' : 'default', padding: 0 }}>
              <span style={{
                width: 28, height: 28, borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
                background: done ? 'var(--accent)' : active ? 'var(--accent-subtle)' : 'var(--surface-2)',
                color: done ? 'var(--accent-fg)' : active ? 'var(--accent)' : 'var(--fg-subtle)',
                border: active ? '1px solid var(--accent)' : '1px solid var(--border)', fontSize: 12, fontWeight: 700,
                transition: 'all 150ms',
              }}>{done ? <I.check size={15} /> : i + 1}</span>
              <span style={{ fontSize: 13, fontWeight: active ? 600 : 500, color: active ? 'var(--fg)' : 'var(--fg-muted)' }}>{s.label}</span>
            </button>
            {i < steps.length - 1 && <div style={{ flex: 1, height: 1.5, margin: '0 12px', background: i < idx ? 'var(--accent)' : 'var(--border)', transition: 'background 200ms' }} />}
          </React.Fragment>
        );
      })}
    </div>
  );
}

function SideRail({ idx, steps, setIdx, canGo }) {
  return (
    <div style={{ display: 'grid', gap: 4, width: 220, flexShrink: 0 }}>
      {steps.map((s, i) => {
        const done = i < idx, active = i === idx;
        return (
          <button key={s.key} onClick={() => i <= canGo && setIdx(i)} disabled={i > canGo}
            style={{ display: 'flex', alignItems: 'flex-start', gap: 11, padding: '11px 12px', borderRadius: 'var(--radius)', cursor: i <= canGo ? 'pointer' : 'default', textAlign: 'left',
              background: active ? 'var(--accent-subtle)' : 'transparent', border: `1px solid ${active ? 'var(--accent-line)' : 'transparent'}` }}>
            <span style={{ width: 24, height: 24, borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, marginTop: 1,
              background: done ? 'var(--accent)' : active ? 'var(--bg)' : 'var(--surface-2)', color: done ? 'var(--accent-fg)' : active ? 'var(--accent)' : 'var(--fg-subtle)',
              border: active ? '1px solid var(--accent)' : '1px solid var(--border)', fontSize: 11, fontWeight: 700 }}>{done ? <I.check size={13} /> : i + 1}</span>
            <div>
              <div style={{ fontSize: 13, fontWeight: active ? 600 : 500, color: active || done ? 'var(--fg)' : 'var(--fg-muted)' }}>{s.label}</div>
              <div style={{ fontSize: 11, color: 'var(--fg-muted)' }}>{s.desc}</div>
            </div>
          </button>
        );
      })}
    </div>
  );
}

window.Wizard = function Wizard({ pattern, persona, go }) {
  const toast = useToast();
  const [idx, setIdx] = uWz(0);
  const [maxReached, setMax] = uWz(0);
  const [st, setSt] = uWz({ src: 'workmail', dest: 'gmail', srcAddr: 'harold@conway-law.com', destAddr: 'harold@conwaylaw.com', test: null, openIssue: 'i1', res: {} });
  const set = (patch) => setSt((s) => ({ ...s, ...patch }));
  const steps = STEPS;
  const cur = steps[idx];

  const canNext = () => {
    if (cur.key === 'source') return !!st.srcAddr;
    if (cur.key === 'dest') return !!st.destAddr;
    if (cur.key === 'test') return st.test === 'ok';
    return true;
  };

  const goNext = () => {
    if (idx < steps.length - 1) { const n = idx + 1; setIdx(n); setMax((m) => Math.max(m, n)); }
    else { toast({ status: 'running', title: 'Migration started', body: `${providerById(st.src).short} → ${providerById(st.dest).short} · running in background` }); go('run'); }
  };
  const goBack = () => idx > 0 && setIdx(idx - 1);

  const heading = (
    <div style={{ marginBottom: 4 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <span style={{ width: 34, height: 34, borderRadius: 'var(--radius)', background: 'var(--accent-subtle)', color: 'var(--accent)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>{cur.icon({ size: 18 })}</span>
        <div>
          <h2 style={{ margin: 0, fontSize: 'var(--fs-h1)', fontWeight: 600, letterSpacing: '-0.015em' }}>{stepTitle(cur.key)}</h2>
          <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg-muted)' }}>Step {idx + 1} of {steps.length} · {cur.desc}</div>
        </div>
      </div>
    </div>
  );

  const footer = (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 26, paddingTop: 18, borderTop: '1px solid var(--border)' }}>
      <Button variant="ghost" icon={<I.chevronL size={15} />} onClick={goBack} disabled={idx === 0}>Back</Button>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        {!canNext() && cur.key === 'test' && <span style={{ fontSize: 12, color: 'var(--fg-muted)' }}>Run the test to continue</span>}
        <Button variant="primary" iconRight={idx === steps.length - 1 ? <I.play size={15} /> : <I.arrowR size={15} />} onClick={goNext} disabled={!canNext()}>
          {idx === steps.length - 1 ? 'Start migration' : 'Continue'}
        </Button>
      </div>
    </div>
  );

  /* SINGLE-SCROLL pattern */
  if (pattern === 'single') {
    return (
      <div style={{ maxWidth: 720, margin: '0 auto', display: 'grid', gap: 18 }}>
        <div style={{ position: 'sticky', top: 0, zIndex: 4, background: 'var(--bg)', paddingBottom: 12, marginBottom: -4 }}>
          <Progress value={(maxReached + 1) / steps.length * 100} height={6} />
        </div>
        {steps.map((s) => (
          <Card key={s.key} style={{ scrollMarginTop: 80 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 18 }}>
              <span style={{ width: 30, height: 30, borderRadius: 'var(--radius)', background: 'var(--accent-subtle)', color: 'var(--accent)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>{s.icon({ size: 16 })}</span>
              <h2 style={{ margin: 0, fontSize: 'var(--fs-h2)', fontWeight: 600 }}>{stepTitle(s.key)}</h2>
            </div>
            <StepBody stepKey={s.key} st={st} set={set} />
          </Card>
        ))}
        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button variant="primary" size="lg" iconRight={<I.play size={16} />} onClick={() => { toast({ status: 'running', title: 'Migration started', body: 'Running in the background' }); go('run'); }}>Start migration</Button>
        </div>
      </div>
    );
  }

  /* SIDE-RAIL pattern */
  if (pattern === 'siderail') {
    return (
      <div style={{ display: 'flex', gap: 32, maxWidth: 900, margin: '0 auto' }}>
        <SideRail idx={idx} steps={steps} setIdx={setIdx} canGo={maxReached} />
        <div style={{ flex: 1, minWidth: 0 }}>
          {heading}
          <div style={{ marginTop: 22 }}><StepBody stepKey={cur.key} st={st} set={set} /></div>
          {footer}
        </div>
      </div>
    );
  }

  /* STEPPED (default) */
  return (
    <div style={{ maxWidth: 760, margin: '0 auto' }}>
      <StepperTop idx={idx} steps={steps} setIdx={setIdx} canGo={maxReached} />
      <Card style={{ marginTop: 22 }}>
        {heading}
        <div style={{ marginTop: 22 }}><StepBody stepKey={cur.key} st={st} set={set} /></div>
        {footer}
      </Card>
    </div>
  );
};

function stepTitle(key) {
  return { source: 'Connect your current mailbox', dest: 'Choose where mail goes', test: 'Test the connection', review: 'Review & resolve', run: 'Ready to migrate' }[key];
}
Object.assign(window, { Alert });
