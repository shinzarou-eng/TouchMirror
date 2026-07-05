# Connexion USB et WiFi

## USB (recommandé pour démarrer)

1. Branche le téléphone → il apparaît dans le sélecteur / le hub
2. Clique **Connecter** → le miroir s'affiche
3. Clique **Dofus** dans la toolbar pour lancer le jeu

L'USB offre la meilleure latence et la meilleure stabilité.

## Passer en WiFi — un clic

1. Téléphone **branché en USB**, connecté dans l'app
2. Menu **⋯ → Activer le WiFi**
3. L'appareil bascule en TCP/IP et reconnecte automatiquement
4. **Débranche le câble** — le flux continue en sans-fil

### Conditions et limites

- PC et téléphone sur le **même réseau** (WiFi ou Ethernet, peu importe)
- La latence dépend de la qualité du WiFi — pour du jeu, un routeur 5 GHz proche est conseillé
- **Après un redémarrage du téléphone**, il faut rebrancher en USB et refaire *Activer le WiFi* — limitation Android, pas un bug
- Le mode WiFi reste actif tant que le téléphone ne redémarre pas

## Le téléphone n'apparaît pas ?

Dans l'ordre :

1. **Câble** : certains câbles ne font que la charge — essaie un autre câble / port USB
2. **Popup d'autorisation** : vérifie le téléphone, la popup RSA peut être passée inaperçue
3. **Bouton refresh** dans la toolbar, ou débranche/rebranche
4. **Options développeur → Révoquer les autorisations de débogage USB**, rebranche, ré-autorise
5. Autre logiciel adb ouvert (scrcpy, Vysor, Android Studio) ? Ferme-le — un seul serveur adb à la fois
6. Dernier recours : redémarre le téléphone

Voir aussi la page [Dépannage](Depannage.md).
