# Documentation technique — Club de Badminton

Application de bureau **Windows (WPF, .NET 8)** de gestion des adhérents d'un club de
badminton, synchronisée avec **Google Contacts**, **Google Sheets** et **Gmail**.

> Voir aussi [DEPLOIEMENT.md](DEPLOIEMENT.md) pour construire/versionner/installer l'appli.

---

## 1. Vue d'ensemble

- **Usage** : solo, un utilisateur, avec possibilité de **plusieurs comptes Google**.
- **Connexion obligatoire** : l'application n'est accessible qu'une fois connecté à un
  compte Google (écran de connexion au démarrage).
- **Données par compte** : chaque compte Google a son propre jeu de données local, isolé.
- **Synchronisation deux sens** avec Google Contacts (création / modification / suppression).
- **Synchronisations automatiques multiples** : plusieurs Google Sheets → plusieurs libellés,
  en parallèle, chacune sur son minuteur.
- **Hors ligne** : l'application reste utilisable ; seules les actions Google nécessitent Internet.

---

## 2. Architecture

### 2.1 Pile technique
- **C# / WPF / .NET 8** (`net8.0-windows`), code-behind propre (pas de MVVM lourd, pas de DI).
- **NuGet** : `Google.Apis.Auth`, `Google.Apis.PeopleService.v1`, `Google.Apis.Sheets.v4`,
  `Google.Apis.Drive.v3`, `ClosedXML` (lecture Excel).
- **Persistance** : fichiers **JSON** (`System.Text.Json`).

### 2.2 Organisation du code
```
BadmintonClub.csproj        Projet, dépendances, icône, version
App.xaml(.cs)               Ressources/styles globaux, convertisseurs, gestion d'exceptions
MainWindow.xaml(.cs)        Coquille : connexion, menu latéral, header (cloche de synchro), navigation, timer 1 s
app.manifest               DPI-aware
assets/app.ico             Icône de l'application

Models/                    Objets de données (POCO + INotifyPropertyChanged)
  Adherent.cs              Adhérent (Id, Nom, Prenom, Telephone, Email, DateInscription,
                           GoogleResourceName, IsSelected)
  AppSettings.cs           Paramètres persistés (navigateur, compte, liste des synchros…)
  AutoSyncConfig.cs        Une synchro auto (Sheet → libellé) + état d'exécution (minuteur, %, en cours)
  LabelItem.cs             Libellé (Nom, ResourceName, NombreMembres, IsSelected)
  SheetRecord.cs           Classeur listé (id, nom, url, date, IsSelected)
  CheckOption.cs           Option cochable générique (utilisée par le select2)

Services/                  Logique métier et accès Google
  AppServices.cs           Conteneur partagé : settings, repos, cache libellés, synchro
                           contacts, moteur multi-synchros, compte courant
  AppPaths.cs              Chemins des dossiers/fichiers (par compte, modèles, mails…)
  GoogleAuth.cs            OAuth2 (récepteur loopback, scopes unifiés, déconnexion)
  GoogleContactsService.cs People API : contacts, libellés, membres, appartenances
  GoogleSheetsService.cs   Sheets/Drive : création, partage, export CSV, lecture de plage
  GoogleErrors.cs          Traduction des erreurs Google en messages FR
  GoogleSyncException.cs   Exception métier
  AdherentRepository.cs    Lecture/écriture adherents.json
  SheetRepository.cs       Lecture/écriture worksheets.json
  SettingsService.cs       Lecture/écriture settings.json (+ seed config.json)
  BrowserService.cs        Détection navigateurs, ouverture liens, Gmail compose
  CsvContactImporter.cs    CSV/Excel → adhérents : lecture, détection d'en-têtes, mapping par
                           lettre de colonne, vérification (« Tester »), conversions lettre↔index
  ExcelContactImporter.cs  Lecture d'un .xlsx (ClosedXML) → lignes → CsvContactImporter
  CsvContactExporter.cs    Export adhérents → CSV
  EmailValidator.cs        Validation + correction d'e-mails
  EmlParser.cs             Analyse d'un .eml (objet + corps)
  MailTemplateStore.cs     Modèles de mail (un JSON par modèle)
  UpdateService.cs         Vérification de nouvelle version (GitHub Releases)

Controls/                  Contrôles réutilisables
  MultiSelectComboBox      « select2 » : recherche, cases à cocher (multi) OU liste simple
                           (mode SingleSelect), focus auto sur la recherche à l'ouverture
  SearchBox                Champ de recherche arrondi avec croix d'effacement

Helpers/
  Animations.cs            Transitions (page, barre d'actions)
  ProgressRunner.cs        Fenêtre de progression (X/N ou indéterminée) + résultat de lot
  PhoneFormatter.cs        Mise en forme / validation des téléphones

Converters/
  PhoneConverters.cs       Téléphone formaté + couleur rouge si invalide
  SyncConverters.cs        État de synchro → vert/rouge, textes « En marche/Arrêté »,
                           « ▶ Démarrer/⏸ Arrêter », bool → Visibility

Views/                     Écrans (UserControl) et fenêtres (Window)
```

### 2.3 Navigation
`MainWindow` héberge un menu latéral (RadioButtons) et un `ContentControl`. Les pages sont des
`UserControl` instanciés une fois puis affichés par échange de contenu avec animation.
Pages : **Contacts, Libellés, Association, E-mail, Google Sheets, Synchro auto, Paramètres**.

Chaque page peut implémenter `IActivableView.OnActivated()` (rafraîchissement à l'affichage).

### 2.4 État partagé (`AppServices`)
Instance unique passée à toutes les vues. Contient :
- `Settings`, `SettingsService`, `Contacts`, `Sheets`, repositories, `Adherents` (collection observable),
- le **compte courant**, le **cache des libellés** (+ événement `LabelsChanged`),
- la collection observable **`AutoSyncs`** et le **moteur de synchros** (voir §11).

### 2.5 Boucle temps réel (timer 1 s)
`MainWindow` fait tourner un unique `DispatcherTimer` (1 s) qui, tant que l'appli est visible :
- affiche/masque la **bannière hors ligne** ;
- appelle `AppServices.TouchSyncs()` → rafraîchit minuteurs / états / pourcentages (bindings) ;
- si en ligne, appelle `AppServices.RunDueSyncs()` → lance les synchros échues (concurrentes) ;
- met à jour la **cloche 🔔** (nombre de synchros en marche).

Il n'y a **plus** de timer 5 min dédié : chaque synchro porte son propre `NextRun` et est
déclenchée par ce tick lorsqu'elle est due.

---

## 3. Stockage des données

Racine : `%LOCALAPPDATA%\BadmintonClub\`
```
settings.json                    Paramètres globaux (dont la liste des synchros auto)
google_token/                    Jetons OAuth (par « user »)
accounts/<email>/adherents.json  Adhérents du compte
accounts/<email>/worksheets.json Registre des Sheets créés par le compte
modeles/                         Modèles de Sheets (Excel/CSV)
mails/                           Modèles de mail (.json : nom, objet, corps)
```
Le compte « par défaut » (avant connexion) conserve les anciens `adherents.json` /
`worksheets.json` à la racine (rétro-compatibilité + migration à la 1re connexion).

**Structure d'un adhérent** :
```json
{ "Id": "guid", "Nom": "", "Prenom": "", "Telephone": "", "Email": "",
  "DateInscription": "2026-01-01T00:00:00", "GoogleResourceName": "people/…" }
```

**Structure d'une synchro auto** (dans `settings.json`, tableau `AutoSyncs`) :
```json
{ "Id": "guid", "Name": "Adultes 2026", "SheetUrl": "https://docs.google.com/…",
  "LabelResourceName": "contactGroups/…", "LabelName": "Adultes",
  "StartRow": 5, "EndRow": 110,
  "ColNom": "B", "ColPrenom": "C", "ColTel": "D", "ColEmail": "E",
  "Enabled": true }
```
Le minuteur, le pourcentage et l'indicateur « en cours » ne sont **pas** sérialisés (état runtime).

---

## 4. Intégration Google (OAuth & scopes)

- **Fichier requis** : `client_secret.json` (identifiants OAuth « Application de bureau »)
  à côté de l'exécutable.
- **Autorisation unique partagée** (`GoogleAuth.AllScopes`, clé `user-badminton`) :
  `contacts`, `userinfo.email`, `userinfo.profile`, `spreadsheets`, `drive`.
  → **un seul écran de consentement**, un seul jeton pour toute l'appli.
- **Récepteur loopback** maison : ouvre le navigateur via ShellExecute (évite la troncature
  d'URL sur `&`), sur `http://127.0.0.1:<port>/authorize/`. Connexion **annulable**.
- **Mode Test Google** : chaque compte doit être ajouté comme **utilisateur de test** dans la
  console Google Cloud ; les jetons expirent ~7 jours (reconnexion périodique).

---

## 5. Connexion / comptes

- **Démarrage** : si un jeton existe → connexion silencieuse ; sinon → **écran de connexion**.
- **Se connecter** : force le choix du compte + consentement complet. Bouton **Annuler** pendant l'attente.
- **Se déconnecter** (menu) : supprime les jetons → retour à l'écran de connexion.
- **Changer de compte** = se déconnecter puis se reconnecter avec un autre compte.
- **Isolation** : à chaque connexion, les vues sont réinitialisées et les données rebasculées
  sur le dossier du compte. Migration des données « par défaut » uniquement au tout premier compte.

---

## 6. Page **Contacts**

Tableau : sélection (case), Nom, Prénom, **Téléphone** (formaté, rouge si invalide), **E-mail**
(lien cliquable → Gmail), Actions.

**Filtres (barre compacte)** :
- **Recherche** (nom / prénom / téléphone / e-mail) avec croix d'effacement.
- **Filtre par libellé** (multiselect) incluant l'option **« (Sans libellé) »**. Croix pour vider.
- Le **compteur** indique « X sur Y » quand un filtre est actif ; message **« Aucun résultat »**
  au centre du tableau si le filtrage ne renvoie rien.

**Actions par ligne** : **✏ Modifier** (vert), **🗑 Supprimer** (rouge), **🏷 Libellés** (bleu).
- *Modifier* : formulaire (prénom/nom obligatoires, e-mail validé, téléphone reformaté) →
  mise à jour + **push Google** immédiat.
- *Supprimer* : confirmation stylée → suppression **locale + Google Contacts**.
- *Libellés* : modal listant les libellés, cochés = appartenances ; **Valider** applique les
  ajouts/retraits d'un coup (libellés issus du **cache**, sans rechargement réseau).

**Actions groupées** : cases à cocher + « tout sélectionner » (sur le filtrage affiché) + barre
rouge **« Supprimer (N) »** animée.

**Ajouter** : formulaire + choix de **libellés à associer** à la création. Push Google.

**Importer** : modal à 2 modes (voir §12 pour le détail des colonnes/dropzone).

**Exporter CSV** (bouton vert « Excel ») : exporte **le filtrage affiché** (ou la sélection cochée).

---

## 7. Page **Libellés**

Un « libellé » = un **groupe de contacts Google**. Tableau : sélection, Libellé, Membres, Actions.

- **➕ Créer un libellé** : saisie du nom (chargement).
- Par ligne : **👁 Voir** (→ page Association du libellé), **✏ Renommer**, **🗑 Supprimer**
  (le contact reste, seul le libellé est retiré).
- **Suppression groupée** + « tout sélectionner » ; **double-clic** → page Association.
- Toute modification rafraîchit le **cache des libellés** (`LabelsChanged`) → mise à jour en direct
  des filtres et listes ailleurs.
- **Tri** : les libellés sont classés par **ordre alphabétique inverse (Z→A)** dans toutes les
  listes déroulantes.

---

## 8. Page **Association**

- **Sélecteur de libellé en select2 à choix unique** (recherche, pas de cases à cocher) +
  bouton **👥 Associer** + **recherche** dans le tableau.
- Tant qu'aucun libellé n'est choisi, le tableau affiche **« 👆 Sélectionnez un libellé… »**
  (évite de croire qu'il n'y a aucune association).
- Tableau des **membres** du libellé : sélection, Nom, Prénom, Téléphone, E-mail (→ Gmail), Action.
- **✂ Dissocier** par ligne (**confirmation** « Dissocier X du libellé « Y » ? ») ou
  **sélection multiple + « ✂ Dissocier (N) »** (contact **non supprimé**, juste retiré du libellé).
- **👥 Associer** : modal (adhérents **non déjà associés** au libellé) → ajoute les cochés.
- Sélection **isolée** de la page Contacts (wrapper interne `MemberRow`).

---

## 9. Page **E-mail**

- **Multiselect des libellés destinataires**.
- **🗂 Gérer les modèles** : modal listant les modèles (Modifier / Supprimer) + **➕ Ajouter**.
  - **Éditeur de modèle** : **zone de dépôt `.eml`** (glisser un e-mail téléchargé → objet + corps
    extraits — RFC 2047, multipart, quoted-printable, base64) + champs éditables + **Enregistrer**.
    La dropzone passe **au vert** si le fichier est bien lu, **au rouge** si le format est refusé.
  - Modèles stockés dans `…\mails`.
- **✈ Écrire un mail** : modal de choix **« Mail vierge »** ou **« À partir d'un modèle enregistré »**.
  Le sélecteur de modèle est un **select2 à choix unique** qui n'apparaît que si l'option modèle est
  cochée. L'appli récupère l'**union** des membres des libellés sélectionnés et ouvre **Gmail** en
  composition (destinataires + objet/corps du modèle le cas échéant).

---

## 10. Page **Google Sheets**

Liste **tous les classeurs accessibles** au compte (les tiens **et** ceux partagés avec toi).

- **Filtres** : recherche par nom (croix) + **période** (DatePickers **Du / Au** stylés).
- **➕ Créer un Sheet** : nom + **radios** *Classeur vierge* / *À partir d'un modèle* (glisser un
  Excel/CSV — dropzone colorée vert/rouge selon le format ; ouvre par défaut le dossier `modeles`)
  + **⚙ Paramètres de partage** (accessible par lien + rôle Lecteur/Commentateur/Éditeur).
  Le clone = téléversement + conversion Drive (mise en forme préservée).
- Par ligne : **🔗 Ouvrir**, **📋 Copier le lien**, **⚙ Options** (partage, **⬇ Télécharger CSV**,
  **⭐ Enregistrer comme modèle**), **🗑 Supprimer**.
- **Sélection multiple + suppression groupée**.

> La configuration des imports automatiques n'est **plus** ici : elle a sa propre page (§11).

---

## 11. Page **Synchro auto** (multi-synchros)

Remplace l'ancien import auto unique. Permet **plusieurs synchros en parallèle**, chacune reliant
**un Google Sheet à un libellé cible**.

### 11.1 Règles
- Plusieurs synchros peuvent tourner **en même temps** (exécution concurrente et coopérative sur le
  thread UI, aux points `await`).
- **Un libellé ne peut être ciblé que par une seule synchro** (unicité validée à l'enregistrement,
  `AppServices.IsLabelInUse`).
- Chaque synchro s'exécute **immédiatement** quand on l'active, puis **toutes les 5 minutes**
  (`AutoSyncInterval`).

### 11.2 Tableau
Colonnes : **Nom · Libellé cible · Lien (cliquable) · État · Minuteur · Actions**.
- **État** : ● **vert « En marche »** ou ● **rouge « Arrêté »** ; pendant un import, **⟳ animé + %**.
- **Minuteur** : compte à rebours `MM:SS` avant la prochaine exécution (ou « en cours… »).
- **Actions** : **▶ Démarrer / ⏸ Arrêter**, **✏ Modifier**, **🗑 Supprimer**.
- **Double-clic** sur une ligne → modale de modification.
- **Recherche par nom** en haut de la page (filtre live).

### 11.3 Modale d'ajout/édition (`AutoSyncEditWindow`)
Mélange de l'ancien écran de réglages et de l'ancienne modale d'import :
Nom · **Lien du Sheet** · **Libellé cible (select2 à choix unique)** · **lignes début/fin** ·
**colonnes** (Nom/Prénom/Tél/E-mail) · **✨ Remplissage automatique** · **🔍 Tester** · **Activer**.
- **Remplissage automatique** : lit l'en-tête du Sheet et remplit les lettres de colonnes
  (vert = 4 trouvées, orange = partiel, rouge = aucune).
- **Tester** : lit les lignes avec les colonnes indiquées et affiche un **verdict ✔ vert / ✘ rouge**
  + un exemple réellement lu.

### 11.4 Cloche de notification (header)
Bouton **🔔** avec **pastille verte = nombre de synchros en marche**. Au clic, un popup liste les
synchros actives (**nom + minuteur + ⟳ + %**) avec un bouton **Suspendre** par ligne (la synchro
disparaît alors de la liste).

### 11.5 Moteur (`AppServices`)
- `AutoSyncs` (ObservableCollection) chargée depuis/écrite dans `settings.json`.
- `RunDueSyncs()` (appelé chaque seconde si en ligne) lance `RunSyncNowAsync(config)` pour chaque
  synchro **activée, non en cours, échue**. Un garde `IsImporting` empêche tout double lancement.
- `ImportConfigAsync(config)` : lit les lignes (`A{début}:Z{fin}`), mappe les colonnes par lettre,
  fait l'**upsert par e-mail**, pousse vers Google **uniquement les vraies différences** (comparaison
  au contact Google réel), associe au **libellé cible** (ajout des manquants) et **dissocie** les
  membres absents du fichier. La **progression** (0→100 %) est publiée pendant la poussée Google.

### 11.6 Lecture du Sheet & colonnes (partagé)
- La plage lue est reconstruite en `A{début}:Z{fin}` : on lit **toutes les colonnes** et on prend
  chaque champ à sa **lettre absolue** → les colonnes **non adjacentes** fonctionnent.
- `CsvContactImporter` fournit : lecture, `DetectColumns` (repère l'en-tête), `BuildFromColumns`
  (mapping par lettre, ne garde que les lignes dont la colonne e-mail contient un `@`),
  `CheckColumns`/`BuildCheckMessage` (le « Tester »), `SliceRows`, et les conversions
  **lettre ↔ index** de colonne.

---

## 12. Import de contacts (modale, page Contacts)

Deux modes, choisis par radio :
- **Depuis un fichier (Excel/CSV)** : glisser-déposer ou parcourir.
  - La **dropzone se colore** : neutre + **barre de chargement** pendant la lecture (en tâche de
    fond), **vert + nom du fichier** si valide, **rouge** si format refusé/illisible.
  - Un panneau **« lignes lues » + « colonnes »** apparaît une fois le fichier chargé, avec
    **✨ Remplissage automatique** (pré-rempli au dépôt) et **🔍 Tester** (verdict vert/rouge).
  - À l'import : si des colonnes sont renseignées, mapping par lettre sur la plage de lignes ;
    sinon repli sur la **détection automatique par en-têtes**.
- **Coller des e-mails** : une adresse par ligne.

Commun : **libellés cibles** (multiselect) ; **vérification des e-mails** (adresses douteuses listées
avec **correction proposée**) ; **upsert par e-mail** ; push Google avec barre de progression.

---

## 13. Synchronisation Contacts (deux sens, au lancement)

`AppServices.SyncContactsAsync` réconcilie la liste locale avec Google :
- contact lié absent en ligne → **supprimé localement** (suppression Gmail répercutée) ;
- contact lié présent → **champs mis à jour** depuis Google ;
- contact local non lié → **rapproché par e-mail**, sinon **poussé** vers Google ;
- contact Google non présent localement → **ajouté**.

**App → Google immédiat** : ajout, modification (✏) et suppression (🗑) se répercutent tout de suite.
**Google → App** : au **lancement** (relancer l'appli pour voir un changement fait dans Gmail).

**Hors ligne** : bannière d'avertissement dans le header ; les synchros auto sont **en pause**
jusqu'au retour d'Internet.

---

## 14. Divers

- **Téléphone** : reformaté « 06 12 34 56 78 » (0 en tête, espaces), **rouge** si invalide malgré la
  correction (vide = ignoré) ; comparé « chiffres seuls » pour éviter les faux écarts de format.
- **select2** (`MultiSelectComboBox`) : recherche, mode multi (cases) ou **single** (liste simple),
  message **« Aucun résultat »** en recherche vide, **focus automatique** sur la recherche à l'ouverture.
- **Confirmations** : boîte de dialogue **stylée** (icône, message, boutons colorés) pour toutes les
  suppressions/dissociations.
- **Navigateur** : seul paramètre de la page **Paramètres** — navigateur pour ouvrir Sheets/Gmail/connexion.
- **Mise à jour** : à la connexion, comparaison avec la dernière **Release GitHub** ; proposition de télécharger.
- **Version** affichée dans le menu latéral et sur l'écran de connexion.

---

## 15. Gestion des erreurs Google (courantes)

| Message | Cause | Solution |
|---|---|---|
| `insufficient authentication scopes` | Permissions non toutes accordées | Se déconnecter/reconnecter en **cochant tout** |
| `access_denied` (Accès bloqué) | Compte non testeur | Ajouter le compte en **Utilisateur de test** (console Google Cloud) |
| `People/Sheets/Drive API not enabled` | API non activée | Activer l'API dans le projet Google Cloud |
| `client_secret.json introuvable` | Fichier manquant | Placer `client_secret.json` à côté de l'exe |
