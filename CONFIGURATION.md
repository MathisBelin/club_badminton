# Configuration — que dois-je renseigner ?

Réponse courte : **une seule chose est obligatoire**, et uniquement si tu veux les
fonctionnalités Google (Contacts + création de Sheets). Pour la gestion des adhérents
en local, **rien à configurer**.

---

## ✅ Récapitulatif

| Élément | Obligatoire ? | Où | À quoi ça sert |
|---|---|---|---|
| `client_secret.json` | **Oui** (pour Google) | À côté de l'exe | Identifiants OAuth2 Google. **Toi seul peux le fournir** (téléchargé depuis Google Cloud). |
| `config.json` | Non (optionnel) | À côté de l'exe | Pré-remplir les paramètres au 1er lancement. Aussi modifiable dans l'appli. |
| Paramètres in-app | Non | Écran « Paramètres » | Étiquette Gmail, URL du Sheet, chemin JSON, activation synchro. |

> Sans `client_secret.json`, l'application **fonctionne quand même** : seules les
> actions Google affichent un message d'erreur explicite. Tout le reste (adhérents,
> recherche, JSON local) marche hors ligne.

---

## 1. `client_secret.json` — le SEUL fichier que tu dois vraiment fournir

C'est le fichier d'identifiants OAuth2. Personne ne peut le générer à ta place : il
provient de **ton** projet Google Cloud.

1. Va sur <https://console.cloud.google.com/> → crée (ou choisis) un projet.
2. **Bibliothèque d'API** → active : **People API**, **Google Sheets API**, **Google Drive API**.
3. **Écran de consentement OAuth** → type « Externe » → ajoute ton compte Google en
   « Utilisateur de test ».
4. **Identifiants** → *Créer des identifiants* → *ID client OAuth* → type
   **Application de bureau**.
5. **Télécharge le JSON**, renomme-le **`client_secret.json`**, place-le **à côté de
   l'exécutable** (`bin\Debug\net8.0-windows\`) ou à la racine du projet (il est copié
   automatiquement au build).

Le fichier [`client_secret.example.json`](client_secret.example.json) montre la
structure attendue (valeurs à remplacer par les tiennes).

> ⚠️ Ne committe jamais ce fichier (il est déjà dans `.gitignore`).

---

## 2. `config.json` — optionnel, pour pré-remplir les paramètres

Tu n'es **pas** obligé de l'utiliser : tu peux tout régler dans l'écran **Paramètres**.
Mais si tu veux livrer l'appli déjà configurée, copie
[`config.example.json`](config.example.json) en **`config.json`** (à côté de l'exe) et
renseigne :

```json
{
  "GmailLabel": "Club Badminton",        // nom de l'étiquette / groupe de contacts Google
  "GoogleSheetUrl": "",                   // laisse vide : rempli auto quand tu cliques « Créer un Sheet »
  "AdherentsJsonPath": "",               // vide = %LOCALAPPDATA%\BadmintonClub\adherents.json
  "SyncGoogleEnabled": false             // true pour synchroniser les contacts à l'ajout/suppression
}
```

Ce fichier n'est lu **qu'au tout premier lancement** (tant que `settings.json` n'existe
pas encore). Ensuite, les réglages vivent dans
`%LOCALAPPDATA%\BadmintonClub\settings.json` et se modifient via l'appli.

Il ne contient **aucun secret**.

---

## 3. Rien à faire pour le reste

- Les **adhérents** sont stockés automatiquement dans
  `%LOCALAPPDATA%\BadmintonClub\adherents.json`.
- Le **jeton Google** (après la 1re autorisation dans le navigateur) est mémorisé dans
  `%LOCALAPPDATA%\BadmintonClub\google_token\`.

Tu n'as jamais à éditer ces fichiers à la main.
