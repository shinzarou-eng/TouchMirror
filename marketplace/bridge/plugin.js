const FILE       = 'data.txt';
const RELOAD_MS  = 1500;
const MAX_LIGNES = 12;

const DEFAUT = [
  'Écris dans plugins/bridge/data.txt —',
  'chaque ligne apparaît ici, sur les miroirs.',
  'Vide le fichier pour masquer ce panneau.',
].join('\n') + '\n';

let raw = null;
let shown = [];

function refresh() {
  const r = tm.read(FILE);
  let txt = r && r.ok ? r.data : null;
  if (txt == null) {
    tm.write(FILE, DEFAUT);
    txt = DEFAUT;
    tm.log('bridge — écris dans plugins/bridge/data.txt pour afficher du contenu');
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
tm.log('bridge actif — ' + shown.length + ' ligne(s) affichée(s)');
