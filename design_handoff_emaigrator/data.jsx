/* global window */
/* EMaigrator — mocked domain data (realistic shapes; nothing fetches) */

const PROVIDERS = [
  { id: 'gmail',    name: 'Google Workspace', short: 'Gmail',     color: '#ea4335', auth: 'oauth', protocol: 'Gmail API' },
  { id: 'm365',     name: 'Microsoft 365',    short: 'M365',      color: '#0078d4', auth: 'oauth', protocol: 'Graph API' },
  { id: 'exchange', name: 'Exchange Server',  short: 'Exchange',  color: '#0a5ca8', auth: 'basic', protocol: 'EWS' },
  { id: 'workmail', name: 'AWS WorkMail',     short: 'WorkMail',  color: '#ff9900', auth: 'apppwd', protocol: 'IMAP' },
  { id: 'imap',     name: 'IMAP / Generic',   short: 'IMAP',      color: '#64748b', auth: 'basic', protocol: 'IMAP' },
  { id: 'yahoo',    name: 'Yahoo Mail',       short: 'Yahoo',     color: '#6001d2', auth: 'apppwd', protocol: 'IMAP' },
  { id: 'zoho',     name: 'Zoho Mail',        short: 'Zoho',      color: '#e42527', auth: 'oauth', protocol: 'IMAP' },
];
const providerById = (id) => PROVIDERS.find((p) => p.id === id) || PROVIDERS[4];

/* ---- the individual happy-path live run ---- */
const LIVE_RUN = {
  id: 'mig_8fa2c1',
  source: 'workmail', dest: 'gmail',
  sourceAddr: 'harold@conway-law.com', destAddr: 'harold@conwaylaw.com',
  displayName: 'Harold Conway',
  status: 'running',
  msgTotal: 3201, msgDone: 2310, msgFailed: 4,
  sizeTotal: '4.8 GB', sizeDone: '3.4 GB',
  rate: 412, // msg/min
  startedAt: '24m ago',
  etaMin: 18,
  folders: [
    { name: 'Inbox', total: 1842, done: 1842, status: 'done' },
    { name: 'Sent', total: 906, done: 906, status: 'done' },
    { name: 'Archive/2019', total: 211, done: 211, status: 'done' },
    { name: 'Archive/2020', total: 198, done: 142, status: 'running' },
    { name: 'Clients/Active', total: 44, done: 0, status: 'queued' },
    { name: 'Junk', total: 0, done: 0, status: 'queued' },
  ],
};

/* ---- MSP batch ---- */
const BATCH = {
  id: 'batch_nw01',
  name: 'Northwind Traders — M365 cutover',
  source: 'exchange', dest: 'm365',
  total: 48, done: 31, running: 5, queued: 9, failed: 1, throttled: 2,
  msgTotal: 1284900, msgDone: 902140,
  rate: 5840,
  startedAt: 'Jun 1, 09:14',
  etaMin: 96,
};

const MAILBOXES = [
  { addr: 'a.shah@northwind.com',     name: 'Aanya Shah',       status: 'done',      msgs: 18204, size: '6.2 GB', rate: 0,    err: null },
  { addr: 'd.okafor@northwind.com',   name: 'David Okafor',     status: 'done',      msgs: 9120,  size: '2.1 GB', rate: 0,    err: null },
  { addr: 'm.rossi@northwind.com',    name: 'Marco Rossi',      status: 'running',  msgs: 24310, size: '9.8 GB', rate: 920,  err: null },
  { addr: 'l.chen@northwind.com',     name: 'Li Chen',          status: 'running',  msgs: 14002, size: '5.0 GB', rate: 1140, err: null },
  { addr: 'p.nguyen@northwind.com',   name: 'Phuong Nguyen',    status: 'throttled', msgs: 31992, size: '12.4 GB', rate: 210, err: null },
  { addr: 's.kowalski@northwind.com', name: 'Sofia Kowalski',   status: 'throttled', msgs: 8800,  size: '3.3 GB', rate: 180, err: null },
  { addr: 'j.adeyemi@northwind.com',  name: 'Jide Adeyemi',     status: 'warning',  msgs: 6500,  size: '2.0 GB', rate: 0,    err: 'Folder name conflict: "Sent Items" vs "Sent"' },
  { addr: 'r.müller@northwind.com',   name: 'Rosa Müller',      status: 'error',    msgs: 0,     size: '—',      rate: 0,    err: 'IMAP NO [AUTHENTICATIONFAILED] — app password required' },
  { addr: 't.banerjee@northwind.com', name: 'Tara Banerjee',    status: 'running',  msgs: 12740, size: '4.4 GB', rate: 760,  err: null },
  { addr: 'k.larsen@northwind.com',   name: 'Knut Larsen',      status: 'running',  msgs: 5300,  size: '1.7 GB', rate: 640,  err: null },
  { addr: 'g.santos@northwind.com',   name: 'Gabriel Santos',   status: 'queued',   msgs: 9900,  size: '3.6 GB', rate: 0,    err: null },
  { addr: 'h.yamamoto@northwind.com', name: 'Hana Yamamoto',    status: 'queued',   msgs: 4200,  size: '1.2 GB', rate: 0,    err: null },
  { addr: 'e.dubois@northwind.com',   name: 'Élise Dubois',     status: 'queued',   msgs: 15600, size: '5.9 GB', rate: 0,    err: null },
  { addr: 'w.zhang@northwind.com',    name: 'Wei Zhang',        status: 'running',  msgs: 7700,  size: '2.5 GB', rate: 880,  err: null },
];

/* ---- review issues (wizard step 4) ---- */
const ISSUES = [
  { id: 'i1', group: 'Folder mapping', severity: 'warning', count: 3,
    title: 'Source uses "Sent Items"; destination expects "Sent"',
    items: ['Sent Items → Sent', 'Deleted Items → Trash', 'Junk E-mail → Spam'],
    resolutions: ['Auto-map to closest match', 'Keep original names', 'Map manually'] },
  { id: 'i2', group: 'Large mailboxes', severity: 'warning', count: 2,
    title: '2 mailboxes exceed 25 GB and may take >6h',
    items: ['p.nguyen@northwind.com — 12.4 GB', 'm.rossi@northwind.com — 9.8 GB'],
    resolutions: ['Migrate in background (recommended)', 'Split by date range', 'Skip for now'] },
  { id: 'i3', group: 'Authentication', severity: 'error', count: 1,
    title: '1 account rejected basic auth',
    items: ['r.müller@northwind.com — needs app password'],
    resolutions: ['Send setup link to user', 'Provide app password now', 'Skip account'] },
];

/* ---- audit log ---- */
const AUDIT = [
  { t: '09:14:02', mailbox: 'batch_nw01', event: 'Batch started', detail: '48 mailboxes queued', level: 'info' },
  { t: '09:14:08', mailbox: 'a.shah@northwind.com', event: 'Migration started', detail: 'Exchange → M365 · 18,204 msgs', level: 'info' },
  { t: '09:41:55', mailbox: 'a.shah@northwind.com', event: 'Migration completed', detail: '18,204 msgs · 6.2 GB · 0 errors', level: 'success' },
  { t: '09:42:10', mailbox: 'r.müller@northwind.com', event: 'Connection failed', detail: 'IMAP NO [AUTHENTICATIONFAILED]', level: 'error', trace: '4f9c-21a8' },
  { t: '10:03:20', mailbox: 'p.nguyen@northwind.com', event: 'Throttled by destination', detail: 'Graph 429 · backing off to 210 msg/min', level: 'warning' },
  { t: '10:18:44', mailbox: 'j.adeyemi@northwind.com', event: 'Decision required', detail: 'Folder name conflict', level: 'warning' },
  { t: '10:31:09', mailbox: 'd.okafor@northwind.com', event: 'Migration completed', detail: '9,120 msgs · 2.1 GB · 0 errors', level: 'success' },
];

/* ---- 30-pt throughput series (msg/min) for charts ---- */
const THROUGHPUT = [380,420,455,470,512,540,560,548,575,590,610,580,540,470,420,210,205,215,260,340,430,510,560,600,640,612,588,560,540,560];

Object.assign(window, { PROVIDERS, providerById, LIVE_RUN, BATCH, MAILBOXES, ISSUES, AUDIT, THROUGHPUT });
