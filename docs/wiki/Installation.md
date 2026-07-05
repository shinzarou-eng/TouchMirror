# Installation

## Prérequis

| Élément | Requis |
|---|---|
| OS | Windows 10 ou 11 (x64) |
| Téléphone | Android 8+ avec débogage USB |
| Câble | USB data (ou WiFi après le premier appairage) |
| À installer | **Rien** — adb, .NET et FFmpeg sont embarqués |

## Version prête à l'emploi (recommandé)

1. Télécharge **`TouchMirror-win-x64.zip`** depuis la [dernière release](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
2. Dézippe le dossier où tu veux (Bureau, Documents, `C:\Apps`…)
3. Lance **`TouchMirror.exe`**

Au premier démarrage :
- les binaires **adb** et **FFmpeg** s'extraient automatiquement,
- l'app scanne les appareils branchés et affiche le hub.

> Windows peut afficher un avertissement SmartScreen (« application non reconnue ») : c'est normal pour un binaire neuf et non signé. Clique **Informations complémentaires → Exécuter quand même**. Le code source est entièrement public.

## Mise à jour

L'app vérifie les nouvelles versions au démarrage. Quand une release est disponible, un bandeau apparaît en haut avec un bouton **Télécharger** : télécharge le nouveau zip et remplace ton dossier.

## Désinstallation

Supprime le dossier — l'app ne touche ni au registre ni au système. Les fichiers générés restent dans :

- `%LOCALAPPDATA%\TouchMirror` (adb et FFmpeg extraits, logs)
- `Images\TouchMirror` (captures PNG)
- `Vidéos\TouchMirror` (enregistrements MP4)
