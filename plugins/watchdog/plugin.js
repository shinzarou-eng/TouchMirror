// Watchdog — reconnexion automatique
// Ne reconnecte que les miroirs qui ont LÂCHÉ (session coupée, câble, WiFi).
// Jamais une déconnexion volontaire, jamais un appareil qui n'avait pas de miroir.
// Réglages en haut du fichier — aucune config externe nécessaire.

const INTERVAL_MS = 3000;   // fréquence de scan des appareils prêts
const RETRY_MS    = 30000;  // délai après un échec de connexion
const SERIALS     = [];     // ex: ['RFGL22M2JQM'] pour ne surveiller que certains — [] = tous

const pending = {};    // serial -> true : miroir tombé, à reconnecter dès que l'appareil repasse prêt
const retryAfter = {};

tm.log('watchdog actif — reconnexion des miroirs tombés (scan ' + (INTERVAL_MS / 1000) + 's)');

// Un miroir vient de se fermer. manual = geste utilisateur → on respecte, on ne reconnecte pas.
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
    if (!d || !d.ready) continue;   // pas encore redétecté

    tm.log('→ ' + (d.name || serial) + ' prêt — reconnexion…');
    const r = tm.connect(serial);
    if (r && r.ok) {
      tm.log('  ' + (r.message || 'connecté'));
      delete pending[serial];
      delete retryAfter[serial];
    } else {
      tm.log('  échec — nouvel essai dans ' + (RETRY_MS / 1000) + 's (' + ((r && r.message) || '?') + ')');
      retryAfter[serial] = Date.now() + RETRY_MS;
    }
  }
}, INTERVAL_MS);
