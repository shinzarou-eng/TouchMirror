# TouchMirror Engine

Composant on-device de TouchMirror (capture vidéo/audio, injection d'entrées,
canal de contrôle). Exécuté sur l'appareil Android via `app_process`.

## Origine

Basé sur **scrcpy-server** (https://github.com/Genymobile/scrcpy),
Copyright (C) 2018 Genymobile, Romain Vimont — licence Apache 2.0
(voir `LICENSE` dans ce dossier).

## Modifications par rapport à l'upstream

- Package renommé `com.genymobile.scrcpy` → `com.touchmirror.engine`
- Artefact renommé `touchmirror-engine.jar`
- Tag logcat `scrcpy` → `touchmirror`, noms internes (socket abstract,
  displays virtuels, input port UHID) renommés `touchmirror`
- **Nouvelle option `start_app=<spec>`** : lance une app Android au
  démarrage de la session (syntaxe : `+pkg` = force-stop, `?nom` = recherche
  par nom, `pkg@userId` = profil Android secondaire — multi-compte).
  Utilisée par TouchMirror pour ouvrir Dofus Touch automatiquement.
- La résolution d'app (`+`/`?`/`@user`) est centralisée dans
  `Device.startApp(spec, displayId)`, réutilisée par le message de contrôle
  upstream et par l'option `start_app`.
- `versionName` = `4.1-tm.1` : protocole hérité de scrcpy 4.1, révision
  TouchMirror. Le client envoie cette chaîne en premier argument (le serveur
  refuse tout mismatch).

## Build

Prérequis : JDK 17+ (`JAVA_HOME`) et Android SDK (`local.properties`
avec `sdk.dir=...` ou `ANDROID_HOME`).

```powershell
.\build-engine.ps1
```

Compile `:server:assembleRelease` puis copie l'APK non signé vers
`..\assets\touchmirror-engine.jar` (le « jar » est en réalité l'APK
non signé : `classes.dex` + ressources).
