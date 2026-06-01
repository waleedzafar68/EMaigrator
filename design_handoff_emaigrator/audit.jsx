/* global React, Card, CardTitle, Button, StatusChip, Badge, Tabs, Collapsible, I, AUDIT, BATCH */
const { useState: uA } = React;

const LEVEL = {
  info:    { color: 'var(--fg-muted)', Icon: I.info },
  success: { color: 'var(--success)', Icon: I.checkCircle },
  warning: { color: 'var(--throttled)', Icon: I.alert },
  error:   { color: 'var(--error)', Icon: I.xCircle },
};

window.Audit = function Audit() {
  const [tab, setTab] = uA('all');
  const [exportOpen, setExportOpen] = uA(false);
  const rows = AUDIT.filter((e) => tab === 'all'
    || (tab === 'completed' && e.level === 'success')
    || (tab === 'errors' && e.level === 'error')
    || (tab === 'decisions' && e.level === 'warning'));

  const counts = {
    all: AUDIT.length,
    completed: AUDIT.filter((e) => e.level === 'success').length,
    errors: AUDIT.filter((e) => e.level === 'error').length,
    decisions: AUDIT.filter((e) => e.level === 'warning').length,
  };

  return (
    <div style={{ display: 'grid', gap: 'var(--section-gap)' }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 'var(--grid-gap)' }}>
        {[
          { l: 'Mailboxes completed', v: '31 / 48', c: 'var(--success)', i: <I.checkCircle size={16} /> },
          { l: 'Messages migrated', v: '902,140', c: 'var(--accent)', i: <I.mail size={16} /> },
          { l: 'Errors logged', v: '1', c: 'var(--error)', i: <I.xCircle size={16} /> },
        ].map((k) => (
          <Card key={k.l}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
              <span style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '0.07em', textTransform: 'uppercase', color: 'var(--fg-muted)' }}>{k.l}</span>
              <span style={{ color: k.c, display: 'inline-flex' }}>{k.i}</span>
            </div>
            <div className="tnum" style={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.02em' }}>{k.v}</div>
          </Card>
        ))}
      </div>

      <Card pad={false}>
        <div style={{ padding: '14px var(--card-pad)', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
          <Tabs value={tab} onChange={setTab} tabs={[
            { value: 'all', label: 'All events', count: counts.all },
            { value: 'completed', label: 'Completed', count: counts.completed },
            { value: 'decisions', label: 'Decisions', count: counts.decisions },
            { value: 'errors', label: 'Errors', count: counts.errors },
          ]} />
          <div style={{ position: 'relative' }}>
            <Button variant="outline" size="sm" icon={<I.download size={14} />} iconRight={<I.chevronD size={13} />} onClick={() => setExportOpen(!exportOpen)}>Export</Button>
            {exportOpen && (
              <>
                <div onClick={() => setExportOpen(false)} style={{ position: 'fixed', inset: 0, zIndex: 10 }} />
                <div className="kk-fade-up" style={{ position: 'absolute', right: 0, top: 'calc(100% + 6px)', zIndex: 11, background: 'var(--surface-raised)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius)', boxShadow: 'var(--shadow-md)', padding: 5, width: 180 }}>
                  {['Download CSV', 'Download JSON', 'Full PDF report', 'Email to client'].map((o) => (
                    <button key={o} onClick={() => setExportOpen(false)} style={{ display: 'flex', width: '100%', alignItems: 'center', gap: 9, padding: '8px 10px', background: 'transparent', border: 'none', borderRadius: 'var(--radius-sm)', cursor: 'pointer', fontSize: 13, color: 'var(--fg)', textAlign: 'left' }}
                      onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface-2)'} onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                      <I.download size={14} />{o}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>

        <div style={{ borderTop: '1px solid var(--border)' }}>
          {rows.map((e, i) => {
            const lv = LEVEL[e.level]; const Ic = lv.Icon;
            return (
              <div key={i} style={{ display: 'flex', gap: 12, padding: 'var(--row-pad-y) var(--card-pad)', borderBottom: '1px solid var(--border)', alignItems: 'flex-start' }}>
                <span className="mono" style={{ fontSize: 12, color: 'var(--fg-subtle)', width: 64, flexShrink: 0, paddingTop: 1 }}>{e.t}</span>
                <span style={{ color: lv.color, marginTop: 1, display: 'inline-flex', flexShrink: 0 }}><Ic size={15} /></span>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                    <span style={{ fontSize: 13.5, fontWeight: 600 }}>{e.event}</span>
                    <span className="mono" style={{ fontSize: 11.5, color: 'var(--fg-muted)' }}>{e.mailbox}</span>
                  </div>
                  <div style={{ fontSize: 12.5, color: 'var(--fg-muted)', marginTop: 2 }}>{e.detail}</div>
                  {e.trace && (
                    <div style={{ marginTop: 7 }}>
                      <Collapsible trigger="Technical details">
                        <pre className="mono" style={{ margin: 0, padding: 11, background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 'var(--radius-sm)', fontSize: 11.5, color: 'var(--fg-muted)', whiteSpace: 'pre-wrap', lineHeight: 1.5 }}>
{`IMAP NO [AUTHENTICATIONFAILED] Invalid credentials (Failure)
host: imap.mail.us-east-1.awsapps.com:993
trace: ${e.trace}`}
                        </pre>
                      </Collapsible>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
};
