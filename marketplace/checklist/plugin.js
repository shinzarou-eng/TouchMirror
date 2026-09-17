// checklist — bloc-note éditable affiché sur chaque miroir.
// Édite checklist.txt dans le dossier du plugin (bloc-notes !) :
// une ligne = une tâche, « [x] » / « [ ] » en préfixe pour l'état.
// Clic sur une ligne du widget = cocher/décocher (le fichier suit),
// dernière ligne = tout décocher. Le fichier est relu en continu :
// tes modifs au bloc-notes apparaissent sur le miroir en ~4 s.

const FILE      = 'checklist.txt';
const RELOAD_MS = 4000;

const DEFAUT = [
  '[ ] Almanax',
  '[ ] Archimonstres',
  '[ ] HDV / ventes',
  '[ ] Quêtes quotidiennes',
].join('\n') + '\n';

let items = []; // { text, done }
let raw = null; // dernier contenu connu du fichier

function parse(txt) {
  const prev = {};
  for (const it of items) prev[it.text] = it.done;
  items = txt.split(/\r?\n/)
    .map(s => s.trim())
    .filter(s => s.length > 0)
    .map(s => {
      const m = s.match(/^\[([xX ])\]\s*(.*)$/);
      const text = m ? m[2].trim() : s;
      // sans préfixe, on conserve l'état connu pour ce même texte
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
    // 1er lancement → fichier d'exemple à éditer
    raw = DEFAUT;
    tm.write(FILE, DEFAUT);
    tm.log('checklist — édite plugins/checklist/checklist.txt pour tes tâches');
    parse(DEFAUT);
    return;
  }
  if (txt !== raw) { raw = txt; parse(txt); }
}

function lines() {
  const l = items.map(it => (it.done ? '☑ ' : '☐ ') + it.text);
  l.push('↺ tout décocher');
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
    tm.log('checklist remise à zéro');
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
tm.log('checklist actif — ' + items.length + ' tâches (clic pour cocher)');
renderAll();
