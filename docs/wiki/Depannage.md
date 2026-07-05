# Dépannage

## Téléphone non détecté

1. Vérifie le **câble** (certains ne font que la charge) et essaie un autre port USB
2. Regarde le téléphone : la popup **« Autoriser le débogage USB »** attend peut-être
3. Clique le bouton **refresh** dans la toolbar
4. Options développeur → **Révoquer les autorisations de débogage USB**, rebranche, ré-autorise
5. Ferme tout autre outil adb (scrcpy, Vysor, Android Studio) — un seul serveur adb à la fois
6. Redémarre le téléphone

## L'appareil apparaît « Non prêt »

- Débogage USB désactivé ou autorisation révoquée → refais la [config](Configuration-du-telephone.md)
- Xiaomi : active **Débogage USB (paramètres de sécurité)** — sinon détection incomplète et clics bloqués

## Miroir noir / pas d'image

- L'écran du téléphone est **verrouillé ou éteint** → déverrouille-le ; le miroir se réveille
- Persistance : déconnecte/reconnecte dans la toolbar
- Certains écrans de sécurité Android (saisie de mot de passe bancaire, etc.) masquent volontairement la capture — normal

## Les clics ne passent pas

- **Xiaomi / MIUI** : *Débogage USB (paramètres de sécurité)* obligatoire pour les clics simulés
- Le téléphone affiche une protection d'écran (overlay) → ferme-la sur le téléphone

## Latence / saccades

| Cause | Solution |
|---|---|
| WiFi faible | Repasse en USB, ou rapproche-toi du routeur (5 GHz) |
| Résolution trop haute | Réglages → baisse la résolution max ou le fps |
| Codec | Essaie H.264 (le plus compatible) au lieu de H.265/AV1 |
| PC chargé | Ferme les apps gourmandes ; le décodage utilise le GPU si dispo |

## Pas de son

- Le son suit la **tuile active** — clique la tuile voulue
- Vérifie le volume Windows et le mixeur
- L'audio démarre parfois après quelques secondes de flux

## L'enregistrement MP4 est vide / corrompu

- Stoppe l'enregistrement **depuis le bouton** (pas en fermant l'app) — le fichier doit être finalisé
- Espace disque suffisant dans `Vidéos\TouchMirror`

## Le WiFi se déconnecte après reboot du téléphone

Normal — limitation Android. Rebranche en USB et refais **⋯ → Activer le WiFi**.

## Où sont les logs ?

`%LOCALAPPDATA%\TouchMirror\logs` — utile pour joindre à un [rapport de bug](https://github.com/shinzarou-eng/TouchMirror/issues).
