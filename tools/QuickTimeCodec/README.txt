QuickTimeCodec — validateur du codec QuickTime/iOS de TouchMirror.

Rejoue les captures binaires du protocole de recopie d'écran iOS (USB)
contre le parseur/sérialiseur de src/AndroidMirror/QuickTime/.

Les binaires de fixtures/ proviennent du projet MIT
github.com/danielpaulus/quicktime_video_hack (screencapture/packet/fixtures
et screencapture/coremedia/fixtures).

Usage : dotnet run -c Release
Sortie : PASS/FAIL par fixture, résumé final. Code de sortie != 0 si échec.
