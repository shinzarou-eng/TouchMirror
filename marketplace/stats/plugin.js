// Badge « résolution + fps » en haut à droite de chaque miroir —
// mode compact de l'overlay (sans courbe), relevé chaque seconde.
const armed = {};

tm.log('stats actif — badge résolution + fps sur les miroirs');

tm.setInterval(() => {
  const mirrors = tm.getMirrors().data || [];
  for (const m of mirrors) {
    if (!m.connected) continue;
    if (!armed[m.slot]) {
      tm.overlay(m.slot, { visible: true, title: 'stats', pos: 'tr', compact: true });
      armed[m.slot] = true;
    }
    tm.overlay(m.slot, { title: m.w ? m.w + '×' + m.h : 'stats' });
    tm.push(m.slot, { value: m.fps, label: Math.round(m.fps) + ' fps' });
  }
  const live = mirrors.map(m => m.slot);
  for (const s of Object.keys(armed))
    if (!live.includes(+s)) delete armed[s];
}, 1000);
