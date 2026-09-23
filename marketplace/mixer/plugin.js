const RELOAD_MS = 1500;
const STEPS = [1, 0.6, 0.3, 0.1, 0];
const BAR = 8;

let last = {};

function bar(v, muted) {
  if (muted || v <= 0) return '🔇 ' + '░'.repeat(BAR) + ' muet';
  const full = Math.round(v * BAR);
  return '🔊 ' + '█'.repeat(full) + '░'.repeat(BAR - full) + ' ' + Math.round(v * 100) + '%';
}

function mirrors() {
  return (tm.getMirrors().data || []).filter(m => m.connected);
}

function linesFor() {
  return mirrors().map(m =>
    bar(m.volume == null ? 1 : m.volume, m.muted) + '  ' + (m.name || 'miroir ' + m.slot));
}

function renderAll() {
  const ms = mirrors();
  const cur = {};
  for (const m of ms)
    cur[m.slot] = (m.muted ? -1 : Math.round((m.volume == null ? 1 : m.volume) * 100)) + '|' + (m.name || '');
  const same = ms.length === Object.keys(last).length && ms.every(m => last[m.slot] === cur[m.slot]);
  if (same) return;
  last = cur;
  for (const m of ms)
    tm.overlay(m.slot, {
      id: 'mixer', visible: ms.length > 1, title: 'mixeur',
      compact: true, pos: 'bc', color: '#7BC5E0',
      lines: linesFor(),
    });
}

tm.on('overlay.line', d => {
  if (d.id !== 'mixer') return;
  const ms = mirrors();
  const i = d.index | 0;
  if (i < 0 || i >= ms.length) return;
  const m = ms[i];
  if (m.muted) {
    tm.mute(m.slot, false);
    tm.volume(m.slot, 1);
  } else {
    const cur = m.volume == null ? 1 : m.volume;
    let next = 0;
    for (let s = 0; s < STEPS.length; s++)
      if (cur > STEPS[s] + 0.001) { next = STEPS[s]; break; }
    if (next <= 0) tm.mute(m.slot, true);
    else tm.volume(m.slot, next);
  }
  last = {};
  renderAll();
});

tm.on('mirror.connected', () => { last = {}; renderAll(); });
tm.on('mirror.disconnected', () => { last = {}; renderAll(); });
tm.setInterval(renderAll, RELOAD_MS);

renderAll();
tm.log('mixer actif — clic sur une ligne pour baisser le volume par paliers');
