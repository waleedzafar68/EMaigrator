/* global React, Card, CardTitle, Button, StatusChip, Badge, Progress, ThroughputChart, Donut, I, LIVE_RUN, BATCH, MAILBOXES, THROUGHPUT, providerById */

function Kpi({ label, value, unit, delta, deltaTone, icon, sub }) {
  return (
    <Card style={{ padding: 'var(--card-pad)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
        <span style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '0.07em', textTransform: 'uppercase', color: 'var(--fg-muted)' }}>{label}</span>
        <span style={{ color: 'var(--fg-subtle)', display: 'inline-flex' }}>{icon}</span>
      </div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
        <span className="tnum" style={{ fontSize: 28, fontWeight: 600, letterSpacing: '-0.02em', lineHeight: 1 }}>{value}</span>
        {unit && <span className="mono" style={{ fontSize: 13, color: 'var(--fg-muted)' }}>{unit}</span>}
      </div>
      <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
        {delta && <span style={{ display: 'inline-flex', alignItems: 'center', gap: 3, fontSize: 12, fontWeight: 600, color: deltaTone === 'down' ? 'var(--error)' : 'var(--success)' }}>
          {deltaTone === 'down' ? <I.trendingDown size={13} /> : <I.trending size={13} />}{delta}
        </span>}
        {sub && <span style={{ fontSize: 12, color: 'var(--fg-muted)' }}>{sub}</span>}
      </div>
    </Card>
  );
}

/* envelope source→dest header used in the individual hero */
function Route({ src, dest, big }) {
  const s = providerById(src), d = providerById(dest);
  const node = (p) => (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <span style={{ width: big ? 34 : 26, height: big ? 34 : 26, borderRadius: 'var(--radius)', background: p.color, color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><I.mail size={big ? 18 : 14} /></span>
      <span style={{ fontWeight: 600, fontSize: big ? 15 : 13 }}>{p.short}</span>
    </div>
  );
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: big ? 14 : 10 }}>
      {node(s)}
      <span style={{ color: 'var(--accent)', display: 'inline-flex' }}><I.arrowR size={big ? 20 : 16} /></span>
      {node(d)}
    </div>
  );
}

/* ---------- INDIVIDUAL dashboard (low density, reassuring) ---------- */
function IndividualDashboard({ go }) {
  const r = LIVE_RUN;
  const pct = Math.round((r.msgDone / r.msgTotal) * 100);
  return (
    <div style={{ maxWidth: 720, margin: '0 auto', display: 'grid', gap: 20 }}>
      <Card style={{ padding: 28 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20, flexWrap: 'wrap', gap: 12 }}>
          <div>
            <div style={{ fontSize: 13, color: 'var(--fg-muted)', marginBottom: 6 }}>Your migration</div>
            <Route src={r.source} dest={r.dest} big />
          </div>
          <StatusChip status="running" label="Moving your mail" />
        </div>

        <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 6 }}>
          <span className="tnum" style={{ fontSize: 38, fontWeight: 600, letterSpacing: '-0.02em', lineHeight: 1 }}>{pct}%</span>
          <span style={{ fontSize: 16, color: 'var(--fg-muted)' }}>done</span>
        </div>
        <Progress value={pct} striped height={12} />
        <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 10, fontSize: 14 }}>
          <span className="mono" style={{ color: 'var(--fg-muted)' }}>{r.msgDone.toLocaleString()} / {r.msgTotal.toLocaleString()} messages</span>
          <span className="mono" style={{ color: 'var(--fg-muted)' }}>about {r.etaMin} min left</span>
        </div>

        <div style={{ marginTop: 22, padding: 16, background: 'var(--accent-subtle)', border: '1px solid var(--accent-line)', borderRadius: 'var(--radius)', display: 'flex', gap: 11, alignItems: 'flex-start' }}>
          <span style={{ color: 'var(--accent)', marginTop: 1, display: 'inline-flex' }}><I.shield size={18} /></span>
          <div style={{ fontSize: 14, lineHeight: 1.5 }}>
            <strong>You can keep using your old inbox.</strong> Nothing is deleted from {providerById(r.source).short} — we’re making a copy in {providerById(r.dest).short}. You don’t need to keep this page open.
          </div>
        </div>

        <div style={{ display: 'flex', gap: 10, marginTop: 22 }}>
          <Button variant="primary" size="lg" icon={<I.eye size={16} />} onClick={() => go('run')}>See live progress</Button>
          <Button variant="outline" size="lg" icon={<I.pause size={15} />}>Pause</Button>
        </div>
      </Card>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <Card>
          <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 12, color: 'var(--fg-muted)' }}>What we’ve moved so far</div>
          {r.folders.slice(0, 4).map((f) => (
            <div key={f.name} style={{ display: 'flex', alignItems: 'center', gap: 9, padding: '7px 0', borderBottom: '1px solid var(--border)' }}>
              <span style={{ color: f.status === 'done' ? 'var(--success)' : f.status === 'running' ? 'var(--accent)' : 'var(--fg-subtle)', display: 'inline-flex' }}>
                {f.status === 'done' ? <I.checkCircle size={16} /> : f.status === 'running' ? <I.refresh size={15} /> : <I.circle size={15} />}
              </span>
              <span style={{ flex: 1, fontSize: 14 }}>{f.name}</span>
              <span className="mono" style={{ fontSize: 12.5, color: 'var(--fg-muted)' }}>{f.done.toLocaleString()}</span>
            </div>
          ))}
        </Card>
        <Card style={{ display: 'flex', flexDirection: 'column' }}>
          <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 12, color: 'var(--fg-muted)' }}>Need a hand?</div>
          <div style={{ fontSize: 14, lineHeight: 1.55, color: 'var(--fg)', flex: 1 }}>
            Migrations usually finish within a couple of hours. We’ll email <span className="mono" style={{ fontSize: 12.5 }}>{r.destAddr}</span> the moment everything is done.
          </div>
          <Button variant="outline" style={{ marginTop: 14, alignSelf: 'flex-start' }} icon={<I.info size={15} />}>How migration works</Button>
        </Card>
      </div>
    </div>
  );
}

/* ---------- MSP dashboard (dense monitoring) ---------- */
function MspDashboard({ go, layout }) {
  const b = BATCH;
  const pct = Math.round((b.msgDone / b.msgTotal) * 100);
  const segs = [
    { label: 'Migrated', value: b.done, color: 'var(--success)' },
    { label: 'Running', value: b.running, color: 'var(--accent)' },
    { label: 'Throttled', value: b.throttled, color: 'var(--throttled)' },
    { label: 'Queued', value: b.queued, color: 'var(--idle)' },
    { label: 'Failed', value: b.failed, color: 'var(--error)' },
  ];
  const attention = MAILBOXES.filter((m) => m.status === 'error' || m.status === 'warning');
  const isList = layout === 'list';

  return (
    <div style={{ display: 'grid', gap: 'var(--section-gap)' }}>
      {isList ? (
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 0 }}>
            {[['Active', b.running + b.throttled, `of ${b.total}`], ['Done', b.done, 'mailboxes'], ['Throughput', b.rate.toLocaleString(), 'msg/min'], ['Needs attention', b.failed + 1, '1 failed · 1 decision']].map(([l, v, s], i) => (
              <div key={l} style={{ flex: 1, minWidth: 130, padding: '2px 20px', borderLeft: i ? '1px solid var(--border)' : 'none' }}>
                <div style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '0.07em', textTransform: 'uppercase', color: 'var(--fg-muted)', marginBottom: 5 }}>{l}</div>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}><span className="tnum" style={{ fontSize: 24, fontWeight: 600 }}>{v}</span><span style={{ fontSize: 11.5, color: 'var(--fg-muted)' }}>{s}</span></div>
              </div>
            ))}
          </div>
        </Card>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 'var(--grid-gap)' }}>
          <Kpi label="Active migrations" value={b.running + b.throttled} icon={<I.runs size={16} />} sub={`of ${b.total} mailboxes`} />
          <Kpi label="Mailboxes done" value={b.done} icon={<I.checkCircle size={16} />} delta="+12 today" deltaTone="up" />
          <Kpi label="Throughput" value={b.rate.toLocaleString()} unit="msg/min" icon={<I.zap size={16} />} delta="−18%" deltaTone="down" sub="throttling" />
          <Kpi label="Needs attention" value={b.failed + 1} icon={<I.alert size={16} />} sub="1 failed · 1 decision" />
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: isList ? '1fr' : '1.55fr 1fr', gap: 'var(--grid-gap)' }}>
        <Card>
          <CardTitle sub={`${b.startedAt} · ETA ~${Math.floor(b.etaMin / 60)}h ${b.etaMin % 60}m`} right={<Button size="sm" variant="outline" icon={<I.eye size={14} />} onClick={() => go('run')}>Open</Button>}>Throughput</CardTitle>
          <ThroughputChart data={THROUGHPUT} throttleFrom={15} />
          <div style={{ display: 'flex', gap: 18, marginTop: 12, fontSize: 12, color: 'var(--fg-muted)' }}>
            <span className="mono">{b.msgDone.toLocaleString()} / {b.msgTotal.toLocaleString()} msgs</span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}><span style={{ width: 9, height: 9, background: 'var(--throttled)', opacity: 0.5, borderRadius: 2 }} />throttle window</span>
          </div>
        </Card>
        {!isList && (
        <Card>
          <CardTitle>Batch breakdown</CardTitle>
          <div style={{ display: 'flex', alignItems: 'center', gap: 18 }}>
            <Donut segments={segs} center={<><span className="tnum" style={{ fontSize: 24, fontWeight: 600 }}>{pct}%</span><span style={{ fontSize: 10, color: 'var(--fg-muted)' }}>complete</span></>} />
            <div style={{ flex: 1, display: 'grid', gap: 6 }}>
              {segs.map((s) => (
                <div key={s.label} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5 }}>
                  <span style={{ width: 9, height: 9, borderRadius: 2, background: s.color }} />
                  <span style={{ flex: 1, color: 'var(--fg-muted)' }}>{s.label}</span>
                  <span className="mono" style={{ fontWeight: 600 }}>{s.value}</span>
                </div>
              ))}
            </div>
          </div>
        </Card>
        )}
      </div>

      <Card pad={false}>
        <div style={{ padding: 'var(--card-pad)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <CardTitle right={null}>Mailboxes in flight</CardTitle>
          <Button size="sm" variant="ghost" iconRight={<I.chevronR size={14} />} onClick={() => go('batches')}>All batches</Button>
        </div>
        <MailboxList rows={MAILBOXES.filter((m) => m.status === 'running' || m.status === 'throttled').slice(0, 5)} />
      </Card>

      {attention.length > 0 && (
        <Card>
          <CardTitle sub="Resolve these to keep the batch moving" icon={<I.alert size={16} />}>Needs your decision</CardTitle>
          {attention.map((m) => (
            <div key={m.addr} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '11px 0', borderBottom: '1px solid var(--border)' }}>
              <StatusChip status={m.status} size="sm" />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13.5, fontWeight: 600 }}>{m.name}</div>
                <div className="mono" style={{ fontSize: 11.5, color: 'var(--fg-muted)' }}>{m.err}</div>
              </div>
              <Button size="sm" variant="outline" onClick={() => go('wizard')}>Resolve</Button>
            </div>
          ))}
        </Card>
      )}
    </div>
  );
}

/* compact mailbox rows reused on dashboard + batch view */
function MailboxList({ rows }) {
  return (
    <div>
      {rows.map((m) => {
        const pctText = m.rate > 0 ? `${m.rate.toLocaleString()} msg/min` : '—';
        return (
          <div key={m.addr} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: 'var(--row-pad-y) var(--card-pad)', borderTop: '1px solid var(--border)' }}>
            <div style={{ width: 150, minWidth: 0 }}>
              <div style={{ fontSize: 13, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m.name}</div>
              <div className="mono" style={{ fontSize: 11, color: 'var(--fg-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m.addr}</div>
            </div>
            <div style={{ flex: 1 }}><Progress value={m.status === 'done' ? 100 : m.status === 'queued' ? 0 : 64} tone={m.status === 'throttled' ? 'throttled' : 'accent'} striped={m.status === 'running' || m.status === 'throttled'} height={6} /></div>
            <span className="mono" style={{ width: 96, textAlign: 'right', fontSize: 12, color: 'var(--fg-muted)' }}>{pctText}</span>
            <StatusChip status={m.status} size="sm" dot />
          </div>
        );
      })}
    </div>
  );
}

window.Dashboard = function Dashboard({ persona, layout, go }) {
  return persona === 'msp' ? <MspDashboard go={go} layout={layout} /> : <IndividualDashboard go={go} />;
};
Object.assign(window, { MailboxList, Kpi, Route });
