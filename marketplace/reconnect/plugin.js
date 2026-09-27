const INTERVAL_MS  = 3000;
const RETRY_MS     = 30000;
const FREEZE_POLLS = 4;
const FREEZE_DELAY = 5000;
const SERIALS      = [];

const L = tm.lang && tm.lang() === 'en' ? {
  active: 'reconnect active — ' + (INTERVAL_MS / 1000) + 's scan + frozen stream detection',
  manual: ' disconnected manually — ignored',
  dropped: ' dropped — reconnecting as soon as ready',
  ready: ' ready — reconnecting…',
  connected: 'connected',
  fail: '  failed — retrying in ' + (RETRY_MS / 1000) + 's',
  frozen: ' : stream frozen — restarting session',
  ovFrozen: '⚠ stream frozen',
  ovBack: 'reconnecting…',
} : {
  active: 'reconnect actif — scan ' + (INTERVAL_MS / 1000) + 's + détection flux figé',
  manual: ' déconnecté volontairement — ignoré',
  dropped: ' a lâché — reconnexion dès que prêt',
  ready: ' prêt — reconnexion…',
  connected: 'connecté',
  fail: '  échec — nouvel essai dans ' + (RETRY_MS / 1000) + 's',
  frozen: ' : flux figé — session relancée',
  ovFrozen: '⚠ flux figé',
  ovBack: 'reconnexion…',
};

const pending = {};
const retryAfter = {};
const frozen = {};
const zeroFps = {};

tm.log(L.active);

tm.on('mirror.disconnected', d => {
  const serial = d.serial;
  if (!serial) return;
  if (d.manual) {
    if (!frozen[serial]) {
      tm.log('✋ ' + (d.name || serial) + L.manual);
      delete pending[serial];
      delete retryAfter[serial];
    }
    return;
  }
  if (SERIALS.length && !SERIALS.includes(serial)) return;
  tm.log('✗ ' + (d.name || serial) + L.dropped);
  pending[serial] = true;
});

function tryConnect(serial, d) {
  if (retryAfter[serial] && Date.now() < retryAfter[serial]) return;
  tm.log('→ ' + (d.name || serial) + L.ready);
  const r = tm.connect(serial);
  if (r && r.ok) {
    tm.log('  ' + (r.message || L.connected));
    delete pending[serial];
    delete frozen[serial];
    delete retryAfter[serial];
  } else {
    tm.log(L.fail);
    retryAfter[serial] = Date.now() + RETRY_MS;
  }
}

tm.setInterval(() => {
  const devices = tm.getDevices().data || [];
  const mirrors = tm.getMirrors().data || [];
  const live = mirrors.map(m => m.serial);

  for (const m of mirrors) {
    if (!m.connected) { delete zeroFps[m.serial]; continue; }
    if (m.fps < 1) {
      zeroFps[m.serial] = (zeroFps[m.serial] || 0) + 1;
      if (zeroFps[m.serial] === FREEZE_POLLS && !frozen[m.serial]) {
        frozen[m.serial] = Date.now();
        tm.log('⚠ ' + (m.name || m.serial) + L.frozen);
        tm.overlay(m.slot, {
          id: 'reconnect', visible: true, title: 'reconnect',
          compact: true, pos: 'tl', color: '#FF5A5A',
          lines: [L.ovFrozen, L.ovBack],
        });
        tm.disconnect(m.slot);
      }
    } else {
      delete zeroFps[m.serial];
    }
  }

  for (const serial of Object.keys(frozen)) {
    if (live.includes(serial)) { delete frozen[serial]; continue; }
    if (Date.now() - frozen[serial] < FREEZE_DELAY) continue;
    const d = devices.find(x => x.serial === serial);
    if (d && d.ready) tryConnect(serial, d);
  }

  for (const serial of Object.keys(pending)) {
    if (live.includes(serial)) { delete pending[serial]; continue; }
    const d = devices.find(x => x.serial === serial);
    if (!d || !d.ready) continue;
    tryConnect(serial, d);
  }
}, INTERVAL_MS);
