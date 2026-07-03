# Club de Badminton — Gestion des adhérents

Application de bureau Windows (WPF / .NET 8) pour gérer les adhérents d'un club de badminton, en usage solo.

## Fonctionnalités

- **Adhérents** : liste filtrable, ajout / modification / suppression, stockage JSON local.
- **Google Contacts** : ajout/retrait automatique des adhérents à une étiquette (groupe de contacts) Google, via OAuth2 + People API.
- **Google Sheets** :
  - bouton **Ouvrir Google Sheets** ouvrant le tableur configuré dans le navigateur ;
  - bouton **Créer un Sheet** qui crée un classeur vierge, le partage en « tout le monde avec le lien = Éditeur », copie le lien dans le presse-papiers, l'ouvre et enregistre son URL.
- **Paramètres** : étiquette Gmail cible, URL du Google Sheet, chemin du fichier JSON, activation de la synchro.

## Prérequis

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Lancer l'application

```sh
dotnet restore
dotnet build
dotnet run
```

Ou ouvrez `BadmintonClub.csproj` dans Visual Studio 2022.

## Configuration de la synchronisation Google (optionnelle)

L'application fonctionne **entièrement hors ligne** ; la synchro Google est facultative.

Pour l'activer :

1. Sur [Google Cloud Console](https://console.cloud.google.com/), créez un projet.
2. Activez **People API**, **Google Sheets API** et **Google Drive API** (Bibliothèque d'API).
3. Configurez l'**écran de consentement OAuth** (type « Externe », ajoutez votre compte en utilisateur de test).
4. Créez des **identifiants OAuth → ID client OAuth → Application de bureau**.
5. Téléchargez le fichier JSON et renommez-le **`client_secret.json`**.
6. Placez `client_secret.json` **à côté de l'exécutable** (`bin\Debug\net8.0-windows\`) ou à la racine du projet (il est copié automatiquement au build s'il est présent).
7. Dans **Paramètres**, cochez « Synchroniser automatiquement avec Google Contacts » et renseignez le nom de l'étiquette.

Au premier ajout d'adhérent, une fenêtre du navigateur s'ouvre pour autoriser l'accès. Le jeton est ensuite mémorisé.

> Périmètres OAuth utilisés :
> - Contacts : `https://www.googleapis.com/auth/contacts`
> - Sheets : ` `
> - Drive (créer/partager, et lister/supprimer tous vos Sheets) : `https://www.googleapis.com/auth/drive`
>
> Contacts et Sheets ont des autorisations indépendantes : la première utilisation de chaque
> fonctionnalité ouvre sa propre fenêtre de consentement.

## Emplacement des données

- Paramètres et adhérents : `%LOCALAPPDATA%\BadmintonClub\`
  - `settings.json`
  - `adherents.json` (chemin personnalisable dans les Paramètres)
  - `google_token\` (jeton OAuth)

## Structure du JSON adhérent

```json
{
  "Id": "guid",
  "Nom": "string",
  "Prenom": "string",
  "Telephone": "string",
  "Email": "string",
  "DateInscription": "2026-06-24T10:00:00"
}
```

## Notes

- Les versions des paquets Google utilisent une version flottante `1.*` (dernière 1.x). Pour verrouiller un build reproductible, fixez une version précise dans `BadmintonClub.csproj`.
- Pas de base SQL, pas de conteneur DI : architecture volontairement simple (code-behind propre).
