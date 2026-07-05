# Build depuis les sources

## Prérequis

- **.NET 10 SDK** — [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
- Windows 10/11 avec le workload desktop (le SDK suffit, Visual Studio non requis)

C'est tout : **adb, le serveur scrcpy et FFmpeg sont embarqués** dans le repo (`assets/`).

## Compiler et lancer

```bash
git clone https://github.com/shinzarou-eng/TouchMirror.git
cd TouchMirror
dotnet build src/AndroidMirror/TouchMirror.csproj
```

Le binaire sort dans `src/AndroidMirror/bin/Debug/net10.0-windows/TouchMirror.exe`.

## Publier un exécutable autonome

```bash
dotnet publish src/AndroidMirror/TouchMirror.csproj -c Release -r win-x64 --self-contained
```

## Structure du projet

```
src/AndroidMirror/
├── MainWindow.xaml(.cs)     # Fenêtre principale — toolbar, hub, panneaux
├── Views/MirrorView.xaml    # Tuile de miroir (vidéo + input)
├── ViewModels/              # MainViewModel, MirrorInstance (MVVM Toolkit)
├── Services/
│   ├── AdbService.cs        # Détection appareils, commandes adb, écran éteint
│   ├── UpdateService.cs     # Vérification des releases GitHub
│   └── AppLogger.cs         # Journal
├── Scrcpy/                  # Protocole + session serveur scrcpy
└── Video/                   # VideoDecoder (FFmpeg.AutoGen), AudioPlayer (NAudio),
                             # Mp4Recorder (remux)
assets/
├── scrcpy-server.jar        # Poussé sur le téléphone au démarrage
├── platform-tools/          # adb embarqué
└── ffmpeg.zip               # DLLs natives, extraites au premier lancement
```

## Dépendances NuGet

| Package | Rôle |
|---|---|
| WPF-UI | Composants Fluent |
| CommunityToolkit.Mvvm | MVVM source-généré |
| FFmpeg.AutoGen | Bindings FFmpeg (décodage + remux) |
| Microsoft.Web.WebView2 | Navigateur d'aide intégré |
| NAudio | Sortie audio |

## Contribuer

Issues et PR bienvenues sur le [repo GitHub](https://github.com/shinzarou-eng/TouchMirror). Règle d'or du projet : **pas d'automatisation ni de macro** — voir [Conformité Ankama](Conformite-Ankama.md).
