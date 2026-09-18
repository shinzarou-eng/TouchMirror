# Récepteur AirPlay / iOS — état d'avancement

Notes techniques pour celles et ceux qui veulent comprendre, tester ou contribuer au mirroring iPhone.

## Vue d'ensemble

TouchMirror embarque un récepteur AirPlay **entièrement maison** (`src/AndroidMirror/AirPlay/`, ~2 700 lignes de C#). L'iPhone voit « TouchMirror » dans le Centre de contrôle → Recopie de l'écran, exactement comme une Apple TV. Aucun service tiers à installer, aucun binaire externe en tâche de fond.

Une seule exception assumée : `assets/FairPlayHelper.exe` (GPL-3.0 — source dans `native/FairPlayHelper/`, l'implémentation playfair est reprise du projet [UxPlay](https://github.com/FDH2/UxPlay)). C'est un processus standalone invoqué via stdin/stdout pour extraire la clé AES du flux vidéo — jamais lié à l'application, qui reste sous licence MIT. S'il est absent, la session négocie mais la vidéo reste chiffrée.

## Pipeline

```
iPhone ──mDNS──▶ AirPlayAdvertiser
                   _airplay._tcp:7001  (miroir)
                   _raop._tcp:5001     (audio)

iPhone ──RTSP──▶ RtspServer → AirPlaySession
                   pair-setup / pair-verify (SRP-6a + Ed25519)
                   → fp-setup → ANNOUNCE → SETUP → RECORD

iPhone ──TCP───▶ MirrorStreamServer  (canal data, type 110)
                   AES-CTR ou canal data HAP
                   → NAL → Annex-B → VideoDecoder H.264 → MirrorView
```

## Ce qui fonctionne

- **Annonce mDNS** des deux services avec l'empreinte `AppleTV3,2`, règles firewall créées automatiquement, diagnostics intégrés (conflits de ports, profil réseau public, règle bloquante).
- **Machine d'état RTSP complète** : `OPTIONS`, `GET /info`, `POST /pair-setup`, `/pair-verify`, `/fp-setup`, `/reverse`, `ANNOUNCE`, `SETUP`, `RECORD`, `GET/SET_PARAMETER`, `PAUSE`, `FLUSH`, `TEARDOWN`. Plist binaire lu/écrit par un codec maison.
- **Pairing HAP** : SRP-6a, Curve25519, TLV8, canal chiffré après vérification.
- **FairPlay** : `fp-setup` + extraction de la clé AES de l'`ekey` via le helper.
- **Canal vidéo** : stream type 110 accepté dans les deux modes rencontrés — AES-CTR classique et canal data HAP — avcC → SPS/PPS → Annex-B → décodeur H.264 → `IFrameSource` → la `MirrorView` existante.
- **Timing NTP** pour la synchronisation A/V.
- **UI** : tuile « iPhone (AirPlay) », overlay d'attente, mode lecture seule.
- **Contrôle iOS via BLE HID** : le PC s'annonce en périphérique Bluetooth clavier/pointeur — AirPlay étant display-only, le contrôle passe par ce canal séparé.
- **Testable sans iPhone** : `tools/FakeAirPlayHost` rejoue la poignée de main de bout en bout.

## Ce qui manque

| Manquant | Détail |
|---|---|
| **Timing PTP** | iOS moderne négocie `timingProtocol: "PTP"` dans SETUP ; seul NTP est implémenté. Suspect n°1 de l'affichage réel. |
| **Audio RAOP (type 96)** | Les ports sont ouverts et annoncés, aucun paquet n'est encore décodé. |
| **Canal data HAP** | La détection repose sur une heuristique (sonde `len16` + essai de plusieurs secrets) — fragile face aux flux d'un vrai appareil. |
| **`/reverse` (PTTH)** | Réponse `101` stub — pas de canal d'événements. |
| **Enregistrement iOS** | Désactivé (`ios.no_record`). |

## Où en est l'app

La carte « iPhone · AirPlay » de l'accueil indique **« Bientôt disponible »** : la poignée de main et l'échange de clés fonctionnent ; l'affichage vidéo sur un appareil iOS réel est encore en cours de validation.

Le test décisif : un iPhone physique sur le même Wi-Fi, logs `airplay rtsp` ouverts, et observer où la chaîne casse après `RECORD`.

## Limites assumées

- **Lecture seule par défaut** — AirPlay ne transporte pas d'entrées ; le contrôle BLE HID est opt-in et relève du même principe qu'un clavier/souris Bluetooth physique.
- Pas d'automatisation, pas d'injection d'entrées — la limite produit est la même que pour Android.
