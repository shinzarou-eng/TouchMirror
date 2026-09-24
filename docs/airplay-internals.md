# AirPlay maison, de A à Z

Comment TouchMirror a reconstruit un récepteur AirPlay complet en C# — sans
bibliothèque de protocole externe. Document technique pour les curieux, les
contributeurs, et quiconque déboguera la prochaine cassure iOS.

L'état courant de la fonctionnalité (ce qui marche, ce qui manque) vit dans
[airplay.md](airplay.md) ; ce fichier raconte le *comment*.

## En version simple

Ton iPhone croit parler à une Apple TV. Pour ça, TouchMirror a dû apprendre à :

- **Se présenter** : annoncer sur le réseau « je suis un récepteur AirPlay »
  avec les bons papiers d'identité — sinon l'iPhone ne le voit même pas dans
  Recopie de l'écran.
- **Faire connaissance** : à la première connexion, un code à 4 chiffres
  s'affiche sur le PC et se tape sur l'iPhone — comme appairer des écouteurs
  Bluetooth. Ensuite les deux se reconnaissent tout seuls.
- **Parler chiffré** : tout l'échange est verrouillé par un système de clés
  qu'Apple ne documente pas — réécrit à la main, environ 3 200 lignes.
- **Déchiffrer la vidéo** : le flux arrive protégé par FairPlay ; une petite
  pièce externe isolée (sous sa propre licence) extrait la clé.
- **Afficher et entendre** : la vidéo passe dans le moteur de rendu existant,
  le son dans un lecteur audio, avec une horloge commune pour la synchro.
- **Contrôler** : AirPlay ne renvoie jamais les doigts — donc le PC se
  présente en clavier/pointeur Bluetooth pour piloter l'iPhone.

Le reste du document descend dans le détail technique.

## Pourquoi from scratch

Il n'existe pas de bibliothèque AirPlay receveur en .NET. Les références
open-source sont en C (UxPlay, shairport-sync) ou en C++ — les embarquer aurait
signifié un binaire tiers opaque, une licence à gérer (GPL pour UxPlay) et une
intégration bancale dans une app WPF. Le choix : réécrire le protocole, ~3 200
lignes dans `src/AndroidMirror/AirPlay/`, plus quelques services.

Ce que ça coûte : chaque subtilité non documentée d'iOS se découvre à
l'exécution. Ce que ça rapporte : zéro dépendance de protocole, une licence MIT
propre, et une chaîne de bout en bout déboguable dans le même processus que
l'UI.

## Carte des composants

| Fichier | Rôle |
|---|---|
| `RtspServer.cs` | Serveur TCP/HTTP-RTSP brut, deux instances : 7001 (miroir) et 5001 (audio) |
| `AirPlaySession.cs` | Machine à états : routes `OPTIONS`, `GET /info`, `POST /pair-*`, `/fp-setup`, `ANNOUNCE`, `SETUP`, `RECORD`, `TEARDOWN`… |
| `Pairing.cs` | Appairage HAP complet + stores persistants (identité receveur, clients appairés) |
| `Srp6a.cs` / `Srp6aHap.cs` | SRP-6a pour le pairing par PIN |
| `Curve25519.cs` | Ed25519 + X25519 implémentés à la main |
| `Tlv8.cs` | Codec TLV8 (format des échanges d'appairage) |
| `PlistCodec.cs` | plist binaire/XML Apple, lu et écrit |
| `ChannelCipher.cs`, `AesCtr.cs`, `Gcm.cs` | Chiffrement du canal post-vérification |
| `FairPlay.cs`, `FairPlayDecrypt.cs` | Poignée de main `/fp-setup` et décryptage du flux |
| `MirrorStreamServer.cs` | Canal vidéo (stream type 110) : NAL → Annex-B → décodeur |
| `AirPlayAudioStream.cs` | Canal audio RTP/UDP : ALAC ou AAC-ELD → lecteur |
| `NtpTiming.cs` | Horloge de référence pour la synchro A/V |
| `Services/AirPlayService.cs` + `MdnsHost.cs` | Orchestration + annonce Bonjour |
| `Services/BleHidHost.cs` | Contrôle iOS : le PC se présente en clavier/pointeur BLE |

La seule brique externe : `assets/FairPlayHelper.exe` (GPL-3.0, dérivé d'UxPlay,
source dans `native/FairPlayHelper/`) — processus isolé invoqué en stdin/stdout
pour extraire la clé AES de l'`ekey` FairPlay. Jamais lié au binaire principal :
l'app reste MIT.

## La chaîne, étape par étape

### 1. Se faire voir — mDNS

L'iPhone découvre les récepteurs via Bonjour. `MdnsHost` publie deux services :
`_airplay._tcp:7001` (miroir) et `_raop._tcp:5001` (audio), avec les TXT records
qui comptent : `deviceid`, clé publique `pk`, `srcvers`, `protovers`, `flags`,
et le bitfield `features` — celui d'un récepteur tiers réel mesuré sur le
terrain, moins deux bits (PTP, ScreenMultiCodec) pour forcer iOS sur le chemin
implémenté. Sans ce profil crédible, « TouchMirror » n'apparaît même pas dans
Recopie de l'écran.

### 2. Parler — RTSP

AirPlay, c'est du RTSP-over-HTTP sur TCP brut. `RtspServer` accepte les
connexions, `AirPlaySession` route chaque requête. `GET /info` renvoie un plist
décrivant capacités et identité — écrit par `PlistCodec`, maison lui aussi
(plist binaire *et* XML, les deux formes qu'iOS envoie).

### 3. La confiance — appairage HAP

iOS ne stream pas vers un inconnu. Quatre phases, toutes réimplémentées :

- **`POST /pair-pin-start`** : l'iPhone demande un appairage PIN → on génère un
  code à 4 chiffres, affiché dans l'app (`PinReady`), réponse 200.
- **`POST /pair-setup`** : SRP-6a prouve la connaissance du PIN sans jamais
  l'envoyer. En échange, chaque côté pose sa clé Ed25519 long terme (LTPK),
  échangée chiffrée — stockée dans `airplay-paired.json`.
- **`POST /pair-verify`** : aux connexions suivantes, échange éphémère X25519 +
  signatures Ed25519 → secret partagé qui chiffre tout le reste de la session
  (`ChannelCipher`, AES-CTR/HAP).
- **`/fp-setup`** : la poignée de main FairPlay dérive la clé AES du flux
  vidéo — l'`ekey` chiffré part au helper isolé, la clé revient.

Identité receveur persistante dans `airplay-identity.json` : une fois appairé,
l'iPhone reconnaît TouchMirror sans repasser par le PIN.

### 4. Le flux

Après `ANNOUNCE`/`SETUP`/`RECORD` :

- **Vidéo** : `MirrorStreamServer` reçoit le stream type 110 — deux variantes
  rencontrées, AES-CTR classique et canal data HAP, toutes deux gérées.
  avcC → SPS/PPS → Annex-B → décodeur H.264 → `IFrameSource` → la tuile
  `MirrorView` existante.
- **Audio** : `AirPlayAudioStream` écoute en RTP/UDP, déjitterise, décode ALAC
  ou AAC-ELD selon le `codecType` annoncé.
- **Timing** : `NtpTiming` répond aux requêtes d'horloge — base de la synchro
  A/V (la variante PTP d'iOS récents n'est volontairement pas annoncée).

### 5. Le contrôle — BLE HID

AirPlay transporte zéro entrée. Le contrôle iOS passe par un canal séparé :
`BleHidHost` fait du PC un périphérique Bluetooth clavier/pointeur — coordonnées
absolues 16 bits, 3 boutons, molette — recalé à chaque changement d'orientation
du flux vidéo.

## Les pièges réels rencontrés

Documentés parce qu'ils coûtent cher à redécouvrir :

- **Ordre des champs TLV** : `pair-verify` peut envoyer `Method` avant `State`.
  Un parser qui exige `State` en premier octet rejette un paquet parfaitement
  valide → déconnexion. Le TLV se parse par tags, jamais par position.
- **`pair-pin-start` manquant** : sans cette route, l'iPhone prend un 501 et
  abandonne avant même d'afficher le champ PIN.
- **Méthode SRP non nulle** : après `pair-pin-start`, le M1 de `pair-setup`
  peut porter un `method` ≠ 0 — le check strict cassait le flow PIN.
- **Canal data HAP** : la détection du mode de chiffrement repose sur une
  heuristique (sonde `len16` + essai de plusieurs secrets) — point fragile
  face à un flux inconnu.

## Tester sans iPhone

`tools/FakeAirPlayHost` rejoue la chaîne côté UI (named pipes → tuile iOS →
600 frames animées), et `tests/TouchMirror.Tests` couvre les codecs et la
machine d'état. Le vrai test reste un appareil physique : chaque génération
d'iOS a ses variantes.

## Ce que ce choix ne règle pas

Le protocole bouge avec iOS — chaque version peut casser un détail. La
stratégie de défense : logs verbeux sur la phase de handshake, diagnostics
intégrés (ports, pare-feu, profil réseau, annonce mDNS vérifiable), et
l'export de diagnostic assaini pour que les utilisateurs puissent envoyer un
rapport exploitable.
