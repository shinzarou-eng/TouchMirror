// Graphe déplaçable sur chaque miroir : courbe FPS du flux vidéo,
// relevée chaque seconde. tm.overlay() affiche le widget,
// tm.push() alimente la courbe — le panneau se déplace à la souris.
const armed = {};

tm.log('graphs actif — courbe FPS sur les miroirs');

tm.setInterval(() => {
  const mirrors = tm.getMirrors().data || [];
  for (const m of mirrors) {
    if (!m.connected) continue;
    if (!armed[m.slot]) {
      tm.overlay(m.slot, { visible: true, title: 'FPS', color: '#3ECF8E' });
      armed[m.slot] = true;
    }
    tm.push(m.slot, m.fps);
  }
  const live = mirrors.map(m => m.slot);
  for (const s of Object.keys(armed))
    if (!live.includes(+s)) delete armed[s];
}, 1000);
