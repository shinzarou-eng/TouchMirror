<div align="center">

<img src="assets/mascot.png" width="140" alt="Mascotte TouchMirror">

# TouchMirror

**Le mirroring Android pensé pour Dofus Touch — gratuit, natif, multicompte.**

[![Version](https://img.shields.io/badge/version-0.1.0-E8A33D?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Dofus Touch](https://img.shields.io/badge/optimis%C3%A9%20pour-Dofus%20Touch-E8A33D?style=flat-square)](https://www.dofus-touch.com)

<img src="docs/screenshot.png" width="720" alt="TouchMirror — écran d'accueil">

[Télécharger la dernière version](https://github.com/shinzarou-eng/TouchMirror/releases/latest) · [Signaler un bug](https://github.com/shinzarou-eng/TouchMirror/issues)

</div>

---

## Pourquoi TouchMirror ?

scrcpy est puissant mais n'a pas d'interface. Vysor fait payer la HD. Aucun ne gère le multicompte proprement.

TouchMirror est une application **Windows native** qui affiche et contrôle ton téléphone Android depuis le PC : tu branches, tu cliques, tu joues. Interface Fluent 2, faible latence, et une grille multicompte unique — plusieurs téléphones dans une seule fenêtre.

## Fonctionnalités

| | |
|---|---|
| **Mirroring HD** | Jusqu'à la résolution native du téléphone, 60/90/120 fps, codecs H.264, H.265 et AV1 |
| **Faible latence** | Décodage FFmpeg basse latence, dernière frame prioritaire, audio à 400 ms, sockets optimisés |
| **Multicompte** | Plusieurs téléphones dans **une seule fenêtre**, en grille adaptative — clique une tuile pour la cibler |
| **USB et WiFi** | Bascule en sans-fil en un clic, puis débranche le câble — le flux continue |
| **Souris = tactile** | Clic, glisser, molette = scroll, Ctrl+molette = pinch-to-zoom (zoom de la map) |
| **Clavier** | Le texte tapé arrive sur le téléphone comme un clavier Bluetooth |
| **Enregistrement MP4** | Remux sans ré-encodage — fichiers directement lisibles et uploadables |
| **Captures PNG** | Un clic, enregistrées dans `Images\TouchMirror` |
| **Écran éteint** | Éteins l'écran physique du téléphone pendant le mirroring — économise batterie et AMOLED |
| **Aide intégrée** | Sites Dofus Touch (forum, encyclopédie, DofusDB) dans un panneau navigateur sans quitter le jeu |
| **Plein écran** | F11 ou bouton dédié, barre de contrôle au survol du bord haut |
| **Mode capture** | Fenêtre propre pour OBS — idéal pour les streamers |

## Installation

### Version prête à l'emploi (recommandé)

1. Télécharge **`TouchMirror-win-x64.zip`** depuis la [dernière release](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
2. Dézippe où tu veux, lance **`TouchMirror.exe`**
3. C'est tout — **adb est embarqué**, le runtime .NET est inclus et FFmpeg s'extrait tout seul au premier lancement

### Configuration du téléphone

1. **Options développeur → Débogage USB** activé
2. Branche en USB, accepte l'autorisation sur le téléphone
3. Clique **Connecter** → **Dofus**

### Mode WiFi

Menu **⋯ → Activer le WiFi** pendant que le câble est branché → l'appareil bascule en TCP/IP et reconnecte automatiquement. Débranche le câble, le flux continue. *(Nécessite que le PC et le téléphone soient sur le même réseau. À refaire après un redémarrage du téléphone — limitation Android.)*

## Multicompte

Autorisé par Ankama : autant d'appareils physiques que tu veux, un compte par téléphone.

1. Connecte le premier téléphone
2. Branche le deuxième → il apparaît dans la liste → **＋ Ajouter**
3. La grille s'adapte : 2 côte à côte, 3–4 en 2×2
4. Clique une tuile pour la cibler — seule la tuile active reçoit les actions et sort le son

## Raccourcis

| Touche | Action |
|---|---|
| `F11` | Plein écran |
| `Ctrl` + molette | Zoom (pinch) |
| Souris sur la vidéo | Tactile direct — aucun raccourci caché qui interfère avec le jeu |

## Build depuis les sources

```bash
dotnet build src/AndroidMirror/TouchMirror.csproj
```

Prérequis : .NET 10 SDK uniquement — adb, le serveur scrcpy et les DLLs FFmpeg (`assets/ffmpeg.zip`) sont embarqués dans le repo et s'installent tout seuls.

## Stack technique

WPF / .NET 10 · WPF-UI (Fluent 2) · serveur scrcpy · FFmpeg (décodage + remux MP4) · NAudio · WebView2

## Conformité Ankama

TouchMirror affiche et contrôle le **jeu officiel** qui tourne sur ton **vrai téléphone** — pas d'émulateur, pas de client modifié, pas de macro ni d'automatisation. Chaque action correspond à un geste humain. C'est le cas d'usage que le support Ankama a confirmé comme autorisé (voir la [FAQ officielle](https://support.ankama.com/hc/fr/articles/26840828168209)).

## Licence

[MIT](LICENSE) — libre d'utilisation, de modification et de redistribution.

---

<div align="center">
Fait avec ❤️ pour la communauté Dofus Touch
</div>
