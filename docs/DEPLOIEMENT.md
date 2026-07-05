# Déploiement & mises à jour — Club de Badminton

Guide complet pour **construire, versionner, publier** l'application et la **mettre à jour**
(sur ton PC ou sur un autre PC).

Dépôt GitHub : **https://github.com/MathisBelin/club_badminton**

---

## 1. Prérequis (poste de développement)

| Outil | Rôle | Lien |
|---|---|---|
| **.NET 8 SDK** | Compiler / publier | https://dotnet.microsoft.com/download/dotnet/8.0 |
| **Git** | Versionner / pousser | https://git-scm.com |
| **Inno Setup 6** | Générer l'installeur `setup.exe` | https://jrsoftware.org/isdl.php |
| **client_secret.json** | Identifiants OAuth (non versionné) | Console Google Cloud |

> Inno Setup s'installe aussi via : `winget install JRSoftware.InnoSetup`.

---

## 2. Récupérer / lancer le projet

```powershell
git clone https://github.com/MathisBelin/club_badminton.git
cd club_badminton
dotnet run                 # compile et lance en mode debug
```
Placer **`client_secret.json`** à la racine (copié à côté de l'exe au build) — voir
[CONFIGURATION.md](../CONFIGURATION.md).

---

## 3. Versionner et pousser sur GitHub

> ⚠️ **Ne pousser que sur demande explicite.** Committer en local librement.

```powershell
git add -A
git commit -m "Description claire du changement"
git push                   # uniquement quand c'est validé
```

Ce qui **n'est jamais** versionné (`.gitignore`) : `bin/`, `obj/`, `dist/`,
`client_secret.json`, `config.json`, données locales.

---

## 4. Construire une nouvelle version (exe + installeur)

Un seul script fait tout :

```powershell
# incrémente la version + publie l'exe autonome + génère l'installeur
powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 1.1.0
```
Il produit :
- `bin\Release\net8.0-windows\win-x64\publish\BadmintonClub.exe` (exe **autonome**, ~70 Mo,
  sans besoin d'installer .NET sur le PC cible) ;
- `dist\ClubBadminton-Setup-1.1.0.exe` (**installeur** avec raccourcis + désinstallateur).

Sans `-Version`, il reconstruit avec la version actuelle. Le numéro de version est mis à jour
dans le `.csproj` **et** le script Inno, et s'affiche dans l'application.

Étapes manuelles équivalentes :
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
ISCC.exe installer\ClubBadminton.iss
```

---

## 5. Installer sur un PC

### 5.1 Ce PC (rapide, sans admin)
```powershell
powershell -ExecutionPolicy Bypass -File install.ps1            # installer
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall # désinstaller
```
Crée les raccourcis **Menu Démarrer + Bureau** ; installe dans
`%LOCALAPPDATA%\Programs\ClubBadminton`.

### 5.2 Un autre PC (installeur)
1. Copier **`dist\ClubBadminton-Setup-<version>.exe`** sur l'autre PC (clé USB, Drive…).
2. Double-cliquer → suivant → terminé (SmartScreen : « Informations complémentaires » →
   « Exécuter quand même », l'appli n'étant pas signée).
3. Au 1er lancement : **se connecter avec Google**.

> Le compte Google utilisé sur l'autre PC doit être **utilisateur de test** du projet Google
> Cloud (sinon « Accès bloqué »). Ajout : console → *OAuth consent screen* → *Test users*.

---

## 6. Mettre à jour l'application

### 6.1 Circuit standard
1. Modifier le code, committer (et pousser si validé).
2. `build-release.ps1 -Version X.Y.Z` → nouveau `setup.exe`.
3. **Réinstaller** :
   - ce PC : relancer `install.ps1` **ou** le nouveau `setup.exe` ;
   - autre PC : lancer le nouveau `setup.exe` — il **met à jour par-dessus** (même `AppId`),
     raccourcis conservés.

> **Les données du club sont préservées** (elles sont dans `%LOCALAPPDATA%\BadmintonClub`,
> séparées de l'application). Pas besoin de désinstaller avant.

### 6.2 Notification automatique de mise à jour (optionnel)
À la connexion, l'appli compare sa version à la dernière **Release GitHub** et propose le
téléchargement. Pour l'activer, publier chaque version en **Release** :
1. `build-release.ps1 -Version X.Y.Z`.
2. GitHub → **Releases** → *Draft a new release* → **Tag** `vX.Y.Z` → **joindre**
   `dist\ClubBadminton-Setup-X.Y.Z.exe` → publier.
3. Les postes en version antérieure proposeront la mise à jour au prochain lancement.

> ⚠️ **Sécurité** : l'installeur généré **inclut `client_secret.json`** s'il est présent dans
> `publish`. Pour une **Release publique**, retirer `client_secret.json` du dossier `publish`
> avant de compiler l'installeur (chaque poste mettra le sien), **ou** garder le dépôt/la release privés.

---

## 7. Rappels sécurité

- Ne jamais committer `client_secret.json` (déjà ignoré). Le fichier d'exemple
  `client_secret.example.json` ne doit contenir que des valeurs factices.
- En cas de fuite du secret OAuth : le **réinitialiser** dans la console Google Cloud
  (Identifiants → client OAuth → *Réinitialiser le secret*) puis mettre à jour le
  `client_secret.json` local.
- Mode **Test** Google : jetons valables ~7 jours (reconnexion périodique). Pour un usage large,
  publier l'application en production (procédure de vérification Google requise pour les scopes sensibles).

---

## 8. Récapitulatif express

| Besoin | Commande |
|---|---|
| Lancer en dev | `dotnet run` |
| Committer | `git add -A && git commit -m "…"` |
| Pousser (sur demande) | `git push` |
| Nouvelle version + installeur | `build-release.ps1 -Version X.Y.Z` |
| Installer/MàJ ce PC | `install.ps1` ou `dist\ClubBadminton-Setup-*.exe` |
| Installer/MàJ autre PC | lancer `ClubBadminton-Setup-*.exe` |
