<div align="center">

### 🌐 [English version — click here](README.md)

</div>

---

<div align="center">

<img src="assets/mascot.png" width="120" alt="Mascotte TouchMirror">

# TouchMirror

**Mirroring Android natif pour Windows — pensé pour Dofus Touch.**

Gratuit, open source, sans compte, sans pub.

[![Version](https://img.shields.io/badge/version-0.4.0-D9A94E?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
[![Plateforme](https://img.shields.io/badge/plateforme-Windows%2010%2F11-0078D4?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![Licence](https://img.shields.io/badge/licence-MIT-green?style=flat-square)](LICENSE)
[![Dofus Touch](https://img.shields.io/badge/optimis%C3%A9%20pour-Dofus%20Touch-D9A94E?style=flat-square)](https://www.dofus-touch.com)
[![Discord](https://img.shields.io/badge/Discord-rejoins--nous-5865F2?style=flat-square)](https://discord.gg/DBJ9kNCdX)

<img src="docs/screenshot.png" width="780" alt="TouchMirror — Dofus Touch en cours de jeu">

**[Télécharger la dernière version](https://github.com/shinzarou-eng/TouchMirror/releases/latest)** · [Discord](https://discord.gg/DBJ9kNCdX) · [Documentation](docs/wiki/Home.md) · [Roadmap](ROADMAP.md) · [Signaler un bug](https://github.com/shinzarou-eng/TouchMirror/issues) · [Proposer une idée](https://github.com/shinzarou-eng/TouchMirror/issues/new)

</div>

---

## Pourquoi TouchMirror ?

TouchMirror est une application **Windows native** qui affiche et contrôle ton téléphone Android depuis le PC : tu branches, tu cliques, tu joues. Interface sombre soignée, faible latence, plusieurs téléphones dans **une seule fenêtre** — et un moteur de mirroring maison, fork de scrcpy-server, dont les sources sont dans `engine/`.

Gratuit, open source et communautaire : chaque ligne est lisible, les idées remontent sur Discord, et l'app n'appartient à personne d'autre qu'à ceux qui l'utilisent.

## Fonctionnalités

| | |
|---|---|
| **Mirroring HD** | Résolution native du téléphone, 60/90/120 fps, codecs H.264, H.265 et AV1 |
| **Faible latence** | Décodage FFmpeg basse latence, dernière frame prioritaire, audio ~400 ms, sockets optimisés |
| **Raccourcis plaqués** | Pose un repère sur un sort à l'écran, assigne une touche — 1 frappe = 1 tap à cet endroit. Style (pastille, cercle, minimal), opacité et taille réglables, persisté par appareil. Manuel pur : pas de répétition, pas de macro |
| **Affichage virtuel** | Dofus tourne sur un écran virtuel dédié — le téléphone physique reste libre. Presets paysage, portrait et **tablette** (8″/10″ : les apps passent en UI tablette) |
| **Multicompte** | Plusieurs téléphones dans une seule fenêtre — un compte par téléphone |
| **Espaces de travail** | « Solo », « Duo », « Stream » — appareils, ordre, miroir actif et réglages par appareil restaurés en un clic ; `Ctrl`+`Maj`+`1-9` pour basculer |
| **Détection instantanée** | `adb track-devices` événementiel — le téléphone apparaît dès le branchement, sans polling |
| **Presets qualité** | Performance / Équilibré / Qualité+ / Maximal — résolution, fps et bitrate appliqués en un clic |
| **Économie de ressources** | Les miroirs inactifs arrêtent de décoder la vidéo (CPU/GPU économisés) — l'enregistrement continue en arrière-plan |
| **Diagnostics intégrés** | Verdicts USB (câble douteux, autorisation, liaison instable) et diag réseau affichés directement dans l'app |
| **Pare-feu automatique** | Règles entrantes vérifiées et créées au premier lancement — une seule invite UAC |
| **Personnalisation** | Renomme chaque téléphone (tuiles + hub) et choisis sa couleur d'accent — clic droit sur l'appareil |
| **USB & WiFi** | Bascule en sans-fil en un clic, puis débranche le câble — le flux continue |
| **Souris = tactile** | Clic, glisser, molette = scroll, `Ctrl`+molette = pinch-to-zoom (zoom de la map) |
| **Clavier** | Le texte tapé arrive sur le téléphone comme un clavier Bluetooth |
| **Presse-papiers** | Bidirectionnel — `Ctrl`+`V` colle sur le tel, copier sur le tel arrive sur le PC |
| **Enregistrement MP4** | Remux sans ré-encodage — fichiers directement lisibles et uploadables |
| **Captures PNG** | Un clic, enregistrées dans `Images\TouchMirror` |
| **Écran éteint** | L'écran physique du téléphone passe au noir pendant le mirroring — économise la batterie et l'AMOLED |
| **Aide intégrée** | Forum, encyclopédie et DofusDB dans un panneau navigateur sans quitter le jeu |
| **Plein écran** | `F11` ou bouton dédié, barre de contrôle au survol du bord haut |
| **Mode capture** | Fenêtre propre pour OBS — idéal pour streamer |
| **API locale** | HTTP + SSE sur localhost avec token — pilotage Stream Deck, OBS, scripts. Aucun endpoint ne peut injecter d'input sur le téléphone |
| **Plugins** | Moteur JavaScript embarqué (sandbox) — manifest `plugin.json`, plugins officiels vérifiés par hash |
| **Mises à jour** | L'app détecte les nouvelles releases GitHub au démarrage |
| **iPhone / AirPlay** | *Bientôt disponible* — le mirroring iOS est en cours de finalisation |

## Comparatif

Chaque outil a ses forces — voici où TouchMirror se situe :

| Fonctionnalité | TouchMirror | scrcpy | Vysor | Walky |
|---|:---:|:---:|:---:|:---:|
| Interface graphique native Windows | ✅ | — (CLI) | ✅ | ✅ |
| Multi-téléphones dans une fenêtre | ✅ | — | — | ✅ |
| Raccourcis plaqués (touche → tap à l'écran) | ✅ | — | — | — |
| Affichage virtuel / mode tablette | ✅ | ✅ (option) | — | ✅ |
| Multi-compte sur un seul téléphone | *Bientôt* | — | — | ✅ |
| iPhone / iOS | *Bientôt* | — | ✅ | ✅ |
| Presets qualité, diagnostics intégrés | ✅ | — | — | ✅ |
| Enregistrement MP4 intégré | ✅ | ✅ | — | — |
| API locale + plugins sandbox | ✅ | — | — | — |
| Multilingue | ✅ FR · EN | — | ✅ | — |
| Open source | ✅ MIT | ✅ Apache-2.0 | — | — |

*État au 16/09/2026 — n'hésite pas à corriger via une issue si quelque chose a changé.*

## Installation

### Version prête à l'emploi (recommandé)

1. Télécharge **`TouchMirror-win-x64.zip`** depuis la [dernière release](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
2. Dézippe où tu veux, lance **`TouchMirror.exe`**
3. C'est tout — **adb est embarqué**, le runtime .NET est inclus et FFmpeg s'extrait au premier lancement

### Configuration du téléphone

1. **Options développeur → Débogage USB** activé
2. Branche en USB, accepte l'autorisation sur le téléphone
3. Clique **Connecter** — le miroir s'affiche, tu joues depuis le PC

### Mode WiFi

Menu **⋯ → Activer le WiFi** pendant que le câble est branché → l'appareil bascule en TCP/IP et reconnecte automatiquement. Débranche le câble, le flux continue. *(PC et téléphone sur le même réseau ; à refaire après un redémarrage du téléphone — limitation Android.)*

## Raccourcis plaqués

Une touche clavier qui tape à un endroit précis de l'écran — pour les sorts, les items, les boutons :

1. Active **⌨ Raccourcis** dans la barre d'outils
2. Clique sur un sort à l'écran → un repère apparaît
3. Appuie sur une touche (`1`, `A`, `F1`…) → le repère prend le nom de la touche
4. Quitte le mode édition → chaque frappe envoie **un** tap à cet endroit

En mode édition : glisser pour déplacer, clic droit pour supprimer, `Échap` pour quitter. Style, opacité et taille se règlent dans le panneau en haut de la vidéo. Les positions sont relatives à l'image — elles survivent au redimensionnement, à la rotation et à l'affichage virtuel.

**Strictement manuel :** 1 frappe = 1 tap, maintenir la touche ne répète rien. C'est un raccourci ergonomique, pas une automatisation.

## Affichage virtuel

Réglages → **VIDÉO → Écran** : au lieu de l'écran physique, le miroir affiche un écran virtuel Android dédié (Android 10+) :

- **Dofus tourne dans le virtuel** — tu peux utiliser ton téléphone normalement en parallèle
- Presets **paysage** (1080p/900p/720p), **portrait** (1080×1920) et **tablette** (1920×1200, 2560×1600)
- Les presets tablette baissent la densité → les apps passent en interface tablette (HUD plus aéré)

## Multicompte

Autorisé par Ankama : autant d'appareils physiques que tu veux, un compte par téléphone.

1. Connecte le premier téléphone
2. Branche le deuxième → il apparaît dans la liste → **Connecter**
3. Clique une miniature pour la cibler — seule la tuile active reçoit les actions et sort le son ; les inactives arrêtent de décoder pour économiser le CPU
4. Au clavier : `Ctrl`+`Tab` pour cycler, `Ctrl`+`1…9` pour viser directement

## Raccourcis clavier

| Touche | Action |
|---|---|
| `F11` | Plein écran |
| `Ctrl` + `Tab` | Miroir suivant / précédent (`+Shift`) |
| `Ctrl` + `1…9` | Activer directement le miroir N |
| `Ctrl` + molette | Zoom (pinch) |
| Touche assignée | Tap au repère plaqué (raccourcis écran) |
| Souris sur la vidéo | Tactile direct — aucun raccourci caché qui interfère avec le jeu |

## API locale (optionnelle)

Réglages → **API locale** : expose `http://127.0.0.1:<port>` protégé par token (`Authorization: Bearer <token>` ou `?token=`).

| Endpoint | Effet |
|---|---|
| `GET /api/status` · `/api/mirrors` · `/api/devices` | État de l'app, tuiles et appareils |
| `POST /api/mirrors/{n}/activate` | Changer de miroir (= `Ctrl`+N) |
| `POST /api/mirrors/{n}/record` · `/screenshot` · `/disconnect` | Actions par tuile |
| `POST /api/devices/{serial}/connect` | Connecter un appareil |
| `GET /api/events` | Flux SSE temps réel (connexions, tuile active, REC…) |

**Conformité :** l'API pilote l'app, jamais le jeu — aucun endpoint ne produit d'input sur le téléphone.

### Plugins

Panneau **🧩 Plugins** dans la barre latérale : un plugin = un dossier `plugins/<nom>/` avec un manifest `plugin.json` (nom, version, description) et un `plugin.js`. Le code tourne dans un **moteur JavaScript embarqué et sandboxé** — pas de process externe, pas de shell : le plugin ne voit que l'objet `tm`.

```text
plugins/
  reconnect/
    plugin.json    # métadonnées (nom, version, auteur…)
    plugin.js      # logique, via l'API tm.*
```

**API exposée** (`tm.*`, control-plane uniquement) :

```javascript
await tm.getDevices();           // appareils découverts
await tm.getMirrors();           // miroirs et leurs slots
await tm.connect("RFGL22M2JQM"); // connecter un appareil
await tm.activate(0);            // slot 0 en grand miroir
await tm.screenshot(0);          // capture
tm.on("devices", e => …);        // événements temps réel
tm.setInterval(fn, ms); tm.setTimeout(fn, ms);
tm.log("message");               // → journal de l'app
```

Aucun accès au système de fichiers, au réseau ou aux process depuis le sandbox — et comme l'API locale, **rien ne peut injecter d'input vers le téléphone**. Le moteur est borné (mémoire, récursion, timers, 30 appels/s max) et chaque action d'un plugin est tracée dans le journal.

Un plugin est du code : n'installe que ce que tu lis ou qui vient de nous. Les plugins officiels portent un badge bouclier vert (hash SHA-256 vérifié) — tout autre plugin demande une confirmation avant activation, et toute modification d'un plugin déjà approuvé redemande ton accord. Les plugins pilotent l'app — jamais le jeu.

Inclus : **Reconnect** — restaure un miroir dont la session a lâché (câble, WiFi, plantage), jamais après une déconnexion volontaire. La farm se répare seule, sans rien lancer sur le téléphone.

## Build depuis les sources

```bash
dotnet build src/AndroidMirror/TouchMirror.csproj
```

Prérequis : **.NET 10 SDK** uniquement — adb, le moteur TouchMirror (`assets/touchmirror-engine.jar`) et les DLLs FFmpeg sont embarqués dans le repo. Les sources du moteur (fork de scrcpy-server, Apache-2.0) sont dans `engine/` — rebuild via `engine/build-engine.ps1`.

## Stack technique

WPF / .NET 10 · WPF-UI · moteur TouchMirror (fork scrcpy-server) · FFmpeg (décodage + remux MP4) · NAudio · WebView2

## Conformité Ankama

TouchMirror affiche et contrôle le **jeu officiel** qui tourne sur ton **vrai téléphone** — pas d'émulateur, pas de client modifié, pas de macro ni d'automatisation. Chaque action correspond à un geste humain : les raccourcis plaqués envoient un tap par frappe, rien de plus. C'est le cas d'usage que le support Ankama a confirmé comme autorisé (voir la [FAQ officielle](https://support.ankama.com/hc/fr/articles/26840828168209)).

**TouchMirror ne proposera jamais de système d'automatisation, de bot ou de macro** — ni aujourd'hui, ni dans une version future. L'API locale et les plugins pilotent l'application (miroir, capture, enregistrement, reconnexion), jamais les actions en jeu : aucune route API n'injecte de tactile, clavier, texte ou presse-papiers vers Android. Voir le [hors périmètre de la roadmap](ROADMAP.md#hors-périmètre).

## Roadmap

Prochaines étapes : finalisation du mirroring iPhone (AirPlay + contrôle), sortie audio par appareil, et polish continu de l'expérience multi-téléphones.

**[Consulter la roadmap](ROADMAP.md)** — priorités, critères de validation et pistes à l'étude. Les éléments prévus ne sont pas encore des fonctionnalités disponibles.

## Communauté

Questions, retours, entraide multicompte → **[Discord](https://discord.gg/DBJ9kNCdX)** · Bugs et idées → [issues GitHub](https://github.com/shinzarou-eng/TouchMirror/issues)

## Licence

[MIT](LICENSE) — libre d'utilisation, de modification et de redistribution. Le moteur dans `engine/` est sous Apache-2.0 (fork de scrcpy-server).

---

<div align="center">
Fait avec ❤️ pour la communauté Dofus Touch
</div>
