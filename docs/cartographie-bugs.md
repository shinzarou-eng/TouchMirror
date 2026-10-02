# Cartographie des bugs — TouchMirror

Registre interne des bugs connus, zones à risque et dette de test.

- **Base** : `b6cd8c8` (release 0.9.6) + chantier non commité
- **Dernière revue** : 2026-10-19

## Lecture

| Champ | Valeurs |
|---|---|
| Sévérité | **P0** bloquant · **P1** majeur · **P2** mineur |
| Statut | 🔴 ouvert · 🟡 surveillé (fragile, repro non ferme) · 🟢 corrigé (gardé pour la régression) · ⚪ hypothèse (jamais observé, le code le permet) |
| Preuve | *confirmé* = démontré dans le code/contrat API · *risque* = entrelacement ou panne à reproduire sur le matériel |

**Convention** : un correctif fait passer l'entrée en 🟢 et s'archive en
« Livré » — on ne supprime jamais une entrée, la carte sert la régression.
ID incrémentaux par section (`A1`…`H3`) ; prochain ID = dernier + 1.

## A. Chaîne ADB / scrcpy (Android)

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| A1 | P1 | Identité appareil USB↔Wi-Fi | La clé = modèle + serial USB. Si le serial USB n'a jamais été vu, un appareil Wi-Fi peut créer un doublon ou perdre ses réglages à la migration | 🟡 | `AdbService`, `DeviceKey`/`MatchesSerial` |
| A2 | P1 | Conflit adb server | Un autre adb (Android Studio, autres outils) sur :5037 tue ou remplace le nôtre — détecté en diag, mais le miroir peut mourir sans explication visible | 🟡 | `AdbService`, note `wiz.blocked_adb` |
| A3 | P2 | Appareil `offline` clignotant | Un tel qui oscille offline/device peut faire boucler l'étape wizard et le statut | ⚪ | `WizardStepEvaluator`, `RefreshDevicesAsync` |
| A4 | P1 | Mort de scrcpy silencieuse | `UnexpectedDeath` existe ; les lignes moteur sont préfixées `[device]` et le stderr est drainé — cause restant à observer | 🟡 | `MirrorInstance`, `ScrcpySession` |
| A5 | P2 | PnP « comptage » | `PnpOnlyCount` voit un tel branché mais pas encore autorisé adb — risque de faux positif sur un tel en charge seule | ⚪ | `WizardStepEvaluator` |
| A6 | P1 | Reconnexion | Une reconnexion ne doit jamais relancer le jeu (constitution III) — à revérifier à chaque chantier miroir | 🟡 | `MirrorInstance`, `ReconnectStatus` |
| A7 | P1 | `exec-out` stderr non drainé | Corrigé sur screencap/bench ; tout nouveau `exec-out`/`shell` redirigé doit drainer stderr sous peine de deadlock de pipe | 🟢 | `AdbService` |

## B. AirPlay / iOS sans fil

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| B1 | P1 | Heuristique canal data HAP | Choix du canal chiffré observé, pas spécifié — une version iOS peut la casser | 🟡 | `AirPlaySession` |
| B2 | P1 | Ordre des champs TLV `pair-verify` | Piège connu déjà rencontré ; toute refactor du codec TLV peut re-casser l'ordre | 🟡 | `Pairing`, `Tlv8` |
| B3 | P2 | FairPlay dépend du helper GPL | Si `FairPlayHelper.exe` manque/crash, le miroir échoue tard — vérifier le message d'erreur exposé | 🟡 | `FairPlay`, `FairPlayDecrypt` |
| B4 | P1 | Audio AAC-ELD | Décodage encore instrumenté (logs debug) — zone en cours de stabilisation | 🟡 | `AirPlayAudioStream` |
| B5 | P1 | Resync horloge NTP | Drift ou burst de pertes → gel/audio décalé ; resync corrigé côté D3D mais pas audité côté audio | 🟡 | `NtpTiming`, `AirPlayAudioStream` |
| B6 | P1 | Annonce mDNS multi-interfaces | Vérifié : Makaretu joint le groupe sur toutes les interfaces par défaut — pas de fix requis | 🟢 | `MdnsHost`, `AirPlayService` |
| B7 | P2 | `pair-pin-start` → 501 | iOS récent répond 501 selon l'état — fallback codé mais à retester par version | 🟡 | `Pairing` |
| B8 | P2 | Conflit récepteur | Un autre récepteur AirPlay avec le même nom : collision TXT non gérée ? | ⚪ | `MdnsHost` |

## C. BLE HID (contrôle iOS)

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| C1 | P1 | Mapping curseur absolu | Recalage par orientation/résolution — régression possible à chaque nouveau device | 🟡 | `BleHidHost`, modes de mapping |
| C2 | P2 | Pairing Windows BLE | Le PIN BLE Windows peut expirer entre deux sessions ; comportement à documenter dans le diagnostic | ⚪ | `BleHidHost` |
| C3 | P1 | Perte BLE pendant miroir | Vidéo AirPlay continue mais le contrôle tombe silencieusement — état UI à vérifier | 🟡 | `BleHidHost`, tuile |

## D. Rendu vidéo / enregistrement

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| D1 | P0 | Contexte D3D11 partagé | Sérialisation corrigée (`8745315`) — zone à risque élevé, tout changement présentateur = retester multi-miroirs | 🟢 | `GpuPresenter` |
| D2 | P1 | Resync sur retard accumulé | Borné (`8745315` + 0.9.6) : sortie de drop exige un keyframe frais (lag < 350 ms) ; surveiller PC en veille / pause longue | 🟢 | `GpuPresenter`, `IFrameSource` |
| D3 | P1 | `Mp4Recorder` coupure franche | Débrancher pendant l'enregistrement : fichier finalisé ou corrompu ? | ⚪ | `Mp4Recorder` |
| D4 | P2 | Décodage H.264 flux AirPlay | Les NALU de type inconnu sont droppés — sur certains iOS le flux peut produire des artefacts persistants | 🟡 | `VideoDecoder` |
| D5 | P2 | Curseur iOS (PiP) | Mapping par orientation — régressions possibles par device | 🟡 | `MirrorView` |
| D6 | P1 | Backbuffer à la taille viewport | Le rebind D3D9/D3DImage suit les resizes (debounce 160 ms) — transition bornée par `_pendingRebind`, dirty-rect calé sur `_bgra` — à valider en drag-resize prolongé et multi-DPI | 🟡 | `GpuPresenter`, `MirrorView.SetOutputSize` |
| D7 | P1 | Plancher adaptatif | Le débit adaptatif pouvait descendre à 1,5 Mbps — injouable sur flux ≥1440p (rapport S22/S24). Plancher par dimension max : ≥2560→8 Mbps, ≥1440→5 Mbps, sinon inchangé ; appliqué à la taille réelle de la vidéo | 🟢 | `AdaptEvaluator.SetFloor`, `MirrorInstance.ApplyAdaptFloor` |
| D8 | P1 | Qualité encodeur dépend du SoC | À codec égal (H265 auto), l'encodeur Exynos (`c2.exynos.*`) produit une image plus molle que Qualcomm au même débit — hypothèse forte pour le rapport S22, à confirmer par test USB seul. Le nom d'encodeur remonte au log (`Ln.i`) et au diag (`enc=`) ; `KEY_FRAME_RATE` reflète désormais `maxFps` | 🟡 | `SurfaceEncoder`, `pickAuto` |

## E. UI / WPF

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| E1 | P2 | `ProgressBar.Value` TwoWay | Audit refait — `BenchSeconds` corrigé ; tout `ProgressBar` bindé sans `Mode=OneWay` reste suspect | 🟢 | `MainWindow.xaml` |
| E2 | P2 | `async void` handlers | ~10 handlers : les exceptions retombent sur `DispatcherUnhandledException` (loggées, pas de crash) — risque résiduel = état incohérent en plein milieu | 🟡 | `MainWindow.xaml.cs` |
| E3 | P2 | Z-order overlays | Wizard plein écran + autres overlays (PiP, diag export) : ordre d'empilement à re-tester si un nouvel overlay arrive | 🟡 | `MainWindow.xaml` |
| E4 | P2 | Binding converters absents | Un `StaticResource` manquant ne crashe qu'au runtime — vérifier chaque ajout à la main | 🟡 | global XAML |
| E5 | P1 | Watchdog UI pendant transition d'espace | Rapport Alaba : l'app « crashe » au switch/création/fermeture d'espace — mécanisme probable : teardown+recréation de N miroirs fige le dispatcher >12 s → `Environment.Exit(2)`. Grâce de 60 s via `App.SuppressWatchdog()` dans `RestoreWorkspaceAsync` ; si le crash persiste, `crash.log`/`app.prev.log` désormais inclus dans l'export trancheront | 🟡 | `App.StartUiWatchdog`, `RestoreWorkspaceAsync` |

## F. Réseau local / API / plugins

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| F1 | P1 | Token API locale | Masqué dans les exports diag — pattern : chaque feature qui introduit un secret doit mettre à jour le sanitizer | 🟡 | `ReportSanitizer`, `CollectReportIds` |
| F2 | P2 | Signature plugins LF/CRLF | `index.json` et `VerifiedPlugins` régénérés diffèrent selon PowerShell 5/7 — corrigé mais fragile à chaque outil | 🟢 | `tools/sign-plugins.ps1` |
| F3 | P2 | Plugin en boucle | Pas de fuse/circuit breaker sur les événements JS — un plugin qui boucle peut spammer les logs | ⚪ | `PluginHost`, `PluginApi` |
| F4 | P2 | Discord/Presence ping | Coupure silencieuse côté UX (voulu) mais visible dans le log | 🟡 | `DiscordPresence`, `PresencePing` |

## G. Réglages / persistance

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| G1 | P1 | Migration identité | Réglages rattachés à `DeviceKey` ; si la clé change (serial découvert plus tard), les prefs semblent « perdues » | 🟡 | `SettingsStore` |
| G2 | P2 | `WizardSeen` | Valeur absente = overlay réaffiché aux utilisateurs existants (comportement choisi, à confirmer) | ⚪ | `SettingsStore`, `MainViewModel` |
| G3 | P2 | Clamp `AdaptiveCeiling` | Validation au chargement ; valeurs hors bornes remises à null | 🟢 | `SettingsStore` |

## H. Diagnostic / support

| # | Sév. | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|------|-------------------|--------|----------|
| H1 | P1 | Couverture sanitizer | Chaque nouvelle donnée sensible doit être ajoutée à la main — un oubli = fuite dans un export | 🟡 | `ReportSanitizer` |
| H2 | P2 | Perte d'un crash ancien | `crash.log` dédié écrit depuis 0.9.6 — survit au flood du journal | 🟢 | `AppLogger` |
| H3 | P2 | Doc/impl drift | `docs/airplay.md` pointe désormais `tools/QuickTimeCodec` | 🟢 | `docs/airplay.md` |

## Livré

- **`40534b4`** — R01–R12 : affinity `D3DImage`, watchdog avec nettoyage,
  fermeture bornée 10 s, transition d'espace sérialisée, gardes
  d'identité callbacks, `FailRebind`, capture de raccourcis prioritaire,
  key-up de navigation consommé, `_confirmTcs` dans `navAllowed`,
  collections null normalisées.
- **`3901e9f` — 0.9.5** — texte composé au clavier (`!`, `@`, majuscules,
  AZERTY) ; display virtuel qui survit au suspend ; app lancée une fois
  directement sur le bon display.
- **`b6cd8c8` — 0.9.6** — R13 borné (keyframe frais <350 ms, mesure
  reprise→première frame) ; `ws.Devices` dédupliqué avec auto-guérison ;
  bouton Dofus scopé `@userId` (vol de task corrigé) ; logs `[device]`,
  `vd=`/`app=` au diag, `crash.log` ; résolution display retentée,
  fallback 400 dpi.
- **Non commité** — gel d'arrêt plugin (`_stopped` avant `Join`, `Wait`
  sondé 50 ms) ; stderr drainé sur `exec-out` ; `ScreenDimmer.Refs--` sur
  échec ; Échap sur modale depuis un champ texte + TCS sortant complété ;
  désabonnements `BindKeybinds`/`Detach` ; IP Wi-Fi sur toute interface
  `wlan*` ; volume API sur valeur clampée ; backbuffer à la résolution
  d'affichage + CAS post-upscale (voir D6) ; plancher adaptatif par
  résolution (D7) ; `KEY_FRAME_RATE` réel + nom d'encodeur au
  diag `enc=` (D8) ; grâce watchdog sur transition d'espace +
  `crash.log`/`app.prev.log` dans l'export (E5) ; filet de tests
  `AdaptEvaluator`/`WatchdogGate`.

## Leçons — rectifications de diagnostics passés

- La cause du gel 0.9.3 n'est pas prouvée : vidéo noire + log ne
  démontrent ni saturation GPU ni pile bloquante.
- `D3DImage` n'est pas utilisable depuis un worker : `TryLock`,
  `Unlock`, `AddDirtyRect` exigent leur Dispatcher.
- Le reset à la réactivation vient de `SetVideoHidden(false)` →
  `ResetVideo`, pas d'un palier de débit.
- `[reconnexion: ]` vide peut refléter un nettoyage bloqué, pas juste
  un souci cosmétique.
- `état=unknown` ≠ batterie inconnue : `LastDeviceState` est alimenté
  par `WaitForDeviceAsync`.
- `caps=0xff` ne contient pas le bit sync-frame `0x100`.
- Reconfigurer MediaCodec ≠ frame présentée : la récupération se mesure
  jusqu'à l'image décodée — instrumentée depuis 0.9.6.

## Dette de test

Zones sans couverture automatisée :

- `AdbService` — parsing `adb devices`, états, erreurs process : la zone
  la plus externe et la plus fragile, zéro test.
- `AirPlaySession` / `RtspServer` / `Pairing` — le cœur protocole n'est
  testé que via `FakeAirPlayHost` manuellement.
- `MirrorInstance` — cycle de vie, reconnexion, mort inattendue.
- `GpuPresenter` — rebind, `_upscale`, transitions de taille.
- Sanitizer — couvert, mais rien ne garantit l'ajout des nouveaux
  secrets (voir H1).
- Boucle du watchdog — la décision (`WatchdogGate`) est couverte ;
  l'intégration dispatcher reste manuelle.

## Priorités

1. Valider 0.9.6 sur le PC touché : compte secondaire sur VD sans vol de
   task, workspace 2 joignable, densité correcte ; `vd=`/`app=`/
   `crash.log` confirment si un symptôme persiste.
2. D6 — drag-resize prolongé + changement de moniteur multi-DPI sur un
   miroir « Netteté » actif.
3. E2 — passer les `async void` handlers en commandes sécurisées ou
   wrapper try/catch explicite.
4. A1/G1 — tel jamais branché en USB, d'abord vu en Wi-Fi : vérifier
   absence de doublon et de réglages perdus.
5. H1 — checklist sanitizer à chaque nouveau secret introduit.
