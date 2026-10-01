# Cartographie des bugs — TouchMirror

## Audit rapide du code actuel — base `79cc8a1`

> **Mise à jour — correctifs appliqués dans `40534b4`** (build 0 warn/err, 144/144 tests) :
> R01 ✅ (`Invalidate` auto-marshalé vers le Dispatcher de l'image, `TryLock` borné conservé ; callbacks `FrameReady` coalescés avec garde d'identité) · R02 ✅ (nettoyage best-effort des sessions avant `Environment.Exit`) · R03 ✅ (déconnexion parallèle de tous les miroirs + deadline globale 10 s + `Shutdown()` garanti) · R04 ✅ (transition d'espace sérialisée par `_wsSwitchBusy`) · R05 ✅ (désabonnement d'événement `D3DImage` marshalé sur son Dispatcher) · R06 ✅ (`CaptureBgra` sur worker avec attente bornée 2,5 s) · R07 ✅ (gardes d'identité présentateur/session sur `FrameReady`, `SizeChanged`, `AttachControl`, `SetUhid`) · R08 ✅ (échec de commit `SetBackBuffer` → `FailRebind`, compteur remis à zéro seulement après succès) · R09 ✅ (champ de capture prioritaire sur tous les raccourcis, combos réservées refusées, doublons signalés) · R10 ✅ (`_navHeld` consomme le key-up des touches de navigation) · R11 ✅ (`_confirmTcs` bloque la navigation pendant une confirmation) · R12 ✅ (collections null normalisées au chargement). **R13 reste ouvert** — mesure de l'âge du flux à l'instrumenter. Validation réelle toujours requise (2 comptes, Wi-Fi, switches).

> **Mise à jour — correctifs en cours (non commités)** : R13 ✅ borné (la sortie de drop exige un keyframe *frais* — lag paquet < 350 ms — et le contenu périmé n'est plus feedé ; le temps unhide→première frame décodée est mesuré et logué `log.resume_frame`) · `ws.Devices` tolère et guérit les doublons de `DeviceKey` (crash loop `SaveNow`, workspace injoignable) · diagnostic enrichi (`vd=`/`app=` par miroir, attribution `[device]` des lignes moteur, `crash.log` dédié aux FATAL) · résolution du display de compte retentée une fois + loguée, fallback 280→400 dpi.

Périmètre : rendu GPU/WPF, cycle de vie des miroirs, espaces/comptes, fermeture, raccourcis et persistance. Lecture statique ciblée, pas un audit exhaustif de sécurité. Aucun code modifié, aucun test/build/exécutable lancé pour cette passe. Les 144 tests annoncés précédemment ne valident pas les chemins GPU ni les interactions clavier ci-dessous.

Les constats suivants priment sur les anciens statuts « corrigé » plus bas. **Confirmé** signifie démontré par le code/contrat API, pas reproduit sur les téléphones. **Risque** signifie un entrelacement ou une panne à reproduire.

### Priorités et corrections proposées

Chemins ci-dessous relatifs à `src/AndroidMirror/` ; lignes relevées sur cette base.

| ID | Priorité / preuve | Problème et impact | Localisation | Changement proposé / validation |
|---|---|---|---|---|
| R01 | P0 — confirmé | `D3DImage` créé sur l'UI est utilisé depuis le thread vidéo : `FrameReady` appelle directement `View.OnGpuFrame`, puis `Invalidate` → `TryLock`. WPF appelle `WritePreamble` → `VerifyAccess` : exception inter-thread, pas une présentation valide. Le `TryLock` est hors du `try` local. Le dernier correctif peut donc lui-même casser l'affichage. | `MirrorInstance.cs:364`, `MirrorView.xaml.cs:99-126`, `GpuPresenter.cs:812-851` ; également `Redraw` exécuté via `Task.Run` | Garder les opérations WPF sur leur Dispatcher ; concevoir séparément le travail GPU avec échange de buffers correctement synchronisé. Tester une image attachée en STA, callback depuis worker, puis flux réel. Ne pas publier cette base telle quelle. |
| R02 | P1 — confirmé | Le watchdog appelle `Environment.Exit(2)` sans nettoyage applicatif. Ce n'est pas une fermeture propre : MP4 non finalisé, réglages non enregistrés et restauration d'écran peuvent être perdus. Le délai n'est pas exactement 12 s : premier timeout après environ 6 s, puis contrôles toutes les ~6 s. | `App.xaml.cs:63-94` | Capturer des diagnostics avant récupération ; distinguer demande de fermeture et gel temporaire. Tester UI lente, veille/reprise, enregistrement actif. Ne pas présenter ce mécanisme comme une correction du deadlock. |
| R03 | P1 — confirmé | Les délais de fermeture ne couvrent pas les appels synchrones `StopTracking`, `StopPlugins`, `SaveNow`, `StopAirPlay`, ni la partie synchrone de `DisconnectAsync` avant son premier await incomplet. Les continuations de timeout dépendent aussi de l'UI. Une exception au début saute le reste du nettoyage ; après deadline, les miroirs suivants ne sont même pas déconnectés. | `MainWindow.xaml.cs:299-331`, `MirrorInstance.cs:1013-1062` | Nettoyage isolé par service, déclenchement de l'arrêt de tous les miroirs et budget global réel. Tester service bloqué, exception et plusieurs miroirs. `Shutdown()` n'est pas garanti par le code actuel. |
| R04 | P1 — risque étayé | `SelectWorkspaceAsync` ne sérialise pas toute la transition. `IsBusy` est vérifié mais pas posé autour de la suppression/actualisation. Deux clics peuvent entrelacer deux restaurations ; `ActiveWorkspace` change avant les awaits, puis une ancienne restauration applique ordre, sélection et sauvegarde. | `MainViewModel.cs:1001-1059` ; `IsBusy` posé dans `ConnectDeviceAsync`, pas autour de la restauration | Transition unique avec génération/cancellation et sauvegarde uniquement de la transition courante. Tester clics A→B→C pendant déconnexion lente. |
| R05 | P1 — confirmé pour le contrat WPF | `ReleaseDecoder` dispose le présentateur dans `Task.Run`. `GpuPresenter.Dispose` désabonne l'événement `D3DImage.IsFrontBufferAvailableChanged` depuis ce worker ; son accesseur WPF impose aussi `WritePreamble`. L'exception est avalée par l'appelant et peut interrompre la libération GPU après `_disposed=true`. | `MirrorInstance.cs:927-944`, `GpuPresenter.cs:875-905` | Séparer détachement WPF sur Dispatcher et libération GPU hors UI, avec état de fin observable. Tester cycles répétés connexion/déconnexion et mémoire GPU. |
| R06 | P1 — confirmé / déclenchement matériel à reproduire | La capture d'écran reste un chemin driver synchrone sur l'UI : le handler appelle `SaveScreenshot` via `Dispatcher.Invoke`, puis `CaptureBgra` fait `CopyResource` et `Map(Read, None)`. `TryEnter` protège seulement l'attente du verrou, pas l'attente GPU dans `Map`. | `MainWindow.xaml.cs:159-169`, `MirrorView.xaml.cs:1232`, `GpuPresenter.cs:719-761` | Readback asynchrone avec buffer de staging et vérification de disponibilité ; seule mise à jour UI sur Dispatcher. Tester capture pendant saturation GPU. |
| R07 | P1 — risque | Callbacks obsolètes : `FrameReady` de l'ancien présentateur appelle la vue courante sans vérifier l'identité ; `SizeChanged` modifie taille/overlay sans vérifier que le présentateur est encore attaché. `Detach`, `AttachControl` et `SetUhid` mis en file sans attente ni génération rendent l'ordre de nettoyage/reconnexion fragile. | `MirrorInstance.cs:364,457-466,1052`, `MirrorView.xaml.cs:112-120` | Associer callbacks à la session/au présentateur ; ignorer les générations périmées et attendre le détachement avant réutilisation. Tester reconnexion pendant callbacks en attente. |
| R08 | P1 — risque | Rebind publié trop tôt : `_pendingRebind=false` et nouvelles textures installées avant que `SetBackBuffer` soit exécuté sur l'UI. Si le commit WPF échoue, les retries de cette branche ne passent pas par `FailRebind`; `_rebindFails` est remis à zéro avant le résultat du Dispatcher. Un échec persistant peut répéter les allocations au lieu d'un fallback borné. | `GpuPresenter.cs:656-714` | Publier l'état prêt après commit réussi ; compteur par tentative complète, génération de rebind et backoff. Tester `TryLock` indisponible et changement de taille pendant rebind. |
| R09 | P2 — confirmé | Des raccourcis sont acceptés mais inutilisables ou non capturables : F11 est intercepté avant la capture ; Ctrl+G et Ctrl+chiffre aussi. Deux actions peuvent recevoir la même combinaison : seule la première branche sera exécutée. | `MainWindow.xaml.cs:1588-1670,1727-1753` | Traiter le champ de capture avant tous les raccourcis, refuser les réservés et signaler les doublons ; ajouter réinitialisation des valeurs par défaut. Tester F11, Ctrl+G et deux actions sur Right. |
| R10 | P2 — confirmé | Le key-down de navigation est consommé mais pas son key-up : absent de `_keyTargets`, le key-up retombe vers le nouveau miroir. Des événements clavier applicatifs peuvent donc être envoyés au téléphone. | `MainWindow.xaml.cs:1628-1652,1763-1776` | Mémoriser les touches consommées par l'application jusqu'au relâchement ; libérer correctement les touches du précédent miroir. Tester flèche seule et Ctrl+Tab avec changement de cible. |
| R11 | P2 — confirmé | Navigation autorisée dans un champ texte dès qu'un modifieur est présent, ainsi que pendant une confirmation : `navAllowed` ne vérifie pas `_confirmTcs`. Une combinaison personnalisée Ctrl+A peut détourner la sélection de texte ; une navigation peut modifier l'espace sous une confirmation. | `MainWindow.xaml.cs:1624-1652` | Contexte de raccourcis explicite : saisie, capture, modal, surface miroir ; tester Ctrl+A dans un champ et Alt+Right pendant confirmation. |
| R12 | P2 — confirmé | Un fichier JSON valide avec `Devices:null`, `EnabledPlugins:null`, etc. provoque une exception pendant la migration. Le chargeur retourne alors tous les réglages par défaut, pas seulement le champ invalide. Les valeurs numériques générales ne sont pas validées ici, contrairement à `AdaptiveCeiling`. | `Services/SettingsStore.cs:132-167` | Normaliser les collections/nulls, valider les bornes et récupérer les champs valides sans perte globale. Tester JSON partiel, nulls et valeurs hors bornes. |
| R13 | P2 — risque / conception | Après masquage >1,5 s, la réactivation détruit le décodeur et demande `ResetVideo`. Ce chemin explique un redémarrage lors de la bascule sans impliquer une saturation GPU. La resync sort au premier keyframe reçu, qui peut encore être ancien dans le backlog : aucune borne de latence 1–2 s n'est démontrée. | `MirrorInstance.cs:300-331,898-924` | Mesurer âge du flux et temps jusqu'à première image présentée, distinguer reprise volontaire et retard ; tester deux comptes sur un même téléphone, câble puis Wi-Fi. |

### Rectification des diagnostics précédents

- **La cause exacte du gel de la release 0.9.3 n'est pas prouvée.** Une vidéo noire et un log ne démontrent ni saturation GPU ni la pile bloquante. R01 concerne le correctif local `79cc8a1`, pas nécessairement le binaire qui a produit les logs.
- **`D3DImage` n'est pas librement utilisable depuis un worker.** Remplacer une propriété WPF par `_frontLost` ne rend pas `TryLock`, `Unlock` ou `AddDirtyRect` thread-safe.
- **Le reset à la réactivation n'est pas attribuable au palier de débit sur ces seules traces.** `SetVideoHidden(false)` envoie explicitement `ResetVideo`; `AdaptTick` envoie `SetVideoParams`.
- **`[reconnexion: ]` n'est pas forcément seulement cosmétique.** Le code pose `IsReconnecting=true`, attend `ClearSessionAsync`, puis seulement remplit `ReconnectStatus`. Un nettoyage bloqué prolonge cet état. L'attente du gate a lieu avant `IsReconnecting=true` et ne suffit donc pas à expliquer ce libellé.
- **`état=unknown` ne signifie pas batterie inconnue** : `LastDeviceState` est notamment alimenté par la vérification ADB dans `WaitForDeviceAsync`.
- Les journaux multi-miroirs fournis n'identifient pas systématiquement la session émettrice. Impossible d'attribuer chaque ligne suspend/restore au compte exact sans instrumentation.
- `caps=0xff` n'annonce pas le bit sync-frame `0x100`. Ne pas présenter une demande de sync-frame comme déjà testée sur ce moteur.
- Reconfigurer MediaCodec ne prouve pas qu'une config+IDR a effectivement été reçue, décodée puis présentée sur le PC. La récupération annoncée à 1–2 s reste à mesurer.

### Changements utiles hors correctifs immédiats

1. Journaliser identifiant de session non sensible, compte/display, codec et étapes reçu/décodé/présenté ; conserver un diagnostic de gel avec version exacte du binaire/moteur.
2. Exposer le transport réel USB/Wi-Fi, le débit effectif et l'état de reprise par miroir ; ne pas afficher une santé « prêt à jouer » fondée uniquement sur ADB lorsqu'aucune image n'est présentée.
3. Distinguer visuellement reprise vidéo, reconnexion et erreur ; conserver si possible la dernière image plutôt qu'un noir sans explication.
4. Donner accès à un mode logiciel/H.264 de diagnostic sans modifier plusieurs paramètres simultanément.
5. Avant une release : tests WPF STA du rendu/détachement, tests clavier key-down/key-up et conflits, tests de transition d'espaces concurrents, fermeture avec dépendance lente et tests de settings corrompus.
6. Validation réelle indispensable : deux comptes sur un téléphone, deux téléphones, masquage >1,5 s, retour rapide, capture/enregistrement, fermeture et veille/reprise. Aucune promesse de fluidité sur la seule base du build.

### Limites et références

Pas de nouvelle validation détaillée de l'API locale, des signatures marketplace, du pairing AirPlay/BLE ou des mises à jour dans cette passe rapide. Les points historiques correspondants ci-dessous restent une liste à vérifier, pas des vulnérabilités confirmées. Les affirmations anciennes sur l'absence de tests ou l'état « corrigé » ne sont pas revalidées ici.

Contrat WPF vérifié dans les sources officielles :
- https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/D3DImage.cs (`TryLock`, `Unlock`, `AddDirtyRect`, événement de disponibilité → `WritePreamble`).
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.freezable.writepreamble?view=windowsdesktop-10.0 (`WritePreamble` appelle `VerifyAccess`).

## Cartographie historique — éléments non revalidés sauf mention ci-dessus

Document de tri interne : zones à risque, bugs connus, points de vigilance.
Statuts : **🔴 ouvert** · **🟡 surveillé** (fragile, pas de repro ferme) ·
**🟢 corrigé** (gardé pour mémoire/régression) · **⚪ hypothèse** (jamais
observé, mais le code le permet).

Dernière revue : 2026-09-30.

## 1. Chaîne ADB / scrcpy (Android)

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| A1 | Identité appareil USB↔Wi-Fi | La clé = modèle + serial USB. Si le serial USB n'a jamais été vu, un appareil Wi-Fi peut créer un doublon ou perdre ses réglages à la migration | 🟡 | `AdbService`, `DeviceKey`/`MatchesSerial` |
| A2 | Conflit adb server | Un autre adb (Android Studio, autres outils) sur :5037 tue ou remplace le nôtre — détecté en diag, mais le miroir peut mourir sans explication visible | 🟡 | `AdbService`, note `wiz.blocked_adb` |
| A3 | Appareil `offline` clignotant | Un tel qui oscille offline/device peut faire boucler l'étape wizard et le statut | ⚪ | `WizardStepEvaluator`, `RefreshDevicesAsync` |
| A4 | Mort de scrcpy silencieuse | `UnexpectedDeath` existe mais la cause n'est remontée que si stderr a été capturé | 🟡 | `MirrorInstance`, `ScrcpySession` |
| A5 | PnP « comptage » | `PnpOnlyCount` voit un tel branché mais pas encore autorisé adb — messages corrects, risque de faux positif sur un tel en charge seule | ⚪ | `WizardStepEvaluator` |
| A6 | Reconnexion | Une reconnexion ne doit jamais relancer le jeu (constitution III) — à revérifier à chaque chantier miroir | 🟡 | `MirrorInstance`, `ReconnectStatus` |

## 2. AirPlay / iOS sans fil

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| B1 | Heuristique canal data HAP | Le choix du canal chiffré repose sur une heuristique observée, pas spécifiée — une version iOS peut la casser | 🟡 | `AirPlaySession` |
| B2 | Ordre des champs TLV `pair-verify` | Piège connu déjà rencontré ; toute refactor du codec TLV peut re-casser l'ordre | 🟡 | `Pairing`, `Tlv8` |
| B3 | FairPlay dépend du helper GPL | Si `FairPlayHelper.exe` manque/crash, le miroir échoue tard — vérifier le message d'erreur exposé | 🟡 | `FairPlay`, `FairPlayDecrypt` |
| B4 | Audio AAC-ELD | Décodage encore instrumenté (logs debug) — zone en cours de stabilisation | 🟡 | `AirPlayAudioStream` |
| B5 | Resync horloge NTP | Drift ou burst de pertes → gel/audio décalé ; resync corrigé côté D3D mais pas audité côté audio | 🟡 | `NtpTiming`, `AirPlayAudioStream` |
| B6 | Annonce mDNS multi-interfaces | Walky a corrigé chez eux « annoncer sur toutes les interfaces LAN, pas une seule » — vérifier qu'on fait pareil (PC multi-NIC, VPN) | 🔴 à vérifier | `MdnsHost`, `AirPlayService` |
| B7 | `pair-pin-start` → 501 | iOS récent répond 501 selon l'état — fallback codé mais à retester par version | 🟡 | `Pairing` |
| B8 | Conflit récepteur | Un autre récepteur AirPlay sur le réseau avec le même nom : collision TXT non gérée ? | ⚪ | `MdnsHost` |

## 3. BLE HID (contrôle iOS)

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| C1 | Mapping curseur absolu | Recalage à l'orientation ajouté récemment ; chaque orientation/résolution est un cas — régression possible à chaque nouveau device | 🟡 | `BleHidHost`, modes de mapping |
| C2 | Pairing Windows BLE | Le PIN BLE Windows peut expirer entre deux sessions ; comportement à documenter dans le diagnostic | ⚪ | `BleHidHost` |
| C3 | Perte BLE pendant miroir | Vidéo AirPlay continue mais le contrôle tombe silencieusement — état UI à vérifier | 🟡 | `BleHidHost`, tuile |

## 4. Rendu vidéo / enregistrement

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| D1 | Contexte D3D11 partagé | Sérialisation corrigée (`8745315`) — zone à risque élevé, tout changement présentateur = retester multi-miroirs | 🟢 récent | `GpuPresenter` (13 catches) |
| D2 | Resync sur retard accumulé | Corrigé (`8745315`) ; surveiller les cas extrêmes : PC en veille, pause longue | 🟢 récent | `GpuPresenter`, `IFrameSource` |
| D3 | `Mp4Recorder` coupure franche | Débrancher pendant l'enregistrement : fichier finalisé ou corrompu ? | ⚪ | `Mp4Recorder` |
| D4 | Décodage H.264 flux AirPlay | Les NALU de type inconnu sont droppés — sur certains iOS le flux peut produire des artefacts persistants | 🟡 | `VideoDecoder` |
| D5 | Curseur iOS (PiP) | Travail en cours dans `MirrorView.xaml.cs` — non commité | 🟡 | `MirrorView` |

## 5. UI / WPF

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| E1 | `ProgressBar.Value` bind en lecture seule | **Bug confirmé et corrigé** : `Value` est TwoWay par défaut → crash au démarrage si bindé sur une prop get-only. **Pattern à re-greffer partout** : tout `ProgressBar` bindé sans `Mode=OneWay` est suspect | 🟢 corrigé / 🔴 pattern à auditer | `MainWindow.xaml` |
| E2 | `async void` handlers | ~11 handlers `async void` (clic UI + `OnWifiDegraded`) : une exception non catchée y crashe l'app sans rapport | 🟡 | `MainWindow.xaml.cs`, `MainViewModel:3690` |
| E3 | Z-order overlays | Wizard plein écran + autres overlays (PiP, diag export) : ordre d'empilement à re-tester si un nouvel overlay arrive | 🟡 | `MainWindow.xaml` |
| E4 | Binding converters absents | Le wizard a failli utiliser un converter inexistant — pattern : vérifier chaque `{StaticResource}` au build ne suffit pas, le crash est au runtime | 🟡 | global XAML |

## 6. Réseau local / API / plugins

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| F1 | Token API locale | Masqué dans les exports diag (corrigé) — tout nouveau secret doit rejoindre `CollectReportIds` ; **pattern : chaque nouvelle feature qui introduit un secret doit mettre à jour le sanitizer** | 🟡 | `ReportSanitizer`, `CollectReportIds` |
| F2 | Signature plugins LF/CRLF | `index.json` et `VerifiedPlugins` régénérés diffèrent selon PowerShell 5/7 — corrigé mais fragile à chaque outil | 🟢 corrigé | `tools/sign-plugins.ps1` |
| F3 | Plugin crash | `PluginHost` a 11 catches — un plugin qui boucle peut spammer les logs ; pas de fuse/circuit breaker identifié | ⚪ | `PluginHost`, `PluginApi` |
| F4 | Discord/Presence ping | Catches sur cancellation uniquement ; une coupure Discord doit rester silencieuse côté UX (voulu) mais visible dans le log | 🟡 | `DiscordPresence`, `PresencePing` |

## 7. Réglages / persistance

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| G1 | Migration identité | Réglages rattachés à `DeviceKey` ; si la clé change (serial découvert plus tard), les prefs semblent « perdues » | 🟡 | `SettingsStore` |
| G2 | `WizardSeen` | Flag premier lancement — migration des vieux settings : valeur absente = overlay affiché une fois de plus pour les utilisateurs existants (comportement choisi, à confirmer) | ⚪ | `SettingsStore`, `MainViewModel` |
| G3 | Clamp `AdaptiveCeiling` | Validation au chargement ajoutée — valeurs hors bornes remises à null ; OK | 🟢 | `SettingsStore` |

## 8. Diagnostic / support

| # | Zone | Symptôme / risque | Statut | Pointeur |
|---|------|-------------------|--------|----------|
| H1 | Couverture sanitizer | Chaque nouvelle donnée sensible (serial BLE, pairing AirPlay…) doit être ajoutée — process manuel, un oubli = fuite dans un export | 🟡 | `ReportSanitizer` |
| H2 | Journal 250 lignes | Le contexte d'un crash ancien est perdu — envisager un crashlog séparé | ⚪ | `AppLogger`, `BuildDebugReportAsync` |
| H3 | Doc/impl drift | `docs/airplay.md` annonce `src/AndroidMirror/QuickTime/` qui n'existe pas — le portage vit dans `tools/QuickTimeCodec` uniquement | 🔴 doc | `docs/airplay.md:72` |

## 9. Dette de test

Zones **sans** couverture de tests actuellement :

- `AdbService` (parsing `adb devices`, états, erreurs process) — zone la plus
  externe et la plus fragile, zéro test.
- `AirPlaySession` / `RtspServer` / `Pairing` — le cœur protocole n'est testé
  que via `FakeAirPlayHost` manuellement.
- `MirrorInstance` (cycle de vie, reconnexion, mort inattendue).
- Sanitizer : couvert aujourd'hui, mais le test bout-en-bout ne garantit pas
  l'ajout futur des nouveaux secrets (voir H1).

## Priorités suggérées

1. **B6** — vérifier l'annonce mDNS multi-interfaces (Walky l'a corrigé chez
   eux, indice fort que c'est un vrai piège).
2. **E1 pattern** — grep tous les `ProgressBar` bindés TwoWay.
3. **H3** — corriger la doc airplay ou remettre le dossier QuickTime.
4. **E2** — passer les `async void` handlers en commandes sécurisées ou
   wrapper try/catch global.
5. **A1/G1** — scénario de test : tel jamais branché en USB, d'abord vu en
   Wi-Fi → vérifier qu'il n'y a pas doublon ni perte de réglages.
