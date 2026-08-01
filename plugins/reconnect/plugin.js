const INTERVAL_MS = 3000;
const RETRY_MS    = 30000;
const SERIALS     = [];

const pending = {};
const retryAfter = {};

tm.log('reconnect actif — scan ' + (INTERVAL_MS / 1000) + 's');

tm.on('mirror.disconnected', d => {
  const serial = d.serial;
  if (!serial) return;
  if (d.manual) {
    tm.log('✋ ' + (d.name || serial) + ' déconnecté volontairement — ignoré');
    delete pending[serial];
    delete retryAfter[serial];
    return;
  }
  if (SERIALS.length && !SERIALS.includes(serial)) return;
  tm.log('✗ ' + (d.name || serial) + ' a lâché — reconnexion dès que prêt');
  pending[serial] = true;
});

tm.setInterval(() => {
  const devices = tm.getDevices().data || [];
  const mirrors = tm.getMirrors().data || [];
  const live = mirrors.map(m => m.serial);

  for (const serial of Object.keys(pending)) {
    if (live.includes(serial)) { delete pending[serial]; continue; }
    if (retryAfter[serial] && Date.now() < retryAfter[serial]) continue;
    const d = devices.find(x => x.serial === serial);
    if (!d || !d.ready) continue;

    tm.log('→ ' + (d.name || serial) + ' prêt — reconnexion…');
    const r = tm.connect(serial);
    if (r && r.ok) {
      tm.log('  ' + (r.message || 'connecté'));
      delete pending[serial];
      delete retryAfter[serial];
    } else {
      tm.log('  échec — nouvel essai dans ' + (RETRY_MS / 1000) + 's');
      retryAfter[serial] = Date.now() + RETRY_MS;
    }
  }
}, INTERVAL_MS);
