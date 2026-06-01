/* global React */
/* EMaigrator — inline-SVG charts (throughput / progress). No chart lib. */
const { useState: uChS } = React;

function ThroughputChart({ data, height = 130, unit = 'msg/min', accent = 'var(--accent)', showAxis = true, throttleFrom }) {
  const w = 600, h = height, padB = showAxis ? 18 : 4, padT = 6;
  const max = Math.max(...data) * 1.12;
  const stepX = w / (data.length - 1);
  const y = (v) => padT + (h - padT - padB) * (1 - v / max);
  const pts = data.map((v, i) => [i * stepX, y(v)]);
  const line = pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(' ');
  const area = `${line} L${w},${h - padB} L0,${h - padB} Z`;
  const [hover, setHover] = uChS(null);
  const gid = React.useMemo(() => 'g' + Math.random().toString(36).slice(2, 7), []);

  return (
    <div style={{ position: 'relative', width: '100%' }}>
      <svg viewBox={`0 0 ${w} ${h}`} width="100%" height={h} preserveAspectRatio="none" style={{ display: 'block', overflow: 'visible' }}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(e) => {
          const r = e.currentTarget.getBoundingClientRect();
          const x = ((e.clientX - r.left) / r.width) * w;
          setHover(Math.max(0, Math.min(data.length - 1, Math.round(x / stepX))));
        }}>
        <defs>
          <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={accent} stopOpacity="0.28" />
            <stop offset="100%" stopColor={accent} stopOpacity="0.02" />
          </linearGradient>
        </defs>
        {showAxis && [0.25, 0.5, 0.75, 1].map((f) => (
          <line key={f} x1="0" x2={w} y1={padT + (h - padT - padB) * f} y2={padT + (h - padT - padB) * f} stroke="var(--chart-grid)" strokeWidth="1" vectorEffect="non-scaling-stroke" />
        ))}
        {throttleFrom != null && (
          <rect x={throttleFrom * stepX} y={padT} width={w - throttleFrom * stepX} height={h - padT - padB} fill="var(--throttled)" opacity="0.06" />
        )}
        <path d={area} fill={`url(#${gid})`} />
        <path d={line} fill="none" stroke={accent} strokeWidth="2" vectorEffect="non-scaling-stroke" strokeLinejoin="round" strokeLinecap="round" />
        {hover != null && (
          <g>
            <line x1={pts[hover][0]} x2={pts[hover][0]} y1={padT} y2={h - padB} stroke="var(--border-strong)" strokeWidth="1" vectorEffect="non-scaling-stroke" strokeDasharray="3 3" />
            <circle cx={pts[hover][0]} cy={pts[hover][1]} r="3.5" fill={accent} stroke="var(--bg)" strokeWidth="2" vectorEffect="non-scaling-stroke" />
          </g>
        )}
      </svg>
      {hover != null && (
        <div className="mono" style={{
          position: 'absolute', top: 0, left: `${(pts[hover][0] / w) * 100}%`, transform: 'translateX(-50%)',
          background: 'var(--fg)', color: 'var(--bg)', padding: '3px 7px', borderRadius: 'var(--radius-sm)',
          fontSize: 11, fontWeight: 600, pointerEvents: 'none', whiteSpace: 'nowrap',
        }}>{data[hover].toLocaleString()} {unit}</div>
      )}
    </div>
  );
}

function Donut({ segments, size = 120, thickness = 14, center }) {
  const r = (size - thickness) / 2;
  const c = 2 * Math.PI * r;
  const total = segments.reduce((s, x) => s + x.value, 0) || 1;
  let off = 0;
  return (
    <div style={{ position: 'relative', width: size, height: size, flexShrink: 0 }}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ transform: 'rotate(-90deg)' }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface-2)" strokeWidth={thickness} />
        {segments.map((s, i) => {
          const len = (s.value / total) * c;
          const el = <circle key={i} cx={size / 2} cy={size / 2} r={r} fill="none" stroke={s.color} strokeWidth={thickness}
            strokeDasharray={`${len} ${c - len}`} strokeDashoffset={-off} strokeLinecap="butt" />;
          off += len;
          return el;
        })}
      </svg>
      {center && <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', textAlign: 'center' }}>{center}</div>}
    </div>
  );
}

Object.assign(window, { ThroughputChart, Donut });
