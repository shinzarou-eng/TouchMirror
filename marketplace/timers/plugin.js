const FILE      = 'timers.txt';
const RELOAD_MS = 4000;
const TICK_MS   = 1000;

const DEFAUT = [
  'Percepteur = 45m',
  'Repop boss = 1h',
  'File koli = 10m',
].join('\n') + '\n';

let defs = [];
let running = {};
let raw = null;

function parseDur(s) {
  s = s.trim().toLowerCase();
  const m = s.match(/^(\d+(?:[.,]\d+)?)([smh])?$/);
  if (m)
    return parseFloat(m[1].replace(',', '.')) * (m[2] === 'h' ? 3600 : m[2] === 's' ? 1 : 60);
  const h = s.match(/^(\d+)h(\d+)?$/);
  if (h)
    return (+h[1]) * 3600 + (h[2] ? +h[2] : 0) * 60;
  const mm = s.match(/^(\d+)m(\d+)?$/);
  if (mm)
    return (+mm[1]) * 60 + (mm[2] ? +mm[2] : 0);
  return NaN;
}

function fmtDur(sec) {
  sec = Math.round(sec);
  const h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60), s = sec % 60;
  if (h > 0)
    return m > 0 ? h + 'h' + String(m).padStart(2, '0') : h + 'h';
  if (m > 0)
    return s > 0 ? m + 'm' + String(s).padStart(2, '0') : m + 'm';
  return s + 's';
}

function fmtLeft(ms) {
  const t = Math.max(0, Math.ceil(ms / 1000));
  const h = Math.floor(t / 3600), m = Math.floor(t % 3600 / 60), s = t % 60;
  return h > 0
    ? h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0')
    : m + ':' + String(s).padStart(2, '0');
}

function parse(txt) {
  defs = [];
  for (const line of txt.split(/\r?\n/)) {
    const s = line.trim();
    if (!s || s.startsWith('#'))
      continue;
    const eq = s.indexOf('=');
    const name = (eq < 0 ? s : s.slice(0, eq)).trim();
    const durTxt = eq < 0 ? '' : s.slice(eq + 1).trim();
    const dur = parseDur(durTxt);
    if (name && isFinite(dur) && dur > 0)
      defs.push({ name, dur });
  }
  for (const k of Object.keys(running))
    if (!defs.some(d => d.name === k))
      delete running[k];
}

function refresh() {
  const r = tm.read(FILE);
  const txt = r && r.ok ? r.data : null;
  if (txt == null) {
    raw = DEFAUT;
    tm.write(FILE, DEFAUT);
    tm.log('timers — édite plugins/timers/timers.txt : « Nom = durée » (45m, 1h30, 90s)');
    parse(DEFAUT);
    return;
  }
  if (txt !== raw) { raw = txt; parse(txt); }
}

function lines() {
  const now = Date.now();
  return defs.map(d => {
    const t = running[d.name];
    if (!t)
      return '▶ ' + d.name + ' · ' + fmtDur(d.dur);
    const left = t.end - now;
    if (left <= 0)
      return '⏰ ' + d.name + ' · terminé !';
    return '⏳ ' + d.name + ' · ' + fmtLeft(left);
  });
}

function render(slot) {
  tm.overlay(slot, {
    id: 'timers', visible: defs.length > 0, title: 'timers',
    compact: true, pos: 'bl', color: '#F0B232',
    lines: lines(),
  });
}

function renderAll() {
  for (const m of tm.getMirrors().data || [])
    if (m.connected) render(m.slot);
}

tm.on('overlay.line', d => {
  if (d.id !== 'timers') return;
  const i = d.index | 0;
  if (i < 0 || i >= defs.length) return;
  const def = defs[i];
  const t = running[def.name];
  if (!t) {
    running[def.name] = { end: Date.now() + def.dur * 1000 };
    tm.log('⏱ ' + def.name + ' lancé — ' + fmtDur(def.dur));
  } else {
    delete running[def.name];
    tm.log('⏱ ' + def.name + ' réarmé');
  }
  renderAll();
});

tm.on('mirror.connected', () => renderAll());
tm.setInterval(() => { refresh(); }, RELOAD_MS);
tm.setInterval(() => { renderAll(); }, TICK_MS);

refresh();
renderAll();
tm.log('timers actif — ' + defs.length + ' minuteur(s)');
