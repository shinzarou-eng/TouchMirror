const FILE       = 'data.txt';
const RELOAD_MS  = 1500;
const MAX_LIGNES = 12;

const L = tm.lang && tm.lang() === 'en' ? {
  defaut: [
    'Write into plugins/bridge/data.txt —',
    'each line appears here, on the mirrors.',
    'Empty the file to hide this panel.',
  ].join('\n') + '\n',
  hint: 'bridge — write into plugins/bridge/data.txt to show content',
  active: 'bridge active — ',
  shown: ' line(s) shown',
} : {
  defaut: [
    'Écris dans plugins/bridge/data.txt —',
    'chaque ligne apparaît ici, sur les miroirs.',
    'Vide le fichier pour masquer ce panneau.',
  ].join('\n') + '\n',
  hint: 'bridge — écris dans plugins/bridge/data.txt pour afficher du contenu',
  active: 'bridge actif — ',
  shown: ' ligne(s) affichée(s)',
};

let raw = null;
let shown = [];

function refresh() {
  const r = tm.read(FILE);
  let txt = r && r.ok ? r.data : null;
  if (txt == null) {
    tm.write(FILE, L.defaut);
    txt = L.defaut;
    tm.log(L.hint);
  }
  if (txt === raw) return;
  raw = txt;
  shown = txt.split(/\r?\n/).map(s => s.trim()).filter(s => s.length > 0).slice(0, MAX_LIGNES);
}

function renderAll() {
  for (const m of tm.getMirrors().data || []) {
    if (!m.connected) continue;
    tm.overlay(m.slot, {
      id: 'bridge', visible: shown.length > 0, title: 'bridge',
      compact: true, pos: 'br', color: '#B48CF2',
      lines: shown,
    });
  }
}

tm.on('mirror.connected', () => renderAll());
tm.setInterval(() => { refresh(); renderAll(); }, RELOAD_MS);

refresh();
renderAll();
tm.log(L.active + shown.length + L.shown);
