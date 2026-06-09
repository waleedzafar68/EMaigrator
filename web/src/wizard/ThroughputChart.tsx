import { Area, AreaChart, ResponsiveContainer, YAxis } from "recharts";

/** Throughput sparkline for the Run view. Lazy-loaded so recharts is split into its own chunk
 *  and only fetched once a migration is actually streaming progress. */
export default function ThroughputChart({ samples }: { samples: { t: number; rate: number }[] }) {
  return (
    <div className="h-16 w-full" aria-hidden>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={samples} margin={{ top: 4, right: 0, bottom: 0, left: 0 }}>
          <defs>
            <linearGradient id="thru" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--accent)" stopOpacity={0.35} />
              <stop offset="100%" stopColor="var(--accent)" stopOpacity={0} />
            </linearGradient>
          </defs>
          <YAxis hide domain={[0, "dataMax + 50"]} />
          <Area type="monotone" dataKey="rate" stroke="var(--accent)" strokeWidth={2} fill="url(#thru)" isAnimationActive={false} dot={false} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
