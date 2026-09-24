const REFRESH_MS = 15000;
const S = {};

tm.log('uptime actif — durée de session et coupures par miroir');

function fmt(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), s = t % 60;
  return h > 0 ? h + 'h' + String(m).padStart(2, '0')
              : (m > 0 ? m + 'm' : '') + s + 's';
}

function render(m) {
  const st = S[m.serial];
  if (!st || !st.since) return;
  tm.overlay(m.slot, {
    id: 'uptime', visible: true, title: 'session',
    compact: true, pos: 'bl', color: '#5AA9FF',
    lines: [
      '⏱ ' + fmt(Date.now() - st.since),
      '✂ ' + st.disc + ' coupure' + (st.disc > 1 ? 's' : ''),
    ],
  });
}

tm.on('mirror.connected', d => {
  if (!d.serial) return;
  const st = S[d.serial] || (S[d.serial] = { since: 0, disc: 0 });
  st.since = Date.now();
});

tm.on('mirror.disconnected', d => {
  if (!d.serial) return;
  const st = S[d.serial] || (S[d.serial] = { since: 0, disc: 0 });
  st.since = 0;
  st.disc++;
});

tm.setInterval(() => {
  for (const m of tm.getMirrors().data || []) {
    if (!m.connected) continue;
    if (!S[m.serial]) S[m.serial] = { since: Date.now(), disc: 0 };
    render(m);
  }
}, REFRESH_MS);

for (const m of tm.getMirrors().data || []) {
  if (!m.connected) continue;
  if (!S[m.serial]) S[m.serial] = { since: Date.now(), disc: 0 };
  render(m);
}
