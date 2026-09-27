const SWEEP_MS = 5000;

const L = tm.lang && tm.lang() === 'en' ? {
  active: 'automute active — only the focused mirror has sound',
  focus: 'focus',
  conn: 'connection',
  boot: 'startup',
  slot: 'slot ',
} : {
  active: 'automute actif — un seul miroir sonore (l\'actif)',
  focus: 'focus',
  conn: 'connexion',
  boot: 'démarrage',
  slot: 'slot ',
};

tm.log(L.active);

function enforce(src) {
  const mirrors = (tm.getMirrors().data || []).filter(m => m.connected);
  const active = mirrors.find(m => m.active);
  for (const m of mirrors) {
    const want = !active || m.slot !== active.slot;
    if (!!m.muted !== want) {
      const r = tm.mute(m.slot, want);
      if (r && r.ok)
        tm.log((want ? '🔇 ' : '🔊 ') + (m.name || L.slot + m.slot) + (src ? ' — ' + src : ''));
    }
  }
}

tm.on('mirror.active', () => enforce(L.focus));
tm.on('mirror.connected', () => enforce(L.conn));
tm.setInterval(() => enforce(), SWEEP_MS);
enforce(L.boot);
