// splits — chrono de run affiché sur chaque miroir.
// Clic sur la ligne du chrono = poser un split (temps intermédiaire).
// Dernière ligne = reset du run : si le temps total bat ton record,
// il devient le nouveau PB (sauvegardé dans splits.json, par téléphone).
// Idéal pour les runs de donjons répétés — local uniquement.

const REFRESH_MS = 1000;
const FILE = 'splits.json';

const runs = {};  // slot -> { t0, splits: [ms] }
let pb = {};      // serial -> meilleur temps total (ms)

function fmt(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const m = Math.floor(t / 60), s = t % 60;
  return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
}

function load() {
  const r = tm.read(FILE);
  if (r && r.ok && r.data) {
    try { pb = JSON.parse(r.data) || {}; } catch (e) { pb = {}; }
  }
}

function save() {
  try { tm.write(FILE, JSON.stringify(pb)); } catch (e) {}
}

function linesFor(slot, serial) {
  const r = runs[slot];
  const el = r ? Date.now() - r.t0 : 0;
  const l = ['⏱ ' + fmt(el)];
  if (r) for (let i = 0; i < r.splits.length; i++)
    l.push('#' + (i + 1) + '  ' + fmt(r.splits[i]));
  if (serial && pb[serial]) l.push('★ PB ' + fmt(pb[serial]));
  l.push('↺ reset');
  return l;
}

function render(m) {
  tm.overlay(m.slot, {
    id: 'splits', visible: true, title: 'splits',
    compact: true, pos: 'tr', color: '#7BE0A4',
    lines: linesFor(m.slot, m.serial),
  });
}

function renderAll() {
  for (const m of tm.getMirrors().data || [])
    if (m.connected) render(m);
}

tm.on('overlay.line', d => {
  if (d.id !== 'splits') return;
  const r = runs[d.slot];
  if (!r) return;
  const m = (tm.getMirrors().data || []).find(x => x.slot === d.slot);
  const serial = m ? m.serial : '';
  const i = d.index | 0;
  const last = 1 + r.splits.length + (serial && pb[serial] ? 1 : 0);
  if (i === 0) {
    const t = Date.now() - r.t0;
    r.splits.push(t);
    tm.log('split #' + r.splits.length + ' : ' + fmt(t));
  } else if (i === last) {
    const total = Date.now() - r.t0;
    if (total > 5000 && serial && (!pb[serial] || total < pb[serial])) {
      pb[serial] = total;
      save();
      tm.log('★ nouveau record sur ' + (m ? m.name : serial) + ' : ' + fmt(total));
    } else {
      tm.log('run remis à zéro');
    }
    r.t0 = Date.now();
    r.splits = [];
  } else {
    return;
  }
  renderAll();
});

tm.on('mirror.connected', d => {
  if (d.slot != null) runs[d.slot] = { t0: Date.now(), splits: [] };
});

tm.on('mirror.disconnected', d => {
  if (d.slot != null) delete runs[d.slot];
});

tm.setInterval(() => {
  for (const m of tm.getMirrors().data || []) {
    if (!m.connected) continue;
    if (!runs[m.slot]) runs[m.slot] = { t0: Date.now(), splits: [] };
    render(m);
  }
}, REFRESH_MS);

load();
renderAll();
tm.log('splits actif — clic sur le chrono = split, reset = nouveau run');
