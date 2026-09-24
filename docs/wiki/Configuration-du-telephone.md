# Configuration du téléphone

Une seule chose est nécessaire : le **débogage USB**. Voici le pas-à-pas.

## 1. Activer les options développeur

1. Ouvre **Paramètres → À propos du téléphone**
2. Appuie **7 fois** sur **Numéro de build** (ou *Version MIUI*, *Version One UI*… selon la marque)
3. Le message « Vous êtes désormais développeur » apparaît

## 2. Activer le débogage USB

1. **Paramètres → Options pour les développeurs** (parfois dans *Système* ou *Paramètres supplémentaires*)
2. Active **Débogage USB** (et **Débogage USB (paramètres de sécurité)** sur Xiaomi, pour que les clics simulés fonctionnent)

## 3. Brancher et autoriser

1. Branche le téléphone en USB sur le PC
2. Une popup **« Autoriser le débogage USB ? »** apparaît sur le téléphone → coche *Toujours autoriser depuis cet ordinateur* → **OK**
3. Le téléphone apparaît dans le hub TouchMirror avec le statut **Prêt**

> Si la popup n'apparaît pas : débranche/rebranche, ou **Révoquer les autorisations de débogage USB** dans les options développeur puis rebranche.

## 4. Connecter

Clique **Connecter** dans TouchMirror — le miroir s'affiche en quelques secondes. Ensuite le bouton **Dofus** lance le jeu directement sur le téléphone.

## Marques — points d'attention

| Marque | Réglage supplémentaire |
|---|---|
| **Xiaomi / Redmi / Poco** | Activer aussi *Débogage USB (paramètres de sécurité)* — sinon les clics ne passent pas |
| **Samsung** | Rien de spécial — débogage USB standard suffit |
| **Huawei / Honor** | Options développeur dans *Système et mises à jour* |
| **Oppo / Realme / Vivo** | Options développeur dans *Paramètres supplémentaires* |
