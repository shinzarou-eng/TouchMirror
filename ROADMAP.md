# Roadmap TouchMirror

**Un espace de travail Windows pour plusieurs téléphones Android, avec des commandes humaines et des intégrations maîtrisées.**

[Accueil](README.md) · [Versions](https://github.com/shinzarou-eng/TouchMirror/releases/latest) · [Proposer une idée](https://github.com/shinzarou-eng/TouchMirror/issues/new) · [Discord](https://discord.gg/DBJ9kNCdX)

## Cap produit

TouchMirror doit permettre de retrouver ses téléphones, sa disposition et ses réglages sans tout reconfigurer à chaque session. La priorité est une expérience multi-appareils fiable : comprendre une coupure, restaurer un miroir, changer de téléphone et partager une capture simplement.

Le client reste open source sous licence MIT. La différence se construit dans la qualité de l'expérience, la fiabilité et les intégrations, pas dans l'automatisation du jeu.

## Lire cette roadmap

- **Prioritaire** : prochain chantier proposé, avant l'ajout de nouvelles fonctionnalités.
- **Prévu** : direction retenue, à détailler après validation des étapes précédentes.
- **À l'étude** : piste à évaluer, sans engagement de livraison.
- Une case cochée signifie que le résultat a été implémenté et vérifié, pas seulement commencé.

L'ordre indique les priorités, pas des dates de sortie. Les périmètres peuvent évoluer avec les tests sur appareils réels et les retours de la communauté. Aucun numéro de version n'est réservé à ces étapes.

## Vue d'ensemble

| Étape | Objectif | Statut | Dépendance |
|---|---|---|---|
| 1. Fiabilité et confiance | Renforcer les plugins, l'API et la reconnexion | Prioritaire | Base actuelle |
| 2. Espaces de travail | Retrouver une installation multi-téléphones en un clic | Prévu | Étape 1 |
| 3. Prise en main et performance | Rendre la première connexion évidente et maîtriser le coût des tuiles | Prévu | Indépendant, en parallèle des étapes 1–2 |
| 4. Diagnostic et assistance | Expliquer les problèmes et faciliter leur résolution | Prévu | Étape 1, puis priorité après les espaces de travail |
| 5. Capture et création | Sauvegarder les moments utiles et simplifier le streaming | À l'étude | Base stabilisée et mesures de ressources |
| 6. Intégrations officielles | Relier TouchMirror aux outils du bureau sans automatiser le jeu | À l'étude | API fiabilisée à l'étape 1 |

## 1. Fiabilité et confiance

**Résultat attendu :** une reconnexion prévisible, des autorisations explicites et une API fiable avec plusieurs clients.

- [x] Vérifier le contenu des plugins à chaque lancement, y compris au démarrage de TouchMirror, et empêcher l'exécution d'un contenu différent de celui validé.
- [x] Lier l'autorisation d'un script tiers à son empreinte : toute modification doit demander un nouvel accord.
- [x] Refuser le lancement d'un script non autorisé si la confirmation ne peut pas être affichée.
- [x] Remplacer l'exécution de scripts externes par un moteur JavaScript sandboxé (Jint) : aucun processus enfant, aucun accès fichier/réseau/process depuis le plugin, seulement l'API `tm.*`.
- [x] Durcir le sandbox : limites mémoire/récursion/tableaux/regex, files et timers bornés, appels API limités à 30/s, actions mutantes tracées au journal, hash couvrant `plugin.js` + `plugin.json`.
- [x] Arrêter les plugins retirés et fiabiliser leur cycle de démarrage, d'arrêt et de sortie.
- [x] Distribuer chaque événement SSE à chaque client abonné, avec des files limitées et un nettoyage à la déconnexion.
- [x] Limiter le plugin de reconnexion aux appareils explicitement sélectionnés et respecter les déconnexions volontaires.
- [x] Séparer la connexion du miroir du lancement d'application : une connexion via l'API ou un plugin ne doit pas lancer Dofus.
- [ ] Ajouter des tests de non-régression sur l'authentification, les autorisations des plugins et les reconnexions.
- [x] Livrer en même temps un premier gain visible : renommer les tuiles et réordonner la grille manuellement — une release de durcissement doit aussi apporter quelque chose à l'utilisateur.

**Validation :** un plugin modifié ne redémarre pas sans accord ; deux clients SSE reçoivent les mêmes événements ; un client lent ne provoque pas une accumulation sans limite ; un appareil déconnecté volontairement reste arrêté ; une reconnexion automatique ne lance pas le jeu ; les tuiles portent un nom choisi et se réordonnent.

### Limites actuelles à connaître

La vérification repose sur des empreintes SHA-256 embarquées, pas sur une signature numérique d'éditeur. Les plugins tournent dans un moteur JavaScript sandboxé qui n'expose que l'API `tm.*` (control-plane) — pas d'accès fichier, réseau ou processus. Le sandbox limite la portée, mais le code d'un plugin tiers reste à lire avant activation.

## 2. Espaces de travail

**Résultat attendu :** choisir « Solo », « Duo » ou « Stream » et retrouver son installation.

- [ ] Créer, renommer, dupliquer et supprimer un espace de travail.
- [ ] Mémoriser les appareils sélectionnés, l'ordre des tuiles et le miroir actif.
- [ ] Personnaliser les noms et les couleurs des téléphones dans TouchMirror.
- [ ] Enregistrer les réglages vidéo et audio par appareil.
- [ ] Restaurer la disposition et les réglages à la demande, sans action en jeu ni lancement automatique du jeu.
- [ ] Conserver les appareils absents dans l'espace, avec un état clair et sans bloquer les autres.
- [ ] Associer des raccourcis aux espaces en préservant les raccourcis de changement de miroir.

**Validation :** après redémarrage de TouchMirror, un espace retrouve son ordre et ses réglages ; un téléphone absent ne bloque pas la restauration ; les dispositions restent utilisables avec différentes tailles de fenêtre et mises à l'échelle Windows.

## 3. Prise en main et performance

**Résultat attendu :** une première connexion qui ne se rate pas, et un coût maîtrisé quand le nombre de tuiles augmente.

- [ ] Guider la première connexion pas à pas : activation du débogage USB, autorisation sur le téléphone, passage en WiFi — chaque étape vérifiable par l'app.
- [ ] Détecter les blocages courants dès l'installation : adb absent, pilote manquant, autorisation refusée, réseau incompatible.
- [ ] Réduire le coût des tuiles inactives : fps et débit adaptés au focus, mesures avant/après pour prouver le gain.
- [ ] Afficher les mesures locales par miroir (fps réels, débit, pertes) avec leur définition.
- [ ] Mesurer le coût CPU/mémoire/batterie à 2, 4 et 6 tuiles et publier des budgets de ressources par configuration.

**Validation :** un nouvel utilisateur connecte son premier téléphone sans documentation externe ; à 4 tuiles, la consommation est mesurée et les tuiles inactives coûtent significativement moins que la tuile active.

## 4. Diagnostic et assistance

**Résultat attendu :** remplacer une erreur générique par une cause compréhensible et une prochaine action utile.

- [ ] Distinguer les états observables : appareil absent, autorisation USB manquante, appareil hors ligne ou session vidéo interrompue.
- [ ] Proposer une action adaptée à chaque état, sans cliquer dans le jeu.
- [ ] Présenter un historique des connexions, coupures et tentatives de reconnexion.
- [ ] Afficher les mesures disponibles avec leur définition, sans les présenter comme une latence de bout en bout si elle n'est pas mesurée.
- [ ] Permettre l'export volontaire d'un diagnostic avec aperçu avant partage.
- [ ] Retirer des exports les tokens, numéros de série, adresses réseau, chemins personnels et autres identifiants sensibles ; ne pas inclure de capture par défaut.

**Validation :** des essais USB et WiFi reproduisent les principaux états ; chaque message indique une action pertinente ; des données sensibles de test sont absentes du rapport exporté. Aucun rapport n'est envoyé automatiquement.

## 5. Capture et création

**Résultat attendu :** conserver un moment et préparer une diffusion sans compliquer la session.

- [ ] Étudier un replay local à durée configurable pour sauvegarder les dernières secondes du miroir actif.
- [ ] Mesurer et limiter la consommation mémoire et disque, avec une désactivation simple.
- [ ] Regrouper les captures et clips dans une bibliothèque locale.
- [ ] Étudier une source de capture OBS stable par téléphone, indépendante des changements de disposition.
- [ ] Prévoir des masques de confidentialité configurables et vérifier leur comportement en cas de rotation ou de redimensionnement.

**Validation avant engagement :** prototype mesuré sur plusieurs configurations ; clips lisibles après changement de miroir ou coupure ; masques présents sur les sorties concernées. Les masques manuels ne seront pas présentés comme une anonymisation automatique garantie.

## 6. Intégrations officielles

**Résultat attendu :** des outils externes qui pilotent TouchMirror, pas les actions du joueur.

- [ ] Évaluer une intégration Stream Deck pour sélectionner un miroir, prendre une capture et contrôler l'enregistrement.
- [ ] Évaluer une intégration OBS pour synchroniser les états utiles à la diffusion.
- [ ] Prévoir des notifications de coupure de session avec destinations et contenu choisis par l'utilisateur.
- [ ] Étudier un format déclaratif pour les intégrations simples : actions autorisées et paramètres, sans exécution de code arbitraire.
- [ ] Définir des autorisations API par intégration et leur révocation.
- [ ] Documenter le contrat API, les erreurs et la compatibilité entre versions avant diffusion des intégrations.

**Validation avant diffusion :** plusieurs intégrations coexistent sans perte d'événements ; les autorisations sont explicites et révocables ; aucun parcours officiel n'exécute de commandes de jeu.

## Hors périmètre

**Ces éléments ne feront jamais partie de TouchMirror — ni aujourd'hui, ni dans une version future :**

- Macros de combat, récolte, déplacement ou toute autre automatisation du gameplay.
- Bots ou scripts jouant à la place du joueur.
- Clics automatiques sur les écrans de connexion du jeu, résolution de captchas ou contournement de protections.
- Diffusion d'une même action sur plusieurs téléphones.
- Routes API d'injection tactile, clavier, texte ou presse-papiers vers Android.

Un moteur de mirroring maison, un service cloud et une marketplace de scripts ne sont pas prioritaires dans cette roadmap.

TouchMirror n'est pas affilié à Ankama. L'absence d'automatisation ne constitue ni une approbation de l'éditeur ni une garantie contre une sanction. Les règles applicables restent celles de l'éditeur.

## Participer et suivre l'avancement

Pour proposer une amélioration, ouvre une [issue GitHub](https://github.com/shinzarou-eng/TouchMirror/issues/new) en précisant le problème rencontré, le résultat attendu et le contexte USB ou WiFi. Ne joins pas de token, de numéro de série ni de capture contenant des informations personnelles.

Les retours et échanges se font aussi sur [Discord](https://discord.gg/DBJ9kNCdX). Les demandes seront évaluées selon leur utilité, leur fiabilité, leur coût de maintenance et leur respect du périmètre ci-dessus.

Les cases de cette roadmap seront mises à jour après validation. Les fonctionnalités effectivement livrées seront décrites dans les [notes de version](https://github.com/shinzarou-eng/TouchMirror/releases/latest).
