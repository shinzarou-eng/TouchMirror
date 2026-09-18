const SWEEP_MS = 5000;

tm.log('automute actif — un seul miroir sonore (l\'actif)');

function enforce(src) {
  const mirrors = (tm.getMirrors().data || []).filter(m => m.connected);
  const active = mirrors.find(m => m.active);
  for (const m of mirrors) {
    const want = !active || m.slot !== active.slot;
    if (!!m.muted !== want) {
      const r = tm.mute(m.slot, want);
      if (r && r.ok)
        tm.log((want ? '🔇 ' : '🔊 ') + (m.name || 'slot ' + m.slot) + (src ? ' — ' + src : ''));
    }
  }
}

tm.on('mirror.active', () => enforce('focus'));
tm.on('mirror.connected', () => enforce('connexion'));
tm.setInterval(() => enforce(), SWEEP_MS);
enforce('démarrage');
