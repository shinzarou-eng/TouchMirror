# Architecture & invariants

Notes de conception pour celles et ceux qui contribuent ou auditent le code.

## Control-plane uniquement

L'API locale (`LocalApi`) et les plugins (`PluginHost`) pilotent **l'application**, jamais le jeu : aucune route ni aucun appel plugin n'envoie de tactile, de clavier, de texte ou de presse-papiers vers Android. Le `ControlChannel` scrcpy (touches, texte, tactile) n'est atteignable que depuis `MirrorView` — les interactions humaines directes.

Deux exceptions assumées, équivalentes à un branchement manuel : `connect` peut lancer l'app configurée (`StartApp`) et réveiller l'écran (`KEYCODE_WAKEUP`) si l'option « écran atténué » est active. Ce sont des actions de session, pas des inputs de jeu.

Les keybinds (touche → tap à position fixe) sont du remapping d'entrée : 1 pression physique = 1 tap, sans auto-repeat. Ce n'est ni une macro ni de l'automatisation.

## Plugins : l'installation est le consentement

Le catalogue (`marketplace/index.json`) n'est pas signé : son hash vérifie l'intégrité du téléchargement, pas l'éditeur. Installer un plugin = exécuter du code distant → une confirmation explicite est demandée **avant** tout téléchargement, et le hash affiché/validé est celui des octets réellement téléchargés.

`RescanPlugins` ne démarre jamais un plugin non vérifié et non approuvé. `VerifyNow` hashe les octets qui seront exécutés — le même buffer, pas une relecture du disque.

L'`id` de catalogue est un identifiant (`[A-Za-z0-9_-]`), jamais un chemin : validation stricte côté install **et** désinstall.

## Sandbox

Jint confiné : pas d'interop CLR, mémoire/temps/récursion bornés, 30 appels/s max. `tm.read`/`tm.write` confinés au dossier du plugin, extensions whitelistées (données, jamais de `.js`). `__call` brut est audité comme un accès privilégié.

## Série USB ↔ Wi-Fi

Un téléphone a deux identités adb : `serial` en USB, `ip:port` en Wi-Fi. L'app migre la session entre les deux sans perdre les réglages : la clé d'identité est le modèle + le serial USB quand il est connu, sinon le serial courant. Les espaces (workspaces) mémorisent les appareils par cette clé, pas par le mode de connexion.

## Pipeline vidéo

Décodeur D3D11VA → texture NV12 → pixel shader custom (conversion YUV→BGRA + netteté) → texture D3D9 partagée → `D3DImage`. Une copie GPU par frame (staging vers partagée) — pas de readback CPU, pas de copie côté thread UI.

`GpuPresenter` est `IDisposable` et appartient au `MirrorInstance` : disposé à chaque reconnect/déconnexion. Le cache de vues (`_srcCache`) est indexé sur (pointeur de texture, index de slice).
