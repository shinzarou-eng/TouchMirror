# Fonctionnalités

## Mirroring

- **Résolution native** du téléphone, jusqu'à 120 fps selon l'appareil
- Codecs **H.264, H.265 (HEVC), AV1** — sélection dans les Réglages
- Décodage **FFmpeg basse latence** : dernière frame prioritaire, pas de file d'attente
- Audio du téléphone vers le PC (~400 ms de délai)

## Contrôle

| Action | Résultat |
|---|---|
| Clic souris | Tactile |
| Glisser | Swipe |
| Molette | Scroll |
| `Ctrl` + molette | Pinch-to-zoom (map Dofus) |
| Clavier | Texte envoyé comme un clavier Bluetooth |
| `Ctrl`+`V` | Colle le presse-papiers PC → tel — la copie sur le tel arrive sur le PC |
| Boutons ← ⌂ ▢ | Navigation Android |

## Capture et stream

- **Capture PNG** : bouton appareil photo → enregistré dans `Images\TouchMirror`
- **Enregistrement MP4** : bouton record → remux sans ré-encodage, fichier directement lisible, dans `Vidéos\TouchMirror`
- **Mode capture** : fenêtre propre sans interface, à capturer dans **OBS**

## Écran éteint

Toggle dans les Réglages : l'écran physique du téléphone passe en **luminosité 0** (visuellement noir sur AMOLED = économie réelle de batterie) pendant que le miroir continue de fonctionner. La luminosité d'origine est restaurée à la désactivation ou à la déconnexion.

> Pourquoi pas un vrai « power off » ? Sur la plupart des téléphones, éteindre le panneau arrête le compositor → le miroir recevrait des frames noires. La luminosité 0 est la bonne solution.

## Lancement Dofus

Bouton **Dofus** dans la toolbar : lance (ou ramène au premier plan) le jeu officiel `com.ankama.dofustouch` sur le téléphone.

## Aide intégrée

Icône tout à gauche de la toolbar : panneau **WebView2** avec les sites utiles Dofus Touch : forum Ankama, encyclopédie, Atlas, **Almanax officiel** et **guides papycha** — consultables sans quitter le jeu.

## Plein écran

`F11` ou bouton dédié : la fenêtre passe en plein écran, une **barre de contrôle apparaît au survol du bord haut** (retour/accueil/récents/quitter).

## Mises à jour

Vérification automatique au démarrage via l'API GitHub Releases — bandeau discret quand une nouvelle version existe.
