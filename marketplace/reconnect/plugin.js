const INTERVAL_MS  = 3000;
const RETRY_MS     = 30000;
const FREEZE_POLLS = 4;   // ~12 s de flux figé avant action
const FREEZE_DELAY = 5000; // délai avant de retenter la connexion
const SERIALS      = [];

const pending = {};
const retryAfter = {};
const frozen = {};   // serial -> timestamp de la détection
const zeroFps = {};  // serial -> nb de scans à fps < 1

tm.log('reconnect actif — scan ' + (INTERVAL_MS / 1000) + 's + détection flux figé');

tm.on('mirror.disconnected', d => {
  const serial = d.serial;
  if (!serial) return;
  if (d.manual) {
    if (!frozen[serial]) {
      tm.log('✋ ' + (d.name || serial) + ' déconnecté volontairement — ignoré');
      delete pending[serial];
      delete retryAfter[serial];
    }
    return;
  }
  if (SERIALS.length && !SERIALS.includes(serial)) return;
  tm.log('✗ ' + (d.name || serial) + ' a lâché — reconnexion dès que prêt');
  pending[serial] = true;
});

function tryConnect(serial, d) {
  if (retryAfter[serial] && Date.now() < retryAfter[serial]) return;
  tm.log('→ ' + (d.name || serial) + ' prêt — reconnexion…');
  const r = tm.connect(serial);
  if (r && r.ok) {
    tm.log('  ' + (r.message || 'connecté'));
    delete pending[serial];
    delete frozen[serial];
    delete retryAfter[serial];
  } else {
    tm.log('  échec — nouvel essai dans ' + (RETRY_MS / 1000) + 's');
    retryAfter[serial] = Date.now() + RETRY_MS;
  }
}

tm.setInterval(() => {
  const devices = tm.getDevices().data || [];
  const mirrors = tm.getMirrors().data || [];
  const live = mirrors.map(m => m.serial);

  // ── flux figé : connecté mais 0 fps en continu ──
  for (const m of mirrors) {
    if (!m.connected) { delete zeroFps[m.serial]; continue; }
    if (m.fps < 1) {
      zeroFps[m.serial] = (zeroFps[m.serial] || 0) + 1;
      if (zeroFps[m.serial] === FREEZE_POLLS && !frozen[m.serial]) {
        frozen[m.serial] = Date.now();
        tm.log('⚠ ' + (m.name || m.serial) + ' : flux figé — session relancée');
        tm.overlay(m.slot, {
          id: 'reconnect', visible: true, title: 'reconnect',
          compact: true, pos: 'tl', color: '#FF5A5A',
          lines: ['⚠ flux figé', 'reconnexion…'],
        });
        tm.disconnect(m.slot);
      }
    } else {
      delete zeroFps[m.serial];
    }
  }

  // ── reconnexion des sessions figées (disconnect manuel → connect explicite) ──
  for (const serial of Object.keys(frozen)) {
    if (live.includes(serial)) { delete frozen[serial]; continue; }
    if (Date.now() - frozen[serial] < FREEZE_DELAY) continue;
    const d = devices.find(x => x.serial === serial);
    if (d && d.ready) tryConnect(serial, d);
  }

  // ── reconnexion des sessions tombées ──
  for (const serial of Object.keys(pending)) {
    if (live.includes(serial)) { delete pending[serial]; continue; }
    const d = devices.find(x => x.serial === serial);
    if (!d || !d.ready) continue;
    tryConnect(serial, d);
  }
}, INTERVAL_MS);
