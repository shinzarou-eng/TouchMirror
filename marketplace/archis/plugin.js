const LANG = tm.lang && tm.lang() === 'en' ? 'en' : 'fr';
const FILE = LANG === 'en' ? 'archis.en.txt' : 'archis.txt';
const RELOAD_MS = 5000;
const PAGE = 9;

const T = LANG === 'en' ? {
  hint: 'archis — full list in archis.en.txt, editable in notepad',
  empty: 'empty list',
  zones: 'Zones',
  next: 'next ›',
  back: '← back',
  all: 'all zones',
  lvl: 'lvl ',
  checked: ' checked',
  uncheck: 'uncheck all',
  reset: 'archis reset',
  active: 'archis active — ',
  monsters: ' monsters in ',
  zonesEnd: ' zones',
} : {
  hint: 'archis — liste complète dans archis.txt, éditable au bloc-notes',
  empty: 'liste vide',
  zones: 'Zones',
  next: 'suite ›',
  back: '← retour',
  all: 'toutes les zones',
  lvl: 'niv ',
  checked: ' cochés',
  uncheck: 'tout décocher',
  reset: 'archis remise à zéro',
  active: 'archis actif — ',
  monsters: ' monstres en ',
  zonesEnd: ' zones',
};

const DATA_FR = `# Avis de recherche
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
[ ] Kaenepris l'Amoureux · 201
[ ] Marude l'ensablé · 201
[ ] Onistérique le déchainé · 203`;

const DATA_EN = `# Wanted
[ ] Akornaddikt the Squirrel · 15
[ ] Stellia Blutzell · 50
[ ] Ogivol Scalarcin · 50
[ ] Brumen Tinctorias · 60
[ ] Marzwel the Goblin · 60
[ ] Aermyne 'Braco' Scalptaras · 70
[ ] Qil Bil · 70
[ ] Musha the Oni · 80
[ ] Flo Inghair · 90
[ ] Domoizelle · 120
[ ] Rok Gnorok · 120
[ ] Nomarow Transplent · 126
[ ] Zatoishwan · 150
[ ] Mastigator · 170
[ ] Peb'Houlud · 200

# Archis lvl 1-20
[ ] Arachnangel the Hopeful · 19
[ ] Arachnekros the Aggressive · 14
[ ] Spimushty the Smelly · 20
[ ] Krabaoly the Patient · 12
[ ] Goblimp the Bis Kit · 20

# Archis lvl 21-40 · 1/4
[ ] Abounteous the Generous · 35
[ ] Treeknidylus · 40
[ ] Treekniddioo the Needy · 38
[ ] Amlullabeye the Dreamer · 35
[ ] Arachma the Greek · 40
[ ] Bandirty the Messy · 22
[ ] Bandinamit the Explosive · 37
[ ] Blorko the Colourful · 37
[ ] Biblokajin the Bald · 24
[ ] Biblopopo the Organiser · 24
[ ] Billbiblop the Great · 24
[ ] Bibloponey the Entertainer · 24
[ ] Boombora the Dangerous · 39
[ ] Mushdrill the Piercer · 25
[ ] Bakeraider the Tomb · 35
[ ] Bworkoder the Mazter · 39

# Archis lvl 21-40 · 2/4
[ ] Chafaldrag the Charming · 25
[ ] Chaferanho the Essential · 32
[ ] Chafred the Fish · 33
[ ] Chaferotix the Sixtininth · 40
[ ] Chaferuption the Volcanic · 40
[ ] Chafermented the Drinker · 40
[ ] Matmushmush the Flasher · 24
[ ] Spimushuaia the Traveller · 29
[ ] Speedmush the Racer · 30
[ ] Spimushtache the Hairy · 32
[ ] Karnyona the Rider · 27
[ ] Codemonic the Mean · 35
[ ] Crowmanion the Primitive · 24
[ ] Crackrodilrock the Helltune · 40
[ ] Lupisnockio the Woodwolf · 35
[ ] Snowhitisha the Pure · 35

# Archis lvl 21-40 · 3/4
[ ] Crabaramis the One · 30
[ ] Crabathos the For · 30
[ ] Craborthos the All · 30
[ ] Crabartanian the Allforone · 30
[ ] Ginsync the Hyperactive · 35
[ ] Kirevampiro the Wrestler · 35
[ ] Misskokoko the Channel · 40
[ ] Koleraspootin the Anesthesialogist · 25
[ ] Kwoanium the Smart · 26
[ ] Larvadelaide the Ozie · 30
[ ] Larvalencia the Orange · 32
[ ] Larvalaska the Cold · 29
[ ] Lert Macraken the Used Emo · 35
[ ] Lord Lacedhat the Vampiric · 37
[ ] Milivanilli the Mime · 24
[ ] Minoskittle the Coloured · 39

# Archis lvl 21-40 · 4/4
[ ] Nebuchadnezzar the Conqueror · 35
[ ] Niptuk the Plasticynic · 35
[ ] Kokonan the Talker · 40
[ ] Osurcus the Tamer · 35
[ ] Palmella the Hefty · 28
[ ] Palmoleaf the Greasy · 28
[ ] Naypalm the Herbivorous · 28
[ ] Palmpilot the Yuppie · 28

# Archis lvl 41-60 · 1/4
[ ] Arakula the Carpature · 51
[ ] Barbrosskam the Chief · 52
[ ] Blopal the Precious · 50
[ ] Blopium the Delirious · 50
[ ] Blorchid the Gorgeous · 50
[ ] Blopulent the Pretentious · 50
[ ] Gobbach the Contrapuntaler · 50
[ ] Gobballad the Romantic · 51
[ ] Gobbalky the Stubborn · 52
[ ] Gobballyhoo the Noisy · 53
[ ] Buzzby the Social · 58
[ ] Bworak the Bohemian · 44
[ ] Ganon the Dwarf · 51
[ ] Pygknightlion the Lousy · 57
[ ] Shamassel the Off · 50
[ ] Pigoblet the Useful · 58

# Archis lvl 41-60 · 2/4
[ ] Kojaklator the Lollipoper · 48
[ ] Jiminicrackler the Conscious · 55
[ ] Cracklerod the Old · 59
[ ] Crackrockisree the Tiger · 50
[ ] Crackedral the Majestic · 50
[ ] Croccyx the Bummer · 42
[ ] Dreggump the Magnificent · 60
[ ] Dragotitis the Painful · 55
[ ] Dreggatón the Latino · 60
[ ] Dreggommomm the Chewer · 60
[ ] Drakokidoki the Volunteer · 60
[ ] Dragoolash the Stewed · 50
[ ] Dragamemnon the Deadtroyer · 60
[ ] Dreggonzola the Cheesy · 50
[ ] Drakween the Cross Dresser · 50
[ ] Dreggershween the Tinpanalley · 50

# Archis lvl 41-60 · 3/4
[ ] Polterghaisk the Stray Soul · 57
[ ] Aperobics the Dynamic · 57
[ ] Arepotair the Bespectacled · 57
[ ] Ghostabrava the Tourist · 57
[ ] Smitherz the Licker · 41
[ ] Gargoyla the Paranoiac · 41
[ ] Jellvis the King · 46
[ ] Jellyposukshion the Slim · 54
[ ] Jelleno the Chinny · 50
[ ] Gwabbit the Wunner · 52
[ ] Bignstrong the Quartermaster · 42
[ ] Kaniedoss the Giggling · 60
[ ] Kannemik the Skinny · 53
[ ] Kannimantha the Maneater · 53
[ ] Kannarrie the Reckless · 53
[ ] Kanniranda the Maniac · 53

# Archis lvl 41-60 · 4/4
[ ] Kidodo the Extinct · 53
[ ] Killua the Assassin · 54
[ ] Koaldmen the Grumpy · 60
[ ] Fung Ku the Master · 60
[ ] Moskoitus the Interruptor · 45
[ ] Hazwonball the Hickler · 51
[ ] Ouassup the Irritating · 44
[ ] Ougineemo the Lost · 42
[ ] Ougathard the Fortunate · 45

# Archis lvl 61-80 · 1/2
[ ] Treekonk the Stunned · 78
[ ] Treektamak the Loud · 74
[ ] Treekalack the Sad · 80
[ ] Crokdylann the Rebel · 61
[ ] Ezothbeitor the Neighbour · 79
[ ] Dokterwho the Tardisporter · 70
[ ] Dreggooniz the Adventurous · 70
[ ] Dreggoog the Downunder · 70
[ ] Dreggooliz the Macho · 70
[ ] Dreggrieg the Pianist · 70
[ ] Greetdoff the Gentleman · 80
[ ] Koaly the Fiddler · 80
[ ] Jackoalak the Ripper · 80
[ ] Snapoalak the Redhead · 80
[ ] Crackoalak the Blonde · 80
[ ] Popoalak the Mousibrown · 80

# Archis lvl 61-80 · 2/2
[ ] Mufavabeenz the Cannibal · 62
[ ] Fisheralf the Stewart · 80

# Archis lvl 81-100 · 1/2
[ ] Mopidyk the Mire · 87
[ ] Dragostino the Tiny · 84
[ ] Dregguantico the Trainer · 100
[ ] Dragoskovit the Barefoot · 84
[ ] Dragory the Violent · 100
[ ] Dragaustin the Power · 84
[ ] Dragospel the Black · 84
[ ] Drakoamax the Mad · 90
[ ] Pighatchoo the Electrical · 90
[ ] Koalarchitect the Balancing Force · 100
[ ] Ambushapens the Unlucky · 82
[ ] Calipzoth the Icy · 91
[ ] Chukoalak the Norris · 100
[ ] Zigzoth the Indecisive · 83
[ ] Mamankalak the Bibliomaniac · 90
[ ] Booty the Beast · 92

# Archis lvl 81-100 · 2/2
[ ] Ouassingiam the Tyrant · 85

# Archis lvl 101-120 · 1/2
[ ] Pikhoven the Deaf · 117
[ ] Floratio the Investigator · 109
[ ] Warazpacho the Cherrilla · 104
[ ] Don Quizothe the Stubborn · 112
[ ] Dragossiper the Nag · 110
[ ] Dragorse the Wild · 110
[ ] Dragangora the Softy · 102
[ ] Draigovsky the SocalledSwan · 110
[ ] Draghouse the Cynical · 110
[ ] Barkricrac the Unsteady · 111
[ ] Ryukualak the Bored · 108
[ ] Floramodovar the Stoned · 116
[ ] Minoknok the Visitor · 120
[ ] Koalsen the Similar · 104
[ ] Koaldman the Garish · 102
[ ] Koelloggs the Creator · 102

# Archis lvl 101-120 · 2/2
[ ] Khameleltux the Tolerant · 110
[ ] Koalakropolis the King of the Hill · 108
[ ] Salamaa the Henpeck · 110
[ ] Milikkybum the Informer · 110
[ ] Jackoalak the Moonwalker · 106
[ ] Supergwass the Free · 104

# Archis lvl 121-140
[ ] Treekness the Dark · 125
[ ] Arakazam the Psychic · 132
[ ] Arachiro the Calligrapher · 130
[ ] Gargamarak the Kidnapper · 129
[ ] Gargantua the Devourer · 131

# Archis lvl 141-160
[ ] Treekstalbal the Psychic · 150
[ ] Snailmetalika the Garagician · 146
[ ] Moops the Bubbleboy · 143

# Archis lvl 161-180
[ ] Nidsally the Mushtang · 167
[ ] Edvushmunch the Screamer · 173
[ ] Mushuliet the Catapulet · 179
[ ] Romush the Montecchi · 176

# Archis lvl 181-200
[ ] Mushketeer the Loyal · 182
[ ] Nanashi the Virtuoso · 200

# Archis lvl 200+
[ ] Kaenamoured the Lover · 201
[ ] Marude the Sandy · 201
[ ] Onisterical the Unleashed · 203`;

const DATA = LANG === 'en' ? DATA_EN : DATA_FR;

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

const GFX_FR = {
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
const GFX_EN = {
  "Abounteous the Generous": 86,
  "Treeknidylus": 345,
  "Treekonk the Stunned": 144,
  "Treektamak the Loud": 13,
  "Treekness the Dark": 601,
  "Treekalack the Sad": 346,
  "Treekniddioo the Needy": 13,
  "Treekstalbal the Psychic": 571,
  "Aermyne 'Braco' Scalptaras": 240,
  "Mastigator": 1047,
  "Amlullabeye the Dreamer": 85,
  "Arachma the Greek": 149,
  "Arachnangel the Hopeful": 261,
  "Arachnekros the Aggressive": 145,
  "Arakula the Carpature": 15,
  "Bandirty the Messy": 255,
  "Bandinamit the Explosive": 661,
  "Blorko the Colourful": 29,
  "Barbrosskam the Chief": 573,
  "Biblokajin the Bald": 168,
  "Biblopopo the Organiser": 167,
  "Billbiblop the Great": 166,
  "Bibloponey the Entertainer": 169,
  "Pikhoven the Deaf": 566,
  "Blopal the Precious": 165,
  "Blopium the Delirious": 163,
  "Blorchid the Gorgeous": 164,
  "Blopulent the Pretentious": 162,
  "Boombora the Dangerous": 124,
  "Mushdrill the Piercer": 59,
  "Gobbach the Contrapuntaler": 73,
  "Bakeraider the Tomb": 79,
  "Gobballad the Romantic": 3,
  "Gobbalky the Stubborn": 75,
  "Gobballyhoo the Noisy": 76,
  "Mopidyk the Mire": 583,
  "Floratio the Investigator": 597,
  "Brumen Tinctorias": 253,
  "Bworak the Bohemian": 489,
  "Bworkoder the Mazter": 10,
  "Ganon the Dwarf": 127,
  "Pygknightlion the Lousy": 191,
  "Chafaldrag the Charming": 57,
  "Chaferanho the Essential": 16,
  "Chafred the Fish": 193,
  "Chaferotix the Sixtininth": 179,
  "Chaferuption the Volcanic": 180,
  "Chafermented the Drinker": 178,
  "Matmushmush the Flasher": 21,
  "Spimushuaia the Traveller": 348,
  "Speedmush the Racer": 350,
  "Nidsally the Mushtang": 635,
  "Shamassel the Off": 241,
  "Edvushmunch the Screamer": 636,
  "Spimushtache the Hairy": 347,
  "Spimushty the Smelly": 349,
  "Mushketeer the Loyal": 637,
  "Mushuliet the Catapulet": 639,
  "Romush the Montecchi": 638,
  "Karnyona the Rider": 23,
  "Warazpacho the Cherrilla": 600,
  "Pigoblet the Useful": 68,
  "Codemonic the Mean": 91,
  "Kojaklator the Lollipoper": 560,
  "Crowmanion the Primitive": 170,
  "Jiminicrackler the Conscious": 4,
  "Cracklerod the Old": 181,
  "Crackrodilrock the Helltune": 562,
  "Crackrockisree the Tiger": 268,
  "Krabaoly the Patient": 24,
  "Crackedral the Majestic": 561,
  "Lupisnockio the Woodwolf": 673,
  "Crokdylann the Rebel": 152,
  "Croccyx the Bummer": 151,
  "Snowhitisha the Pure": 187,
  "Crabaramis the One": 587,
  "Crabathos the For": 586,
  "Craborthos the All": 585,
  "Crabartanian the Allforone": 584,
  "Ezothbeitor the Neighbour": 579,
  "Dokterwho the Tardisporter": 414,
  "Domoizelle": 1326,
  "Don Quizothe the Stubborn": 576,
  "Dregguantico the Trainer": 478,
  "Dragossiper the Nag": 490,
  "Dragoskovit the Barefoot": 678,
  "Dragotitis the Painful": 96,
  "Dragorse the Wild": 491,
  "Dreggatón the Latino": 41,
  "Dreggommomm the Chewer": 42,
  "Drakokidoki the Volunteer": 106,
  "Dragory the Violent": 482,
  "Dragangora the Softy": 480,
  "Dragamemnon the Deadtroyer": 31,
  "Draigovsky the SocalledSwan": 492,
  "Drakoamax the Mad": 427,
  "Draghouse the Cynical": 481,
  "Barkricrac the Unsteady": 599,
  "Polterghaisk the Stray Soul": 201,
  "Aperobics the Dynamic": 514,
  "Arepotair the Bespectacled": 203,
  "Ghostabrava the Tourist": 202,
  "Pighatchoo the Electrical": 334,
  "Ryukualak the Bored": 433,
  "Floramodovar the Stoned": 564,
  "Smitherz the Licker": 66,
  "Koalarchitect the Balancing Force": 426,
  "Akornaddikt the Squirrel": 206,
  "Ambushapens the Unlucky": 116,
  "Stellia Blutzell": 250,
  "Gargoyla the Paranoiac": 281,
  "Calipzoth the Icy": 577,
  "Jellvis the King": 17,
  "Jellyposukshion the Slim": 19,
  "Jelleno the Chinny": 18,
  "Minoknok the Visitor": 357,
  "Ginsync the Hyperactive": 94,
  "Greetdoff the Gentleman": 115,
  "Goblimp the Bis Kit": 99,
  "Gwabbit the Wunner": 50,
  "Chukoalak the Norris": 416,
  "Zigzoth the Indecisive": 578,
  "Peb'Houlud": 1541,
  "Kaniedoss the Giggling": 175,
  "Kannimantha the Maneater": 110,
  "Kannarrie the Reckless": 111,
  "Kannemik the Skinny": 113,
  "Kanniranda the Maniac": 112,
  "Snailmetalika the Garagician": 568,
  "Kidodo the Extinct": 593,
  "Killua the Assassin": 594,
  "Kirevampiro the Wrestler": 90,
  "Koaly the Fiddler": 419,
  "Koalsen the Similar": 422,
  "Koaldmen the Grumpy": 415,
  "Koaldman the Garish": 411,
  "Jackoalak the Ripper": 418,
  "Koelloggs the Creator": 424,
  "Snapoalak the Redhead": 420,
  "Crackoalak the Blonde": 417,
  "Popoalak the Mousibrown": 435,
  "Misskokoko the Channel": 109,
  "Koleraspootin the Anesthesialogist": 188,
  "Khameleltux the Tolerant": 467,
  "Kwoanium the Smart": 80,
  "Larvadelaide the Ozie": 2,
  "Larvalencia the Orange": 12,
  "Larvalaska the Cold": 1,
  "Lert Macraken the Used Emo": 93,
  "Mamankalak the Bibliomaniac": 421,
  "Salamaa the Henpeck": 466,
  "Marzwel the Goblin": 99,
  "Lord Lacedhat the Vampiric": 69,
  "Koalakropolis the King of the Hill": 423,
  "Moops the Bubbleboy": 570,
  "Milivanilli the Mime": 84,
  "Milikkybum the Informer": 465,
  "Minoskittle the Coloured": 65,
  "Jackoalak the Moonwalker": 425,
  "Moskoitus the Interruptor": 22,
  "Mufavabeenz the Cannibal": 592,
  "Booty the Beast": 52,
  "Musha the Oni": 141,
  "Hazwonball the Hickler": 125,
  "Nebuchadnezzar the Conqueror": 89,
  "Supergwass the Free": 598,
  "Niptuk the Plasticynic": 88,
  "Kokonan the Talker": 117,
  "Ogivol Scalarcin": 252,
  "Osurcus the Tamer": 87,
  "Ouassingiam the Tyrant": 279,
  "Ouassup the Irritating": 280,
  "Ougineemo the Lost": 279,
  "Ougathard the Fortunate": 697,
  "Nomarow Transplent": 249,
  "Palmella the Hefty": 588,
  "Palmoleaf the Greasy": 591,
  "Naypalm the Herbivorous": 590,
  "Palmpilot the Yuppie": 589,
  "Fisheralf the Stewart": 436,
  "Qil Bil": 266,
  "Rok Gnorok": 268,
  "Zatoishwan": 294
};

const GFX = LANG === 'en' ? GFX_EN : GFX_FR;

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
    tm.log(T.hint);
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
  if (!sections.length) { map.push(null); return [L(T.empty, 'dim')]; }
  const l = [];
  if (view === 'index') {
    map.push(null); l.push(L(T.zones, 'h', { right: String(sections.length) }));
    const pages = Math.ceil(sections.length / PAGE);
    if (page >= pages) page = 0;
    const slice = sections.slice(page * PAGE, page * PAGE + PAGE);
    for (const s of slice) {
      map.push({ t: 'goto', s });
      l.push(L(s.name, 'nav', { right: doneCount(s) + '/' + s.items.length }));
    }
    if (pages > 1) {
      map.push({ t: 'pg', d: -1 }); l.push(L('‹ ' + (page + 1) + '/' + pages, 'nav'));
      map.push({ t: 'pg', d: 1 });  l.push(L(T.next, 'nav'));
    }
    map.push({ t: 'back' }); l.push(L(T.back, 'nav'));
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
        lm ? { right: T.lvl + lm[2] } : {},
        g ? { img: IMGURL + g + '.png' } : {})));
  }
  if (pages > 1) {
    map.push({ t: 'pg', d: -1 }); l.push(L('‹ ' + (page + 1) + '/' + pages, 'nav'));
    map.push({ t: 'pg', d: 1 });  l.push(L(T.next, 'nav'));
  }
  map.push({ t: 'sec', d: -1 }); l.push(L('‹ ' + sections[(cur + sections.length - 1) % sections.length].name, 'nav'));
  map.push({ t: 'sec', d: 1 });  l.push(L(sections[(cur + 1) % sections.length].name + ' ›', 'nav'));
  map.push({ t: 'index' }); l.push(L(T.all, 'nav'));
  const total = sections.reduce((a, x) => a + x.items.length, 0);
  const done = sections.reduce((a, x) => a + doneCount(x), 0);
  map.push(null); l.push(L(done + '/' + total + T.checked, 'bar',
    { v: total ? done / total : 0 }));
  map.push({ t: 'reset' }); l.push(L(T.uncheck, 'nav', { color: '#E08A7A' }));
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
    tm.log(T.reset);
    save();
  }
  renderAll();
});

tm.on('mirror.connected', d => { if (d.slot) render(d.slot); });
tm.setInterval(() => { refresh(); renderAll(); }, RELOAD_MS);

refresh();
tm.log(T.active + sections.reduce((a, s) => a + s.items.length, 0) + T.monsters + sections.length + T.zonesEnd);
renderAll();
