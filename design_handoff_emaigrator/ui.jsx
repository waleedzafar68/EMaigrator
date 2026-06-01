/* global React, I */
/* EMaigrator — UI primitives. All export to window at the bottom. */
const { useState: uS, useRef: uR, useEffect: uE, createContext: uCtx, useContext: uCx } = React;

/* ---------- status model (status is never color-alone: icon + label) ---------- */
const STATUS = {
  done:      { label: 'Migrated',  color: 'var(--success)',   bg: 'var(--success-bg)',   line: 'var(--success-line)',   Icon: I.check },
  running:   { label: 'Running',   color: 'var(--accent)',    bg: 'var(--accent-subtle)', line: 'var(--accent-line)',    Icon: I.play },
  throttled: { label: 'Throttled', color: 'var(--throttled)', bg: 'var(--throttled-bg)', line: 'var(--throttled-line)', Icon: I.slow },
  warning:   { label: 'Needs decision', color: 'var(--warning)', bg: 'var(--warning-bg)', line: 'var(--warning-line)', Icon: I.alert },
  error:     { label: 'Failed',    color: 'var(--error)',     bg: 'var(--error-bg)',     line: 'var(--error-line)',     Icon: I.x },
  queued:    { label: 'Queued',    color: 'var(--idle)',      bg: 'var(--idle-bg)',      line: 'var(--idle-line)',      Icon: I.circle },
  paused:    { label: 'Paused',    color: 'var(--idle)',      bg: 'var(--idle-bg)',      line: 'var(--idle-line)',      Icon: I.pause },
};

/* ---------- Button ---------- */
function Button({ variant = 'outline', size = 'md', children, onClick, icon, iconRight, disabled, full, type = 'button', title, style }) {
  const [h, setH] = uS(false);
  const base = {
    primary:     { bg: 'var(--accent)', fg: 'var(--accent-fg)', bd: 'transparent', hbg: 'var(--accent-hover)' },
    outline:     { bg: 'var(--surface-raised)', fg: 'var(--fg)', bd: 'var(--border-strong)', hbg: 'var(--surface-2)' },
    ghost:       { bg: 'transparent', fg: 'var(--fg-muted)', bd: 'transparent', hbg: 'var(--surface-2)' },
    danger:      { bg: 'var(--error)', fg: '#fff', bd: 'transparent', hbg: 'color-mix(in oklab, var(--error) 88%, black)' },
    accentGhost: { bg: 'var(--accent-subtle)', fg: 'var(--accent)', bd: 'transparent', hbg: 'color-mix(in oklab, var(--accent) 16%, transparent)' },
  }[variant];
  const pad = size === 'lg' ? '0 20px' : size === 'sm' ? '0 11px' : '0 14px';
  const ht = size === 'lg' ? 'var(--hit)' : size === 'sm' ? '28px' : 'var(--control-h)';
  const fs = size === 'lg' ? 15 : size === 'sm' ? 12.5 : 13.5;
  return (
    <button type={type} title={title} onClick={disabled ? undefined : onClick} disabled={disabled}
      onMouseEnter={() => setH(true)} onMouseLeave={() => setH(false)}
      style={{
        height: ht, minHeight: ht, padding: pad, borderRadius: 'var(--radius)',
        background: h && !disabled ? base.hbg : base.bg, color: base.fg,
        border: `1px solid ${base.bd === 'transparent' ? 'transparent' : base.bd}`,
        fontSize: fs, fontWeight: 600, letterSpacing: '-0.005em', cursor: disabled ? 'not-allowed' : 'pointer',
        display: full ? 'flex' : 'inline-flex', width: full ? '100%' : 'auto',
        alignItems: 'center', justifyContent: 'center', gap: 7,
        opacity: disabled ? 0.5 : 1, transition: 'background 150ms, border-color 150ms, color 150ms',
        boxShadow: variant === 'primary' || variant === 'danger' ? 'var(--shadow-sm)' : 'none',
        whiteSpace: 'nowrap', ...style,
      }}>
      {icon}{children}{iconRight}
    </button>
  );
}

/* ---------- Card ---------- */
function Card({ children, pad = true, style, className = '', onClick, hover }) {
  const [h, setH] = uS(false);
  return (
    <div className={className} onClick={onClick}
      onMouseEnter={hover ? () => setH(true) : undefined} onMouseLeave={hover ? () => setH(false) : undefined}
      style={{
        background: 'var(--surface-raised)', backgroundImage: 'linear-gradient(var(--surface-tint), var(--surface-tint))',
        border: `1px solid ${h ? 'var(--border-strong)' : 'var(--border)'}`,
        borderRadius: 'var(--radius-lg)', padding: pad ? 'var(--card-pad)' : 0,
        boxShadow: 'var(--shadow-sm)', transition: 'border-color 150ms',
        cursor: onClick ? 'pointer' : 'default', ...style,
      }}>{children}</div>
  );
}

function CardTitle({ children, sub, right, icon }) {
  return (
    <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12, marginBottom: sub ? 2 : 14 }}>
      <div style={{ minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 'var(--fs-h2)', fontWeight: 600, letterSpacing: '-0.01em' }}>
          {icon && <span style={{ color: 'var(--fg-muted)', display: 'inline-flex' }}>{icon}</span>}{children}
        </div>
        {sub && <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg-muted)', marginTop: 3, marginBottom: 14 }}>{sub}</div>}
      </div>
      {right && <div style={{ flexShrink: 0 }}>{right}</div>}
    </div>
  );
}

/* ---------- StatusChip (Badge: icon + label, semantic color) ---------- */
function StatusChip({ status, label, dot, size = 'md' }) {
  const s = STATUS[status] || STATUS.queued;
  const sm = size === 'sm';
  const Ic = s.Icon;
  return (
    <span className="tnum" style={{
      display: 'inline-flex', alignItems: 'center', gap: sm ? 4 : 5,
      padding: sm ? '1px 7px 1px 6px' : '3px 9px 3px 7px', borderRadius: 'var(--radius-sm)',
      background: s.bg, color: s.color, border: `1px solid ${s.line}`,
      fontSize: sm ? 11 : 12, fontWeight: 600, lineHeight: 1.4, whiteSpace: 'nowrap',
    }}>
      {dot
        ? <span style={{ width: 6, height: 6, borderRadius: '50%', background: s.color, ...(status === 'running' ? { animation: 'kk-pulse 1.4s infinite' } : {}) }} />
        : <Ic size={sm ? 11 : 13} />}
      {label || s.label}
    </span>
  );
}

/* ---------- Badge (neutral pill) ---------- */
function Badge({ children, tone = 'neutral', mono }) {
  const tones = {
    neutral: { bg: 'var(--surface-2)', fg: 'var(--fg-muted)', bd: 'var(--border)' },
    accent:  { bg: 'var(--accent-subtle)', fg: 'var(--accent)', bd: 'var(--accent-line)' },
  }[tone];
  return (
    <span className={mono ? 'mono' : ''} style={{
      display: 'inline-flex', alignItems: 'center', gap: 4, padding: '1px 7px', borderRadius: 'var(--radius-sm)',
      background: tones.bg, color: tones.fg, border: `1px solid ${tones.bd}`, fontSize: 11, fontWeight: 600,
    }}>{children}</span>
  );
}

/* ---------- Field / Input / Select ---------- */
function Field({ label, hint, children, required }) {
  return (
    <label style={{ display: 'block' }}>
      {label && <div style={{ fontSize: 'var(--fs-sm)', fontWeight: 600, marginBottom: 6, color: 'var(--fg)' }}>
        {label}{required && <span style={{ color: 'var(--accent)' }}> *</span>}
      </div>}
      {children}
      {hint && <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-muted)', marginTop: 5 }}>{hint}</div>}
    </label>
  );
}

function Input({ value, onChange, placeholder, type = 'text', mono, icon, style, ...rest }) {
  const [f, setF] = uS(false);
  return (
    <div style={{ position: 'relative', display: 'flex', alignItems: 'center' }}>
      {icon && <span style={{ position: 'absolute', left: 11, color: 'var(--fg-subtle)', pointerEvents: 'none', display: 'inline-flex' }}>{icon}</span>}
      <input type={type} value={value} placeholder={placeholder}
        onChange={(e) => onChange && onChange(e.target.value)} onFocus={() => setF(true)} onBlur={() => setF(false)}
        className={mono ? 'mono' : ''} {...rest}
        style={{
          width: '100%', height: 'var(--control-h)', padding: icon ? '0 12px 0 32px' : '0 12px',
          background: 'var(--bg)', color: 'var(--fg)',
          border: `1px solid ${f ? 'var(--accent)' : 'var(--border-strong)'}`,
          boxShadow: f ? '0 0 0 3px color-mix(in oklab, var(--accent) 18%, transparent)' : 'none',
          borderRadius: 'var(--radius)', fontSize: 'var(--fs-sm)', outline: 'none', transition: 'border-color 120ms, box-shadow 120ms', ...style,
        }} />
    </div>
  );
}

function Select({ value, onChange, options, style }) {
  const [f, setF] = uS(false);
  return (
    <div style={{ position: 'relative' }}>
      <select value={value} onChange={(e) => onChange && onChange(e.target.value)} onFocus={() => setF(true)} onBlur={() => setF(false)}
        style={{
          width: '100%', height: 'var(--control-h)', padding: '0 32px 0 12px', appearance: 'none',
          background: 'var(--bg)', color: 'var(--fg)',
          border: `1px solid ${f ? 'var(--accent)' : 'var(--border-strong)'}`,
          boxShadow: f ? '0 0 0 3px color-mix(in oklab, var(--accent) 18%, transparent)' : 'none',
          borderRadius: 'var(--radius)', fontSize: 'var(--fs-sm)', outline: 'none', cursor: 'pointer', ...style,
        }}>
        {options.map((o) => typeof o === 'string'
          ? <option key={o} value={o}>{o}</option>
          : <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
      <span style={{ position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--fg-subtle)', pointerEvents: 'none', display: 'inline-flex' }}><I.chevronD size={14} /></span>
    </div>
  );
}

/* ---------- Segmented control ---------- */
function Segmented({ value, onChange, options, size = 'md' }) {
  const sm = size === 'sm';
  return (
    <div style={{ display: 'inline-flex', background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: 2, gap: 2 }}>
      {options.map((o) => {
        const val = o.value ?? o; const lbl = typeof o === 'string' ? o : o.label; const active = value === val;
        return (
          <button key={val} onClick={() => onChange(val)} title={o.title}
            style={{
              height: sm ? 24 : 30, padding: sm ? '0 9px' : '0 12px', border: 'none', borderRadius: 'var(--radius-sm)', cursor: 'pointer',
              background: active ? 'var(--surface-raised)' : 'transparent',
              color: active ? 'var(--fg)' : 'var(--fg-muted)',
              boxShadow: active ? 'var(--shadow-sm)' : 'none',
              fontSize: sm ? 12 : 13, fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: 6,
              transition: 'color 120ms ease, box-shadow 120ms ease',
            }}>{o.icon}{lbl}</button>
        );
      })}
    </div>
  );
}

/* ---------- Progress bar ---------- */
function Progress({ value, max = 100, tone = 'accent', striped, height = 8 }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  const col = tone === 'throttled' ? 'var(--throttled)' : tone === 'success' ? 'var(--success)' : 'var(--accent)';
  return (
    <div style={{ width: '100%', height, background: 'var(--surface-2)', borderRadius: 'var(--radius-full)', overflow: 'hidden', border: '1px solid var(--border)' }}>
      <div style={{
        width: `${pct}%`, height: '100%', background: col, borderRadius: 'var(--radius-full)',
        transition: 'width 600ms cubic-bezier(0.22,1,0.36,1)',
        ...(striped ? {
          backgroundImage: 'linear-gradient(45deg, rgba(255,255,255,0.22) 25%, transparent 25%, transparent 50%, rgba(255,255,255,0.22) 50%, rgba(255,255,255,0.22) 75%, transparent 75%, transparent)',
          backgroundSize: '28px 28px', animation: 'kk-bar-stripes 0.7s linear infinite',
        } : {}),
      }} />
    </div>
  );
}

/* ---------- Spinner ---------- */
function Spinner({ size = 14, color = 'currentColor' }) {
  return <span style={{ display: 'inline-block', width: size, height: size, border: `2px solid color-mix(in oklab, ${color} 25%, transparent)`, borderTopColor: color, borderRadius: '50%', animation: 'kk-spin 0.7s linear infinite' }} />;
}

/* ---------- Skeleton ---------- */
function Skeleton({ w = '100%', h = 14, r = 'var(--radius-sm)', style }) {
  return <div style={{ width: w, height: h, borderRadius: r, background: 'var(--surface-2)', animation: 'kk-skeleton 1.3s ease-in-out infinite', ...style }} />;
}

/* ---------- Tabs ---------- */
function Tabs({ tabs, value, onChange }) {
  return (
    <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--border)' }}>
      {tabs.map((t) => {
        const val = t.value ?? t; const lbl = t.label ?? t; const active = value === val;
        return (
          <button key={val} onClick={() => onChange(val)}
            style={{
              padding: '9px 14px', border: 'none', background: 'transparent', cursor: 'pointer',
              color: active ? 'var(--fg)' : 'var(--fg-muted)', fontSize: 'var(--fs-sm)', fontWeight: 600,
              borderBottom: `2px solid ${active ? 'var(--accent)' : 'transparent'}`, marginBottom: -1,
              display: 'inline-flex', alignItems: 'center', gap: 7, transition: 'color 120ms',
            }}>{lbl}{t.count != null && <Badge>{t.count}</Badge>}</button>
        );
      })}
    </div>
  );
}

/* ---------- Collapsible ---------- */
function Collapsible({ trigger, children, defaultOpen = false, mono }) {
  const [open, setOpen] = uS(defaultOpen);
  return (
    <div>
      <button onClick={() => setOpen(!open)} style={{
        display: 'inline-flex', alignItems: 'center', gap: 5, background: 'transparent', border: 'none', cursor: 'pointer',
        color: 'var(--fg-muted)', fontSize: 'var(--fs-sm)', fontWeight: 600, padding: 0,
      }}>
        <span style={{ transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 150ms', display: 'inline-flex' }}><I.chevronR size={13} /></span>
        {trigger}
      </button>
      {open && <div className="kk-fade-up" style={{ marginTop: 8 }}>{children}</div>}
    </div>
  );
}

/* ---------- Toast system ---------- */
const ToastCtx = uCtx(null);
function ToastProvider({ children }) {
  const [toasts, setToasts] = uS([]);
  const push = (t) => {
    const id = Math.random().toString(36).slice(2);
    setToasts((cur) => [...cur, { id, ...t }]);
    setTimeout(() => setToasts((cur) => cur.filter((x) => x.id !== id)), t.duration || 4200);
  };
  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div style={{ position: 'fixed', bottom: 20, right: 20, zIndex: 9999, display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 360 }}>
        {toasts.map((t) => {
          const s = STATUS[t.status] || STATUS.done; const Ic = s.Icon;
          return (
            <div key={t.id} className="kk-fade-up" style={{
              display: 'flex', gap: 10, alignItems: 'flex-start', padding: '12px 14px',
              background: 'var(--surface-raised)', border: '1px solid var(--border-strong)', borderLeft: `3px solid ${s.color}`,
              borderRadius: 'var(--radius)', boxShadow: 'var(--shadow-md)',
            }}>
              <span style={{ color: s.color, marginTop: 1, display: 'inline-flex' }}><Ic size={16} /></span>
              <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: 'var(--fs-sm)', fontWeight: 600 }}>{t.title}</div>
                {t.body && <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-muted)', marginTop: 2 }}>{t.body}</div>}
              </div>
            </div>
          );
        })}
      </div>
    </ToastCtx.Provider>
  );
}
const useToast = () => uCx(ToastCtx);

Object.assign(window, {
  STATUS, Button, Card, CardTitle, StatusChip, Badge, Field, Input, Select,
  Segmented, Progress, Spinner, Skeleton, Tabs, Collapsible, ToastProvider, useToast,
});
