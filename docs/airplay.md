# Récepteur AirPlay / iOS — état d'avancement

Notes techniques pour celles et ceux qui veulent comprendre, tester ou contribuer au mirroring iPhone.

## Vue d'ensemble

TouchMirror embarque un récepteur AirPlay écrit pour le projet (`src/AndroidMirror/AirPlay/`, en C#). L'iPhone voit « TouchMirror » dans le Centre de contrôle → Recopie de l'écran, exactement comme une Apple TV. Aucun service tiers à installer, aucun binaire externe en tâche de fond.

Une seule brique externe, assumée : `assets/FairPlayHelper.exe` (GPL-3.0 — source dans `native/FairPlayHelper/`, l'implémentation playfair est reprise du projet [UxPlay](https://github.com/FDH2/UxPlay)). C'est un processus standalone invoqué via stdin/stdout pour extraire la clé AES du flux vidéo — jamais lié à l'application, qui reste sous licence MIT. S'il est absent, la session négocie mais la vidéo reste chiffrée.

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

- **Annonce mDNS** des deux services avec un profil de récepteur tiers mesuré sur du vrai matériel (features 64-bit, `srcvers 377.40.00`, `flags 0x244`, `protovers 1.1`), règles firewall créées automatiquement, diagnostics intégrés (conflits de ports, profil réseau public, règle bloquante).
- **Machine d'état RTSP complète** : `OPTIONS`, `GET /info`, `POST /pair-setup`, `/pair-verify`, `/fp-setup`, `/reverse`, `ANNOUNCE`, `SETUP`, `RECORD`, `GET/SET_PARAMETER`, `PAUSE`, `FLUSH`, `TEARDOWN`. Plist binaire lu/écrit par un codec maison.
- **Pairing HAP** : SRP-6a, Curve25519, TLV8, canal chiffré après vérification.
- **FairPlay** : `fp-setup` + extraction de la clé AES de l'`ekey` via le helper.
- **Canal vidéo** : stream type 110 accepté dans les deux modes rencontrés — AES-CTR classique et canal data HAP — avcC → SPS/PPS → Annex-B → décodeur H.264 → `IFrameSource` → la `MirrorView` existante.
- **Timing NTP** pour la synchronisation A/V.
- **UI** : tuile « iPhone (AirPlay) », overlay d'attente, mode lecture seule.
- **Contrôle iOS via BLE HID** : le PC s'annonce en périphérique Bluetooth clavier/pointeur — AirPlay étant display-only, le contrôle passe par ce canal séparé.
- **Testable sans iPhone** : `tools/FakeAirPlayHost` rejoue la poignée de main de bout en bout.

## Références mesurées

Annonces relevées sur le réseau local de deux récepteurs fonctionnels (sept. 2026) :

| TXT `_airplay._tcp` | Récepteur TV tiers (intégrateur TV) | Apple TV 4K (`AppleTV14,1`) | TouchMirror |
|---|---|---|---|
| `features` | `0x7F8AD0,0x38BCF46` | `0x4A7FDFD5,0x3C177FDE` | `0x7F8AD0,0x38BC946` |
| `srcvers` | `377.40.00` | `980.77.2` | `377.40.00` |
| `flags` | `0x244` | `0x644` | `0x244` |
| `protovers` | `1.1` | `1.1` | `1.1` |
| port | `7000` | `7000` | `7001` |

Le masque TouchMirror reprend celui du récepteur tiers — profil éprouvé par les iPhones récents — en retirant deux bits : **PTP** (bit 41) et **ScreenMultiCodec** (bit 42), pour forcer iOS sur le chemin implémenté (timing NTP + flux H.264). Les deux récepteurs réels exposent aussi `PTPInfo`, `featuresEx` et `supportedFormats` dans `/info`, et l'Apple TV annonce `hasUDPMirroringSupport` — voie UDP encore non implémentée ici.

## Ce qui manque

| Manquant | Détail |
|---|---|
| **Timing PTP** | iOS moderne négocie `timingProtocol: "PTP"` dans SETUP ; seul NTP est implémenté. Le bit PTP n'est volontairement pas annoncé — à implémenter si un vrai appareil l'exige malgré tout. |
| **Audio RAOP (type 96)** | Les ports sont ouverts et annoncés, aucun paquet n'est encore décodé. |
| **Canal data HAP** | La détection repose sur une heuristique (sonde `len16` + essai de plusieurs secrets) — fragile face aux flux d'un vrai appareil. |
| **`/reverse` (PTTH)** | Réponse `101` stub — pas de canal d'événements. |
| **Enregistrement iOS** | Désactivé (`ios.no_record`). |

## Où en est l'app

La carte « iPhone · AirPlay » de l'accueil indique **« Bientôt disponible »** : la poignée de main et l'échange de clés fonctionnent ; l'affichage vidéo sur un appareil iOS réel est encore en cours de validation.

Le test décisif : un iPhone physique sur le même Wi-Fi, logs `airplay rtsp` ouverts, et observer où la chaîne casse après `RECORD`.

## Voie câble — protocole QuickTime (USB)

En parallèle du Wi-Fi, le protocole de recopie d'écran par câble (celui utilisé par QuickTime Player sur macOS) est en cours de portage dans `src/AndroidMirror/QuickTime/`. Intérêt : il esquive entièrement FairPlay et le timing PTP — le flux H.264 arrive en clair dans des `CMSampleBuffer`.

Couches portées depuis l'implémentation de référence MIT [quicktime_video_hack](https://github.com/danielpaulus/quicktime_video_hack) :

- **Framing** : préfixes longueur little-endian, familles `PING` / `SYNC` / `ASYN` / `RPLY`, sous-types `CWPA`, `AFMT`, `CVRP`, `CLOK`, `TIME`, `SKEW`, `OG`, `STOP`, `FEED`, `EAT!`, `SPRP`, `SRAT`, `TBAS`, `TJMP`, `RELS`, `HPD0/1`, `HPA0/1`, `NEED`.
- **CoreMedia** : dictionnaires à clés string/index, `NSNumber`, `CMTime`, `CMClock` (skew), `AudioStreamBasicDescription`, `FormatDescriptor` (SPS/PPS H.264), `CMSampleBuffer` vidéo et audio avec timing, tailles d'échantillons, attachments et NALU Annex-B.
- **Machine d'état `QtSession`** : rejoue la poignée de main complète (réponses `RPLY`, dicts `HPD1`/`HPA1`, `NEED`, suivi de l'horloge audio pour `SKEW`, fermeture `HPA0`/`HPD0`).
- **Validation** : `tools/QuickTimeCodec` rejoue les captures binaires du projet de référence — 132 assertions au niveau octet (parsing + sérialisation exacte des réponses).

Reste à faire côté USB : activation de la configuration cachée de l'iPhone (control request `0x40/0x52`), endpoints bulk via libusb/WinUSB, puis `QtFramer` → `QtSession` → `IFrameSource`. Cette partie exige un iPhone physique — et un binding de pilote par appareil, friction d'installation à documenter.

## Limites assumées

- **Lecture seule par défaut** — AirPlay ne transporte pas d'entrées ; le contrôle BLE HID est opt-in et relève du même principe qu'un clavier/souris Bluetooth physique.
- Pas d'automatisation, pas d'injection d'entrées — la limite produit est la même que pour Android.
