FakeAirPlayHost — feeder de test pour la tuile iOS, sans iPhone.

Il remplace AirPlayHost.exe dans bin/.../assets/airplay/ : il se connecte
aux named pipes de TouchMirror, envoie un event "connected" puis pousse
600 frames YUV420P animees (degrade + carre mobile, 640x360 @ ~30fps).

Usage :
  1. dotnet build tools/FakeAirPlayHost -c Release
  2. Renommer assets/airplay/AirPlayHost.exe -> AirPlayHost.native.exe
  3. Copier le build (AirPlayHost.exe + .dll + .runtimeconfig.json + .deps.json)
  4. Lancer TouchMirror -> menu "..." -> "Ajouter un iPhone"
  5. La tuile doit afficher le pattern anime et "Fake iPhone" connecte
