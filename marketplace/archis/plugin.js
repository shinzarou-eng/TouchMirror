const FILE = 'archis.txt';
const RELOAD_MS = 5000;
const PAGE = 9;

const DATA = `# Avis de recherche
[ ] Fouduglen L'écureuil · 15
[ ] Frakacia Leukocytine · 50
[ ] Ogivol Scalarcin · 50
[ ] Brumen Tinctorias · 60
[ ] Marzwel le Gobelin · 60
[ ] Aermyne 'Braco' Scalptaras · 70
[ ] Qil Bil · 70
[ ] Musha L'Oni · 80
[ ] May Shrebell · 90
[ ] Domoizelle · 120
[ ] Rok Gnorok · 120
[ ] Padgref Demoël · 126
[ ] Zatoïshwan · 150
[ ] Ali Grothor · 170
[ ] Ka'Youloud · 200

# Archis niv. 1-20
[ ] Arachitik la Souffreteuse · 19
[ ] Araknay la Galopante · 14
[ ] Champayt l'Odorant · 20
[ ] Craraboss le Féérique · 12
[ ] Gobstiniais le Têtu · 20

# Archis niv. 21-40 · 1/4
[ ] Aboudbra le Porteur · 35
[ ] Abrakadnuzar · 40
[ ] Abrakroc l'édenté · 38
[ ] Ameur la Laide · 35
[ ] Arabord la Cruche · 40
[ ] Bandapar l'Exclu · 22
[ ] Bandson le Tonitruant · 37
[ ] Barchwork le Multicolore · 37
[ ] Bi le Partageur · 24
[ ] Bilvoezé le Bonimenteur · 24
[ ] Bistou le Quêteur · 24
[ ] Bistou le Rieur · 24
[ ] Boombata le Garde · 39
[ ] Boostif l'Affamé · 25
[ ] Boudur le Raide · 35
[ ] Bwormage le Respectueux · 39

# Archis niv. 21-40 · 2/4
[ ] Chafalfer l'Optimiste · 25
[ ] Chafemal le Bagarreur · 32
[ ] Chaffoin le Sournois · 33
[ ] Chafmarcel le Fêtard · 40
[ ] Chafrit le Barbare · 40
[ ] Chalan le Commerçant · 40
[ ] Chamchie le Difficile · 24
[ ] Chamdblé le Cultivé · 29
[ ] Chamflay le Ballonné · 30
[ ] Champayr le Disjoncté · 32
[ ] Chevaustine le Reconstruit · 27
[ ] Codenlgaz le Problème · 35
[ ] Corpat le Vampire · 24
[ ] Craquetou le Fissuré · 40
[ ] Crognan le Barbare · 35
[ ] Cromikay le Néophyte · 35

# Archis niv. 21-40 · 3/4
[ ] Cruskof le Rustre · 30
[ ] Crusmeyer le Pervers · 30
[ ] Crustensyl le Pragmatique · 30
[ ] Crustterus l'Organique · 30
[ ] Ginsenk le Stimulant · 35
[ ] Kiroyal le Sirupeux · 35
[ ] Koktèle le Secoué · 40
[ ] Kolforthe l'Indécollable · 25
[ ] Kwoanneur le Frimeur · 26
[ ] Larchimaide la Poussée · 30
[ ] Larvapstrè le Subjectif · 32
[ ] Larvonika l'Instrument · 29
[ ] Let le Rond · 35
[ ] Maître Amboat le Moqueur · 37
[ ] Milipatte la Griffe · 24
[ ] Minsinistre l'Elu · 39

# Archis niv. 21-40 · 4/4
[ ] Nelvin le Boulet · 35
[ ] Nipulnislip l'Exhibitionniste · 35
[ ] NodKoku le Trahi · 40
[ ] Osuxion le Vampirique · 35
[ ] Palmbytch la Bronzée · 28
[ ] Palmiche le Serein · 28
[ ] Palmiflette le Convivial · 28
[ ] Palmito le Menteur · 28

# Archis niv. 41-60 · 1/4
[ ] Arakule la Revancharde · 51
[ ] Barebourd le Comte · 52
[ ] Blof l'Apathique · 50
[ ] Bloporte le Veule · 50
[ ] Blordur l'Infect · 50
[ ] Blorie L'assourdissante · 50
[ ] Boudalf le Blanc · 50
[ ] Boufdégou le Refoulant · 51
[ ] Bouflet le Puéril · 52
[ ] Boulgourvil le Lointain · 53
[ ] Bourdilleu le Social · 58
[ ] Bworkasse le Dégoutant · 44
[ ] Caboume l'Artilleur · 51
[ ] Cavordemal le Sorcier · 57
[ ] Chamitant le Dillettante · 50
[ ] Chonstip la Passagère · 58

# Archis niv. 41-60 · 2/4
[ ] Corboyard l'Enigmatique · 48
[ ] Crakmitaine le Faucheur · 55
[ ] Cramikaz le Suicidaire · 59
[ ] Craquetuss le Piquant · 50
[ ] Crathdogue le Cruel · 50
[ ] Crolnareff l'Exilé · 42
[ ] Dragkouine la Magnifique · 60
[ ] Draglida la Disparue · 55
[ ] Dragmoclaiss le Fataliste · 60
[ ] Dragnostik le Sceptique · 60
[ ] Dragnoute l'Irascible · 60
[ ] Dragsta le Détendu · 50
[ ] Dragstayr le Fonceur · 60
[ ] Dragstik le Frustre · 50
[ ] Dragstore le Généraliste · 50
[ ] Dragtula l'Ancien · 50

# Archis niv. 41-60 · 3/4
[ ] Fandanleuil le Précis · 57
[ ] Fanlabiz le Véloce · 57
[ ] Fantoch le Pantin · 57
[ ] Fantrask le Rêveur · 57
[ ] Forboyar l'Enigmatique · 41
[ ] Garsim le Mort · 41
[ ] Gelanal le Huileux · 46
[ ] Gelaviv le Glaçon · 54
[ ] Geloliaine l'Aérien · 50
[ ] Grandilok le Clameur · 52
[ ] Grokosto le Bosco · 42
[ ] Kanasukr le Mielleux · 60
[ ] Kannémik le Maigre · 53
[ ] Kannibal le Lecteur · 53
[ ] Kannisterik le Forcené · 53
[ ] Kapota la Fraise · 53

# Archis niv. 41-60 · 4/4
[ ] Kido l'Âtre · 53
[ ] Kilimanj'haro le Grimpeur · 54
[ ] Koalastrof la Naturelle · 60
[ ] Maître Onom le Régulier · 60
[ ] Mosketère le Dévoué · 45
[ ] Nakuneuye le Borgne · 51
[ ] Ouassébo l'Esthète · 44
[ ] Ouature la Mobile · 42
[ ] Ougaould le Parasite · 45

# Archis niv. 61-80 · 1/2
[ ] Abrakanette l'Encapsulé · 78
[ ] Abrakildas le Vénérable · 74
[ ] Abraklette le Fondant · 80
[ ] Crok le Beau · 61
[ ] Diskord le Belliqueux · 79
[ ] Doktopuss le Maléfique · 70
[ ] Dragma le Bouillant · 70
[ ] Dragoeth le Penseur · 70
[ ] Dragoo le Cramoisi · 70
[ ] Dragtonien le Malvoyant · 70
[ ] Gloubibou le Gars · 80
[ ] Koakofrui le Confit · 80
[ ] Koamaembair le Coulant · 80
[ ] Koarmit la Batracienne · 80
[ ] Koaskette la Chapelière · 80
[ ] Koasossyal le Psychopathe · 80

# Archis niv. 61-80 · 2/2
[ ] Mufguedin le Suprême · 62
[ ] Pékeutar le Tireur · 80

# Archis niv. 81-100 · 1/2
[ ] Bouliver le Géant · 87
[ ] Dragalgan l'Effervescent · 84
[ ] Dragdikal le Décisif · 100
[ ] Dragioli le Succulent · 84
[ ] Dragobert le Monarque · 100
[ ] Dragtopaile l'Excavateur · 84
[ ] Dragybuss le Sucré · 84
[ ] Drakolage le Tentateur · 90
[ ] Farlon l'Enfant · 90
[ ] Fossamoel le Juteux · 100
[ ] Fourapin le Chaud · 82
[ ] Gastroth la Contagieuse · 91
[ ] Guerrite le Veilleur · 100
[ ] Guerumoth le Collant · 83
[ ] Mamakomou l'Âge · 90
[ ] Muloufok l'Hilarant · 92

# Archis niv. 81-100 · 2/2
[ ] Ouashouash l'Exubérant · 85

# Archis niv. 101-120 · 1/2
[ ] Bitoven le Musicien · 117
[ ] Brouste l'Humiliant · 109
[ ] Chiendanlémin l'Illusionniste · 104
[ ] Don Kizoth l'Obstiné · 112
[ ] Drageaufol la Joyeuse · 110
[ ] Dragminster le Magicien · 110
[ ] Dragonienne l'Econome · 102
[ ] Dragtarus le Bellâtre · 110
[ ] Draquetteur le Voleur · 110
[ ] Ecorfé la Vive · 111
[ ] Faufoll la Joyeuse · 108
[ ] Floanna la Blonde · 116
[ ] Germinol l'Indigent · 120
[ ] Koalaboi le Calorifère · 104
[ ] Koalvissie le Chauve · 102
[ ] Koamag'oel le Défiguré · 102

# Archis niv. 101-120 · 2/2
[ ] Krapahut le Randonneur · 110
[ ] Maître Koantik le Théoricien · 108
[ ] Mandalo l'Aqueuse · 110
[ ] Minoskour le Sauveur · 110
[ ] Momikonos la Bandelette · 106
[ ] Nerdeubeu le Flagellant · 104

# Archis niv. 121-140
[ ] Abrakine le Sombre · 125
[ ] Arakazam la Psychique · 132
[ ] Arapliké la Calligraphe · 130
[ ] Dardamel la Kidnappeuse · 129
[ ] Gargantua la Dévoreuse · 131

# Archis niv. 141-160
[ ] Abrinos le Clair · 150
[ ] Kaskapointhe la Couverte · 146
[ ] Meuroup le Prêtre · 143

# Archis niv. 161-180
[ ] Chamilero le Malchanceux · 167
[ ] Chamoute le Duveteux · 173
[ ] Champolyon le Polyglotte · 179
[ ] Champoul l'Illuminé · 176

# Archis niv. 181-200
[ ] Champmé le Méchant · 182
[ ] Nanashi le virtuose · 200

# Archis niv. 200+
[ ] Kaenekfeu le volubile · 201
[ ] Marude l'ensablé · 201
[ ] Onistérique le déchainé · 203`;

let sections = [];
let cur = 0;
let page = 0;
let view = 'zone';
let raw = null;
let map = [];

function parse(txt) {
  const prev = {};
  for (const s of sections)
    for (const it of s.items) prev[it.name] = it.done;
  sections = [];
  let sec = null;
  for (const l of txt.split(/\r?\n/)) {
    const s = l.trim();
    if (!s) continue;
    const h = s.match(/^#\s*(.+)$/);
    if (h) { sec = { name: h[1].trim(), items: [] }; sections.push(sec); continue; }
    if (!sec) { sec = { name: 'Archis', items: [] }; sections.push(sec); }
    const m = s.match(/^\[([xX ])\]\s*(.*)$/);
    const name = (m ? m[2] : s).trim();
    if (name) sec.items.push({ name, done: m ? /x/i.test(m[1]) : !!prev[name] });
  }
  if (cur >= sections.length) cur = 0;
}

const GFX = {
  "Aboudbra le Porteur": 86,
  "Abrakadnuzar": 345,
  "Abrakanette l'Encapsulé": 144,
  "Abrakildas le Vénérable": 13,
  "Abrakine le Sombre": 601,
  "Abraklette le Fondant": 346,
  "Abrakroc l'édenté": 13,
  "Abrinos le Clair": 571,
  "Aermyne 'Braco' Scalptaras": 240,
  "Ali Grothor": 1047,
  "Ameur la Laide": 85,
  "Arabord la Cruche": 149,
  "Arachitik la Souffreteuse": 261,
  "Araknay la Galopante": 145,
  "Arakule la Revancharde": 15,
  "Bandapar l'Exclu": 255,
  "Bandson le Tonitruant": 661,
  "Barchwork le Multicolore": 29,
  "Barebourd le Comte": 573,
  "Bi le Partageur": 168,
  "Bilvoezé le Bonimenteur": 167,
  "Bistou le Quêteur": 166,
  "Bistou le Rieur": 169,
  "Bitoven le Musicien": 566,
  "Blof l'Apathique": 165,
  "Bloporte le Veule": 163,
  "Blordur l'Infect": 164,
  "Blorie L'assourdissante": 162,
  "Boombata le Garde": 124,
  "Boostif l'Affamé": 59,
  "Boudalf le Blanc": 73,
  "Boudur le Raide": 79,
  "Boufdégou le Refoulant": 3,
  "Bouflet le Puéril": 75,
  "Boulgourvil le Lointain": 76,
  "Bouliver le Géant": 583,
  "Brouste l'Humiliant": 597,
  "Brumen Tinctorias": 253,
  "Bworkasse le Dégoutant": 489,
  "Bwormage le Respectueux": 10,
  "Caboume l'Artilleur": 127,
  "Cavordemal le Sorcier": 191,
  "Chafalfer l'Optimiste": 57,
  "Chafemal le Bagarreur": 16,
  "Chaffoin le Sournois": 193,
  "Chafmarcel le Fêtard": 179,
  "Chafrit le Barbare": 180,
  "Chalan le Commerçant": 178,
  "Chamchie le Difficile": 21,
  "Chamdblé le Cultivé": 348,
  "Chamflay le Ballonné": 350,
  "Chamilero le Malchanceux": 635,
  "Chamitant le Dillettante": 241,
  "Chamoute le Duveteux": 636,
  "Champayr le Disjoncté": 347,
  "Champayt l'Odorant": 349,
  "Champmé le Méchant": 637,
  "Champolyon le Polyglotte": 639,
  "Champoul l'Illuminé": 638,
  "Chevaustine le Reconstruit": 23,
  "Chiendanlémin l'Illusionniste": 600,
  "Chonstip la Passagère": 68,
  "Codenlgaz le Problème": 91,
  "Corboyard l'Enigmatique": 560,
  "Corpat le Vampire": 170,
  "Crakmitaine le Faucheur": 4,
  "Cramikaz le Suicidaire": 181,
  "Craquetou le Fissuré": 562,
  "Craquetuss le Piquant": 268,
  "Craraboss le Féérique": 24,
  "Crathdogue le Cruel": 561,
  "Crognan le Barbare": 673,
  "Crok le Beau": 152,
  "Crolnareff l'Exilé": 151,
  "Cromikay le Néophyte": 187,
  "Cruskof le Rustre": 587,
  "Crusmeyer le Pervers": 586,
  "Crustensyl le Pragmatique": 585,
  "Crustterus l'Organique": 584,
  "Diskord le Belliqueux": 579,
  "Doktopuss le Maléfique": 414,
  "Domoizelle": 1326,
  "Don Kizoth l'Obstiné": 576,
  "Dragdikal le Décisif": 478,
  "Drageaufol la Joyeuse": 490,
  "Dragioli le Succulent": 678,
  "Draglida la Disparue": 96,
  "Dragminster le Magicien": 491,
  "Dragmoclaiss le Fataliste": 41,
  "Dragnostik le Sceptique": 42,
  "Dragnoute l'Irascible": 106,
  "Dragobert le Monarque": 482,
  "Dragonienne l'Econome": 480,
  "Dragstayr le Fonceur": 31,
  "Dragtarus le Bellâtre": 492,
  "Drakolage le Tentateur": 427,
  "Draquetteur le Voleur": 481,
  "Ecorfé la Vive": 599,
  "Fandanleuil le Précis": 201,
  "Fanlabiz le Véloce": 514,
  "Fantoch le Pantin": 203,
  "Fantrask le Rêveur": 202,
  "Farlon l'Enfant": 334,
  "Faufoll la Joyeuse": 433,
  "Floanna la Blonde": 564,
  "Forboyar l'Enigmatique": 66,
  "Fossamoel le Juteux": 426,
  "Fouduglen L'écureuil": 206,
  "Fourapin le Chaud": 116,
  "Frakacia Leukocytine": 250,
  "Garsim le Mort": 281,
  "Gastroth la Contagieuse": 577,
  "Gelanal le Huileux": 17,
  "Gelaviv le Glaçon": 19,
  "Geloliaine l'Aérien": 18,
  "Germinol l'Indigent": 357,
  "Ginsenk le Stimulant": 94,
  "Gloubibou le Gars": 115,
  "Gobstiniais le Têtu": 99,
  "Grandilok le Clameur": 50,
  "Guerrite le Veilleur": 416,
  "Guerumoth le Collant": 578,
  "Ka'Youloud": 1541,
  "Kanasukr le Mielleux": 175,
  "Kannibal le Lecteur": 110,
  "Kannisterik le Forcené": 111,
  "Kannémik le Maigre": 113,
  "Kapota la Fraise": 112,
  "Kaskapointhe la Couverte": 568,
  "Kido l'Âtre": 593,
  "Kilimanj'haro le Grimpeur": 594,
  "Kiroyal le Sirupeux": 90,
  "Koakofrui le Confit": 419,
  "Koalaboi le Calorifère": 422,
  "Koalastrof la Naturelle": 415,
  "Koalvissie le Chauve": 411,
  "Koamaembair le Coulant": 418,
  "Koamag'oel le Défiguré": 424,
  "Koarmit la Batracienne": 420,
  "Koaskette la Chapelière": 417,
  "Koasossyal le Psychopathe": 435,
  "Koktèle le Secoué": 109,
  "Kolforthe l'Indécollable": 188,
  "Krapahut le Randonneur": 467,
  "Kwoanneur le Frimeur": 80,
  "Larchimaide la Poussée": 2,
  "Larvapstrè le Subjectif": 12,
  "Larvonika l'Instrument": 1,
  "Let le Rond": 93,
  "Mamakomou l'Âge": 421,
  "Mandalo l'Aqueuse": 466,
  "Marzwel le Gobelin": 99,
  "Maître Amboat le Moqueur": 69,
  "Maître Koantik le Théoricien": 423,
  "Meuroup le Prêtre": 570,
  "Milipatte la Griffe": 84,
  "Minoskour le Sauveur": 465,
  "Minsinistre l'Elu": 65,
  "Momikonos la Bandelette": 425,
  "Mosketère le Dévoué": 22,
  "Mufguedin le Suprême": 592,
  "Muloufok l'Hilarant": 52,
  "Musha L'Oni": 141,
  "Nakuneuye le Borgne": 125,
  "Nelvin le Boulet": 89,
  "Nerdeubeu le Flagellant": 598,
  "Nipulnislip l'Exhibitionniste": 88,
  "NodKoku le Trahi": 117,
  "Ogivol Scalarcin": 252,
  "Osuxion le Vampirique": 87,
  "Ouashouash l'Exubérant": 279,
  "Ouassébo l'Esthète": 280,
  "Ouature la Mobile": 279,
  "Ougaould le Parasite": 697,
  "Padgref Demoël": 249,
  "Palmbytch la Bronzée": 588,
  "Palmiche le Serein": 591,
  "Palmiflette le Convivial": 590,
  "Palmito le Menteur": 589,
  "Pékeutar le Tireur": 436,
  "Qil Bil": 266,
  "Rok Gnorok": 268,
  "Zatoïshwan": 294
};
const IMGURL = 'https://api.dofusdb.fr/img/monsters/';

function save() {
  const out = [];
  for (const s of sections) {
    out.push('# ' + s.name);
    for (const it of s.items) out.push((it.done ? '[x] ' : '[ ] ') + it.name);
    out.push('');
  }
  raw = out.join('\n');
  tm.write(FILE, raw);
}

function refresh() {
  const r = tm.read(FILE);
  const txt = r && r.ok ? r.data : null;
  if (txt == null) {
    tm.write(FILE, DATA);
    raw = DATA;
    parse(DATA);
    tm.log('archis — liste complète dans archis.txt, éditable au bloc-notes');
    return;
  }
  if (txt !== raw) { raw = txt; parse(txt); }
}

function doneCount(s) { return s.items.filter(i => i.done).length; }

function L(text, k, extra) {
  return Object.assign({ text: String(text) }, k ? { k } : {}, extra || {});
}

function lines() {
  map = [];
  if (!sections.length) { map.push(null); return [L('liste vide', 'dim')]; }
  const l = [];
  if (view === 'index') {
    map.push(null); l.push(L('Zones', 'h', { right: String(sections.length) }));
    const pages = Math.ceil(sections.length / PAGE);
    if (page >= pages) page = 0;
    const slice = sections.slice(page * PAGE, page * PAGE + PAGE);
    for (const s of slice) {
      map.push({ t: 'goto', s });
      l.push(L(s.name, 'nav', { right: doneCount(s) + '/' + s.items.length }));
    }
    if (pages > 1) {
      map.push({ t: 'pg', d: -1 }); l.push(L('‹ ' + (page + 1) + '/' + pages, 'nav'));
      map.push({ t: 'pg', d: 1 });  l.push(L('suite ›', 'nav'));
    }
    map.push({ t: 'back' }); l.push(L('← retour', 'nav'));
    return l;
  }
  const s = sections[cur];
  const pages = Math.ceil(s.items.length / PAGE);
  if (page >= pages) page = 0;
  map.push(null); l.push(L(s.name, 'h', { right: doneCount(s) + '/' + s.items.length }));
  map.push(null); l.push(L('', 'bar',
    { v: s.items.length ? doneCount(s) / s.items.length : 0 }));
  const slice = s.items.slice(page * PAGE, page * PAGE + PAGE);
  for (const it of slice) {
    map.push({ t: 'item', it });
    const lm = it.name.match(/^(.*?)\s*·\s*(\d+)\s*$/);
    const nm = lm ? lm[1] : it.name;
    const g = GFX[nm];
    l.push(L(nm, null,
      Object.assign({ done: it.done },
        lm ? { right: 'niv ' + lm[2] } : {},
        g ? { img: IMGURL + g + '.png' } : {})));
  }
  if (pages > 1) {
    map.push({ t: 'pg', d: -1 }); l.push(L('‹ ' + (page + 1) + '/' + pages, 'nav'));
    map.push({ t: 'pg', d: 1 });  l.push(L('suite ›', 'nav'));
  }
  map.push({ t: 'sec', d: -1 }); l.push(L('‹ ' + sections[(cur + sections.length - 1) % sections.length].name, 'nav'));
  map.push({ t: 'sec', d: 1 });  l.push(L(sections[(cur + 1) % sections.length].name + ' ›', 'nav'));
  map.push({ t: 'index' }); l.push(L('toutes les zones', 'nav'));
  const total = sections.reduce((a, x) => a + x.items.length, 0);
  const done = sections.reduce((a, x) => a + doneCount(x), 0);
  map.push(null); l.push(L(done + '/' + total + ' cochés', 'bar',
    { v: total ? done / total : 0 }));
  map.push({ t: 'reset' }); l.push(L('tout décocher', 'nav', { color: '#E08A7A' }));
  return l;
}

function render(slot) {
  tm.overlay(slot, {
    id: 'archis', visible: true, title: 'archis',
    compact: true, pos: 'tr', color: '#E2843A',
    lines: lines(),
  });
}

function renderAll() {
  for (const m of tm.getMirrors().data || [])
    if (m.connected) render(m.slot);
}

tm.on('overlay.line', d => {
  if (d.id !== 'archis') return;
  const a = map[d.index | 0];
  if (!a) return;
  if (a.t === 'item') {
    a.it.done = !a.it.done;
    tm.log((a.it.done ? '☑ ' : '☐ ') + a.it.name);
    save();
  } else if (a.t === 'sec') {
    cur = (cur + a.d + sections.length) % sections.length;
    page = 0;
  } else if (a.t === 'pg') {
    const s = view === 'index' ? sections.length : sections[cur].items.length;
    const pages = Math.ceil(s / PAGE);
    page = (page + a.d + pages) % pages;
  } else if (a.t === 'index') {
    view = 'index'; page = Math.floor(cur / PAGE);
  } else if (a.t === 'back') {
    view = 'zone';
  } else if (a.t === 'goto') {
    cur = sections.indexOf(a.s); view = 'zone'; page = 0;
  } else if (a.t === 'reset') {
    for (const s of sections) for (const it of s.items) it.done = false;
    tm.log('archis remise à zéro');
    save();
  }
  renderAll();
});

tm.on('mirror.connected', d => { if (d.slot) render(d.slot); });
tm.setInterval(() => { refresh(); renderAll(); }, RELOAD_MS);

refresh();
tm.log('archis actif — ' + sections.reduce((a, s) => a + s.items.length, 0) + ' monstres en ' + sections.length + ' zones');
renderAll();
