const FILE      = 'checklist.txt';
const RELOAD_MS = 4000;

const L = tm.lang && tm.lang() === 'en' ? {
  defaut: [
    '[ ] Almanax',
    '[ ] Archimonsters',
    '[ ] Marketplace / sales',
    '[ ] Daily quests',
  ].join('\n') + '\n',
  hint: 'checklist — edit plugins/checklist/checklist.txt for your tasks',
  uncheck: '↺ uncheck all',
  reset: 'checklist reset',
  active: 'checklist active — ',
  tasks: ' task(s) (click to check)',
} : {
  defaut: [
    '[ ] Almanax',
    '[ ] Archimonstres',
    '[ ] HDV / ventes',
    '[ ] Quêtes quotidiennes',
  ].join('\n') + '\n',
  hint: 'checklist — édite plugins/checklist/checklist.txt pour tes tâches',
  uncheck: '↺ tout décocher',
  reset: 'checklist remise à zéro',
  active: 'checklist actif — ',
  tasks: ' tâches (clic pour cocher)',
};

let items = [];
let raw = null;

function parse(txt) {
  const prev = {};
  for (const it of items) prev[it.text] = it.done;
  items = txt.split(/\r?\n/)
    .map(s => s.trim())
    .filter(s => s.length > 0)
    .map(s => {
      const m = s.match(/^\[([xX ])\]\s*(.*)$/);
      const text = m ? m[2].trim() : s;
      return { text, done: m ? /x/i.test(m[1]) : !!prev[text] };
    });
}

function save() {
  raw = items.map(it => (it.done ? '[x] ' : '[ ] ') + it.text).join('\n') + '\n';
  tm.write(FILE, raw);
}

function refresh() {
  const r = tm.read(FILE);
  const txt = r && r.ok ? r.data : null;
  if (txt == null) {
    raw = L.defaut;
    tm.write(FILE, L.defaut);
    tm.log(L.hint);
    parse(L.defaut);
    return;
  }
  if (txt !== raw) { raw = txt; parse(txt); }
}

function lines() {
  const l = items.map(it => (it.done ? '☑ ' : '☐ ') + it.text);
  l.push(L.uncheck);
  return l;
}

function render(slot) {
  tm.overlay(slot, {
    id: 'checklist', visible: true, title: 'checklist',
    compact: true, pos: 'tl', color: '#F0B232',
    lines: lines(),
  });
}

function renderAll() {
  for (const m of tm.getMirrors().data || [])
    if (m.connected) render(m.slot);
}

tm.on('overlay.line', d => {
  if (d.id !== 'checklist') return;
  const i = d.index | 0;
  if (i === items.length) {
    for (const it of items) it.done = false;
    tm.log(L.reset);
  } else if (i >= 0 && i < items.length) {
    items[i].done = !items[i].done;
    tm.log((items[i].done ? '☑ ' : '☐ ') + items[i].text);
  } else {
    return;
  }
  save();
  renderAll();
});

tm.on('mirror.connected', d => { if (d.slot) render(d.slot); });
tm.setInterval(() => { refresh(); renderAll(); }, RELOAD_MS);

refresh();
tm.log(L.active + items.length + L.tasks);
renderAll();
