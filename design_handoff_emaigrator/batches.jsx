/* global React, Card, CardTitle, Button, StatusChip, Badge, Progress, Input, Segmented, I, MAILBOXES, BATCH, providerById */
const { useState: uB } = React;

const FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'running', label: 'Running' },
  { value: 'throttled', label: 'Throttled' },
  { value: 'warning', label: 'Needs decision' },
  { value: 'error', label: 'Failed' },
  { value: 'done', label: 'Done' },
  { value: 'queued', label: 'Queued' },
];

function progressFor(status) {
  return status === 'done' ? 100 : status === 'queued' ? 0 : status === 'error' ? 0 : status === 'throttled' ? 32 : status === 'warning' ? 48 : 62;
}

window.Batches = function Batches({ go }) {
  const [q, setQ] = uB('');
  const [filter, setFilter] = uB('all');
  const rows = MAILBOXES.filter((m) =>
    (filter === 'all' || m.status === filter) &&
    (q === '' || m.name.toLowerCase().includes(q.toLowerCase()) || m.addr.toLowerCase().includes(q.toLowerCase())));

  return (
    <div style={{ display: 'grid', gap: 'var(--section-gap)' }}>
      {/* batch summary card */}
      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
          <span style={{ width: 40, height: 40, borderRadius: 'var(--radius)', background: 'var(--accent-subtle)', color: 'var(--accent)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><I.batch size={20} /></span>
          <div style={{ flex: 1, minWidth: 200 }}>
            <div style={{ fontSize: 'var(--fs-h2)', fontWeight: 600 }}>{BATCH.name}</div>
            <div className="mono" style={{ fontSize: 12, color: 'var(--fg-muted)' }}>{providerById(BATCH.source).short} → {providerById(BATCH.dest).short} · {BATCH.total} mailboxes · started {BATCH.startedAt}</div>
          </div>
          <div style={{ display: 'flex', gap: 20 }}>
            {[['Done', BATCH.done, 'var(--success)'], ['Running', BATCH.running + BATCH.throttled, 'var(--accent)'], ['Issues', BATCH.failed + 1, 'var(--error)']].map(([l, v, c]) => (
              <div key={l} style={{ textAlign: 'center' }}>
                <div className="tnum" style={{ fontSize: 22, fontWeight: 600, color: c }}>{v}</div>
                <div style={{ fontSize: 10.5, color: 'var(--fg-muted)', textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 600 }}>{l}</div>
              </div>
            ))}
          </div>
          <Button variant="primary" icon={<I.eye size={15} />} onClick={() => go('run')}>Open live view</Button>
        </div>
      </Card>

      {/* toolbar */}
      <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
        <div style={{ width: 260 }}><Input value={q} onChange={setQ} placeholder="Search mailboxes…" icon={<I.search size={14} />} /></div>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', flex: 1 }}>
          {FILTERS.map((f) => {
            const active = filter === f.value;
            const n = f.value === 'all' ? MAILBOXES.length : MAILBOXES.filter((m) => m.status === f.value).length;
            return (
              <button key={f.value} onClick={() => setFilter(f.value)}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '0 11px', height: 30, borderRadius: 'var(--radius-full)', cursor: 'pointer', fontSize: 12.5, fontWeight: 600,
                  background: active ? 'var(--accent-subtle)' : 'var(--surface)', color: active ? 'var(--accent)' : 'var(--fg-muted)',
                  border: `1px solid ${active ? 'var(--accent-line)' : 'var(--border)'}` }}>
                {f.label}<span className="mono" style={{ opacity: 0.7 }}>{n}</span>
              </button>
            );
          })}
        </div>
        <Button variant="outline" icon={<I.download size={14} />}>Export CSV</Button>
      </div>

      {/* table */}
      <Card pad={false}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 'var(--fs-sm)' }}>
          <thead>
            <tr style={{ textAlign: 'left', color: 'var(--fg-muted)', fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              <th style={{ padding: 'var(--row-pad-y) var(--card-pad)', fontWeight: 700 }}>Mailbox</th>
              <th style={{ padding: 'var(--row-pad-y) 8px', fontWeight: 700, width: '24%' }}>Progress</th>
              <th style={{ padding: 'var(--row-pad-y) 8px', fontWeight: 700, textAlign: 'right' }}>Messages</th>
              <th style={{ padding: 'var(--row-pad-y) 8px', fontWeight: 700, textAlign: 'right' }}>Size</th>
              <th style={{ padding: 'var(--row-pad-y) 8px', fontWeight: 700, textAlign: 'right' }}>Rate</th>
              <th style={{ padding: 'var(--row-pad-y) 8px', fontWeight: 700 }}>Status</th>
              <th style={{ padding: 'var(--row-pad-y) var(--card-pad)', fontWeight: 700, width: 40 }}></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((m) => (
              <tr key={m.addr} style={{ borderTop: '1px solid var(--border)' }}
                onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface-2)'}
                onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                <td style={{ padding: 'var(--row-pad-y) var(--card-pad)' }}>
                  <div style={{ fontWeight: 600 }}>{m.name}</div>
                  <div className="mono" style={{ fontSize: 11, color: 'var(--fg-muted)' }}>{m.addr}</div>
                  {m.err && <div className="mono" style={{ fontSize: 10.5, color: 'var(--error)', marginTop: 2, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: 280 }}>{m.err}</div>}
                </td>
                <td style={{ padding: 'var(--row-pad-y) 8px' }}><Progress value={progressFor(m.status)} tone={m.status === 'throttled' ? 'throttled' : m.status === 'error' ? 'accent' : 'accent'} striped={['running','throttled'].includes(m.status)} height={6} /></td>
                <td className="mono" style={{ padding: 'var(--row-pad-y) 8px', textAlign: 'right', color: 'var(--fg-muted)' }}>{m.msgs ? m.msgs.toLocaleString() : '—'}</td>
                <td className="mono" style={{ padding: 'var(--row-pad-y) 8px', textAlign: 'right', color: 'var(--fg-muted)' }}>{m.size}</td>
                <td className="mono" style={{ padding: 'var(--row-pad-y) 8px', textAlign: 'right', color: 'var(--fg-muted)' }}>{m.rate ? m.rate.toLocaleString() : '—'}</td>
                <td style={{ padding: 'var(--row-pad-y) 8px' }}><StatusChip status={m.status} size="sm" dot /></td>
                <td style={{ padding: 'var(--row-pad-y) var(--card-pad)' }}>
                  <button style={{ background: 'transparent', border: 'none', color: 'var(--fg-subtle)', cursor: 'pointer', display: 'inline-flex' }} title="Actions"><I.dots size={16} /></button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {rows.length === 0 && <div style={{ padding: 40, textAlign: 'center', color: 'var(--fg-muted)', fontSize: 13 }}>No mailboxes match this filter.</div>}
      </Card>
    </div>
  );
};
