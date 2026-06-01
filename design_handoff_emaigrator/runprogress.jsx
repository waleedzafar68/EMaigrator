/* global React, Card, CardTitle, Button, StatusChip, Badge, Progress, Spinner, ThroughputChart, I, LIVE_RUN, BATCH, MAILBOXES, THROUGHPUT, providerById, Route, useToast */
const { useState: uRn, useEffect: uRnE, useRef: uRnR } = React;

/* connection pill — live / reconnecting */
function ConnPill({ conn }) {
  if (conn === 'reconnecting') return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '3px 9px', borderRadius: 'var(--radius-full)', background: 'var(--throttled-bg)', color: 'var(--throttled)', border: '1px solid var(--throttled-line)', fontSize: 12, fontWeight: 600 }}>
      <I.wifiOff size={13} /> Reconnecting…
    </span>
  );
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '3px 9px', borderRadius: 'var(--radius-full)', background: 'var(--success-bg)', color: 'var(--success)', border: '1px solid var(--success-line)', fontSize: 12, fontWeight: 600 }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--success)', animation: 'kk-pulse 1.4s infinite' }} /> Live
    </span>
  );
}

function useStream({ start, total, baseRate, running, conn }) {
  const [done, setDone] = uRn(start);
  const [series, setSeries] = uRn(THROUGHPUT.slice());
  const [throttled, setThrottled] = uRn(false);
  const tick = uRnR(0);
  uRnE(() => {
    if (!running || conn === 'reconnecting') return;
    const id = setInterval(() => {
      tick.current += 1;
      const thr = Math.floor(tick.current / 6) % 3 === 2; // periodic throttle window
      setThrottled(thr);
      const rate = thr ? baseRate * 0.4 : baseRate * (0.9 + Math.random() * 0.2);
      setDone((d) => Math.min(total, d + Math.round(rate / 50)));
      setSeries((s) => [...s.slice(1), Math.round(rate)]);
    }, 900);
    return () => clearInterval(id);
  }, [running, conn, baseRate, total]);
  return { done, series, throttled };
}

/* ---------- INDIVIDUAL live run ---------- */
function IndividualRun({ running, setRunning, conn }) {
  const r = LIVE_RUN;
  const { done, throttled } = useStream({ start: r.msgDone, total: r.msgTotal, baseRate: r.rate, running, conn });
  const pct = Math.round((done / r.msgTotal) * 100);
  return (
    <div style={{ maxWidth: 720, margin: '0 auto', display: 'grid', gap: 18 }}>
      <Card style={{ padding: 26 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 18, gap: 12, flexWrap: 'wrap' }}>
          <Route src={r.source} dest={r.dest} big />
          <ConnPill conn={conn} />
        </div>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 8 }}>
          <span className="tnum" style={{ fontSize: 40, fontWeight: 600, letterSpacing: '-0.02em', lineHeight: 1 }}>{pct}%</span>
          <span className="mono" style={{ fontSize: 15, color: 'var(--fg-muted)' }}>{done.toLocaleString()} / {r.msgTotal.toLocaleString()} messages</span>
          {throttled && <span style={{ marginLeft: 'auto' }}><StatusChip status="throttled" label="Slowing to respect limits" size="sm" /></span>}
        </div>
        <Progress value={pct} striped={running && conn === 'live'} tone={throttled ? 'throttled' : 'accent'} height={12} />
        <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 10, fontSize: 13, color: 'var(--fg-muted)' }}>
          <span className="mono">{throttled ? '≈ 165' : '≈ 412'} msg/min</span>
          <span className="mono">about {r.etaMin} min left</span>
        </div>
        <div style={{ display: 'flex', gap: 10, marginTop: 22 }}>
          <Button variant="outline" size="lg" icon={running ? <I.pause size={15} /> : <I.play size={15} />} onClick={() => setRunning(!running)}>{running ? 'Pause' : 'Resume'}</Button>
          <Button variant="ghost" size="lg">Email me when done</Button>
        </div>
      </Card>

      <Card>
        <CardTitle right={<span className="mono" style={{ fontSize: 12, color: 'var(--fg-muted)' }}>{r.folders.filter(f=>f.status==='done').length}/{r.folders.length} folders</span>}>Folders</CardTitle>
        {r.folders.map((f) => {
          const fpct = f.total ? Math.round((f.done / f.total) * 100) : 0;
          return (
            <div key={f.name} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 0', borderBottom: '1px solid var(--border)' }}>
              <span style={{ color: f.status === 'done' ? 'var(--success)' : f.status === 'running' ? 'var(--accent)' : 'var(--fg-subtle)', display: 'inline-flex' }}>
                {f.status === 'done' ? <I.checkCircle size={16} /> : f.status === 'running' ? <Spinner size={14} color="var(--accent)" /> : <I.circle size={15} />}
              </span>
              <span style={{ width: 150, fontSize: 13.5, fontWeight: 500 }}>{f.name}</span>
              <div style={{ flex: 1 }}><Progress value={fpct} tone="accent" height={5} striped={f.status === 'running'} /></div>
              <span className="mono" style={{ width: 110, textAlign: 'right', fontSize: 12, color: 'var(--fg-muted)' }}>{f.done.toLocaleString()} / {f.total.toLocaleString()}</span>
            </div>
          );
        })}
      </Card>
    </div>
  );
}

/* ---------- MSP live run (dense) ---------- */
function MspRun({ running, setRunning, conn }) {
  const b = BATCH;
  const { done, series, throttled } = useStream({ start: b.msgDone, total: b.msgTotal, baseRate: b.rate, running, conn });
  const pct = Math.round((done / b.msgTotal) * 100);
  const live = MAILBOXES.filter((m) => ['running', 'throttled'].includes(m.status));

  return (
    <div style={{ display: 'grid', gap: 'var(--section-gap)' }}>
      {throttled && (
        <div className="kk-fade-up" style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '10px 14px', background: 'var(--throttled-bg)', border: '1px solid var(--throttled-line)', borderRadius: 'var(--radius)' }}>
          <span style={{ color: 'var(--throttled)', display: 'inline-flex' }}><I.slow size={16} /></span>
          <span style={{ fontSize: 13, fontWeight: 500 }}>Destination is rate-limiting (HTTP 429). <span style={{ color: 'var(--fg-muted)', fontWeight: 400 }}>Backing off automatically — no action needed.</span></span>
          <span style={{ marginLeft: 'auto' }}><StatusChip status="throttled" label="2 mailboxes throttled" size="sm" /></span>
        </div>
      )}

      <Card>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 16, flexWrap: 'wrap', marginBottom: 16 }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 4 }}>
              <h2 style={{ margin: 0, fontSize: 'var(--fs-h2)', fontWeight: 600 }}>{b.name}</h2>
              <ConnPill conn={conn} />
            </div>
            <div className="mono" style={{ fontSize: 12, color: 'var(--fg-muted)' }}>{providerById(b.source).short} → {providerById(b.dest).short} · started {b.startedAt}</div>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <Button size="sm" variant="outline" icon={running ? <I.pause size={14} /> : <I.play size={14} />} onClick={() => setRunning(!running)}>{running ? 'Pause batch' : 'Resume'}</Button>
            <Button size="sm" variant="ghost" icon={<I.stop size={14} />}>Stop</Button>
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, auto) 1fr', gap: 24, alignItems: 'baseline', marginBottom: 16 }}>
          {[['Complete', `${pct}%`], ['Messages', `${done.toLocaleString()}`], ['of total', b.msgTotal.toLocaleString()], ['Rate', `${throttled ? '2,340' : series[series.length-1].toLocaleString()}`]].map(([l, v], i) => (
            <div key={l}>
              <div style={{ fontSize: 10, fontWeight: 700, letterSpacing: '0.07em', textTransform: 'uppercase', color: 'var(--fg-muted)', marginBottom: 4 }}>{l}{i===3 && ' msg/min'}</div>
              <div className="tnum" style={{ fontSize: 22, fontWeight: 600 }}>{v}</div>
            </div>
          ))}
        </div>
        <Progress value={pct} striped={running && conn === 'live'} tone={throttled ? 'throttled' : 'accent'} height={10} />
      </Card>

      <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1fr', gap: 'var(--grid-gap)' }}>
        <Card>
          <CardTitle sub="messages / minute, last 30 min">Live throughput</CardTitle>
          <ThroughputChart data={series} throttleFrom={throttled ? 22 : null} />
        </Card>
        <Card>
          <CardTitle>Status</CardTitle>
          <div style={{ display: 'grid', gap: 9 }}>
            {[['done', b.done], ['running', b.running], ['throttled', b.throttled], ['queued', b.queued], ['error', b.failed]].map(([s, n]) => (
              <div key={s} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <StatusChip status={s} size="sm" />
                <div style={{ flex: 1, height: 6, background: 'var(--surface-2)', borderRadius: 'var(--radius-full)', overflow: 'hidden' }}>
                  <div style={{ width: `${(n / b.total) * 100}%`, height: '100%', background: (window.STATUS[s] || {}).color, borderRadius: 'var(--radius-full)' }} />
                </div>
                <span className="mono" style={{ fontSize: 12, fontWeight: 600, width: 22, textAlign: 'right' }}>{n}</span>
              </div>
            ))}
          </div>
        </Card>
      </div>

      <Card pad={false}>
        <div style={{ padding: 'var(--card-pad)' }}><CardTitle>Mailboxes in flight ({live.length})</CardTitle></div>
        <div>
          {live.map((m) => {
            const mp = m.status === 'throttled' ? 28 : 58 + Math.round(Math.random() * 20);
            return (
              <div key={m.addr} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: 'var(--row-pad-y) var(--card-pad)', borderTop: '1px solid var(--border)' }}>
                <div style={{ width: 170, minWidth: 0 }}>
                  <div style={{ fontSize: 13, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m.name}</div>
                  <div className="mono" style={{ fontSize: 11, color: 'var(--fg-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m.addr}</div>
                </div>
                <div style={{ flex: 1 }}><Progress value={mp} tone={m.status === 'throttled' ? 'throttled' : 'accent'} striped={running && conn === 'live'} height={6} /></div>
                <span className="mono" style={{ width: 84, textAlign: 'right', fontSize: 12, color: 'var(--fg-muted)' }}>{m.rate ? `${m.rate} m/min` : '—'}</span>
                <span className="mono" style={{ width: 64, textAlign: 'right', fontSize: 12, color: 'var(--fg-muted)' }}>{m.size}</span>
                <StatusChip status={m.status} size="sm" dot />
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
}

window.RunView = function RunView({ persona, conn, setConn }) {
  const [running, setRunning] = uRn(true);
  return persona === 'msp'
    ? <MspRun running={running} setRunning={setRunning} conn={conn} />
    : <IndividualRun running={running} setRunning={setRunning} conn={conn} />;
};
Object.assign(window, { ConnPill });
