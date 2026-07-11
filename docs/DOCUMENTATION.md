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
- **Historique des activités** : journal local par compte (ajouts, modifs, suppressions,
  associations, dissociations).
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
MainWindow.xaml(.cs)        Coquille : connexion, menu latéral, header (synchro + cloche), navigation, timer 1 s
app.manifest               DPI-aware
assets/app.ico             Icône de l'application

Models/                    Objets de données (POCO + INotifyPropertyChanged)
  Adherent.cs              Adhérent (Id, Nom, Prenom, Telephone, Email, DateInscription, GoogleResourceName…)
  AppSettings.cs           Paramètres persistés (navigateur, compte, liste des synchros…)
  AutoSyncConfig.cs        Une synchro auto (Sheet → libellé) + état runtime (minuteur, %, alertes, trace)
  LabelItem.cs             Libellé (Nom, ResourceName, NombreMembres, IsSelected)
  SheetRecord.cs           Classeur listé (id, nom, url, date, IsSelected)
  PendingPerson.cs         Inscription incomplète (nom/prénom/tél + e-mail brut manquant/mal formé)
  SyncTraceEntry.cs        Personne associée par une synchro (ViaKnown = via l'option « connues »)
  ActivityEntry.cs         Une entrée de l'historique (catégorie, action, date, cible, ancien/nouveau + instantané contact)
  CheckOption.cs           Option cochable générique (utilisée par le select2)

Services/                  Logique métier et accès Google
  AppServices.cs           Conteneur partagé : settings, repos, cache libellés, synchro contacts,
                           moteur multi-synchros, compte courant, historique (LogActivity/LogContactActivity)
  AppPaths.cs              Chemins des dossiers/fichiers (par compte, modèles, mails, activité, Téléchargements)
  GoogleAuth.cs            OAuth2 (récepteur loopback, scopes unifiés, déconnexion)
  GoogleContactsService.cs People API : contacts, libellés, membres, appartenances (dont SetContactMembershipsAsync)
  GoogleSheetsService.cs   Sheets/Drive : création, partage, export CSV/XLSX, renommage, lecture de plage, existence
  GoogleErrors.cs          Traduction des erreurs Google en messages FR
  AdherentRepository.cs    Lecture/écriture adherents.json
  SheetRepository.cs       Lecture/écriture worksheets.json
  ActivityRepository.cs    Lecture/écriture activity.json (historique, enums en texte)
  SettingsService.cs       Lecture/écriture settings.json (+ seed config.json)
  BrowserService.cs        Détection navigateurs, ouverture liens, Gmail compose
  CsvContactImporter.cs    CSV/Excel → adhérents : mapping par lettre, détection d'en-têtes, incomplets, conversions
  ExcelContactImporter.cs  Lecture d'un .xlsx (ClosedXML) → lignes → CsvContactImporter
  CsvContactExporter.cs    Export adhérents → CSV
  EmailValidator.cs        Validation + correction d'e-mails (Suggest, IsValidOrFixable)
  PendingMatcher.cs        Niveau de correspondance (Connue/Doute/Inconnue) d'une inscription incomplète
  ErrorLogger.cs           Journalise les plantages dans log_error.txt
  EmlParser.cs             Analyse d'un .eml (objet + corps)
  MailTemplateStore.cs     Modèles de mail (un JSON par modèle)
  UpdateService.cs         Vérification de nouvelle version (GitHub Releases)

Controls/
  MultiSelectComboBox      « select2 » : recherche, cases (multi) ou liste simple (SingleSelect), focus auto
  SearchBox                Champ de recherche arrondi avec croix d'effacement

Helpers/
  Animations.cs            Transitions (page, barre d'actions)
  ProgressRunner.cs        Fenêtre de progression (X/N ou indéterminée) + résultat de lot
  PhoneFormatter.cs        Mise en forme / validation des téléphones

Converters/
  PhoneConverters.cs       Téléphone formaté + couleur rouge si invalide
  SyncConverters.cs        État synchro → vert/rouge/textes, bool → Visibility, niveau de correspondance
                           → couleur/texte, ActionBrush (couleur d'action d'historique)

Views/                     Écrans (UserControl) et fenêtres (Window)
  … + fenêtres partagées : PickLabelsWindow (select2 multi générique), LabelListWindow (puces libellés),
    SyncWarningWindow (alertes synchro), SyncTraceWindow (trace synchro), PendingMatchWindow (correspondances),
    ContactDetailsWindow (détails figés d'un contact), ConfirmWindow (confirmations stylées)
```

### 2.3 Navigation
`MainWindow` héberge un menu latéral (RadioButtons) et un `ContentControl`. Les pages sont des
`UserControl` instanciés une fois puis affichés par échange de contenu avec animation.
Pages : **Contacts, Libellés, Association, E-mail, Google Sheets, Synchro auto, Historique, Paramètres**.
La page **Inscriptions non finalisées** (`PendingView`) existe aussi mais son entrée de menu est
**masquée** : on y accède via le bouton de la page **Association** (§8bis) ou le lien de la cloche (§11.4).

Chaque page peut implémenter `IActivableView.OnActivated()` (rafraîchissement à l'affichage).

Un **class handler global** (`App.xaml.cs`) désélectionne la cellule de tout DataGrid dès qu'il perd
le focus clavier. Sa remontée d'arbre gère les `ContentElement` (ex. `Hyperlink`) via l'arbre logique
(sinon `VisualTreeHelper.GetParent` lève « n'est pas un objet Visual »).

### 2.4 État partagé (`AppServices`)
Instance unique passée à toutes les vues. Contient :
- `Settings`, `SettingsService`, `Contacts`, `Sheets`, repositories, `Adherents` (collection observable),
- le **compte courant**, le **cache des libellés** (+ événement `LabelsChanged`),
- la collection **`AutoSyncs`** et le **moteur de synchros** (§11),
- la collection **`Pending`** (inscriptions incomplètes, reconstruite à chaque synchro),
- la collection **`Activities`** (historique) + `LogActivity(...)` / `LogContactActivity(...)`.
- `GetLabelsAsync(forceRefresh)` rafraîchit le cache et appelle `ReconcileLabelNames()` : les noms de
  libellés mémorisés dans les synchros et les inscriptions en attente sont réalignés (fix renommage).

### 2.5 Boucle temps réel (timer 1 s)
`MainWindow` fait tourner un unique `DispatcherTimer` (1 s) qui, tant que l'appli est visible :
- affiche/masque la **bannière hors ligne** ;
- appelle `AppServices.TouchSyncs()` → rafraîchit minuteurs / états / pourcentages ;
- si en ligne, appelle `AppServices.RunDueSyncs()` → lance les synchros **complètes** échues (concurrentes) ;
- met à jour l'icône **🔄** (nombre de synchros en marche + pastille jaune d'alerte).

---

## 3. Stockage des données

Racine : `%LOCALAPPDATA%\BadmintonClub\`
```
settings.json                    Paramètres globaux (dont la liste des synchros auto)
log_error.txt                    Journal des plantages (diagnostic)
google_token/                    Jetons OAuth (par « user »)
accounts/<email>/adherents.json  Adhérents du compte
accounts/<email>/worksheets.json Registre des Sheets créés par le compte
accounts/<email>/activity.json   Historique des activités du compte
modeles/                         Modèles de Sheets (Excel/CSV)
mails/                           Modèles de mail (.json : nom, objet, corps)
```
Le compte « par défaut » (avant connexion) conserve les fichiers à la racine (rétro-compat + migration).

---

## 4. Intégration Google (OAuth & scopes)

- **Fichier requis** : `client_secret.json` (identifiants OAuth « Application de bureau »).
- **Autorisation unique partagée** (`GoogleAuth.AllScopes`) : `contacts`, `userinfo.email`,
  `userinfo.profile`, `spreadsheets`, `drive` → un seul écran de consentement, un seul jeton.
- **Récepteur loopback** maison (ShellExecute, `http://127.0.0.1:<port>/authorize/`). Connexion annulable.
- **Mode Test Google** : chaque compte doit être **utilisateur de test** ; jetons ~7 jours.

---

## 5. Connexion / comptes

- **Démarrage** : jeton présent → connexion silencieuse ; sinon → écran de connexion.
- **Se connecter / déconnecter** : gère le choix du compte et l'isolation des données par compte.
- **Isolation** : à chaque connexion les vues sont réinitialisées et les dépôts (adhérents, sheets,
  activité) rebasculés sur le dossier du compte.

---

## 6. Page **Contacts**

Tableau paginé : sélection, Nom, Prénom, **Téléphone** (formaté, rouge si invalide), **E-mail**
(lien Gmail), **Ajouté le** (`DateInscription`), Actions (✏ Modifier / 🏷 Libellés / 🗑 Supprimer).

- **Pagination** : 20 / 50 / 100 par page ; tri par en-tête ; copie de cellule (Ctrl+C).
- **Filtres (barre)** : recherche, **filtre par libellé** (multiselect + « (Sans libellé) »),
  case **« Masquer les contacts sans nom / prénom / tél »**, bouton **🔎 Recherche avancée**.
- **Recherche avancée** (panneau dépliable) :
  - **Filtre par période d'ajout** (Du / Au sur `DateInscription`, bornes incluses, ✕ pour effacer) ;
  - bascule **🔁 Doublons** : n'affiche que les **homonymes** (même nom + prénom présents ≥ 2 fois).
- **Modifier** : formulaire → mise à jour + push Google immédiat (journalisé, ancien/nouveau).
- **Libellés** : modale `ManageLabelsWindow` (cases) → applique l'ensemble voulu **en un seul appel**
  (`SetContactMembershipsAsync`, voir §13) + journalise associations/dissociations.
- **Ajouter / Supprimer** : formulaire / confirmation stylée ; push Google immédiat ; journalisés.
- **Importer** : modale à 2 modes (§12). À la fin, un **message box** résume l'import :
  « Tout s'est bien passé » ou **avertissement** listant les **e-mails en double** et les **personnes
  aux infos incomplètes** (non importées). Ajouts/modifs/associations journalisés (« Import manuel »).
- **Exporter CSV** (bouton vert) : exporte le filtrage affiché (ou la sélection).

---

## 7. Page **Libellés**

Un « libellé » = un **groupe de contacts Google** (`USER_CONTACT_GROUP` uniquement). Créer / Voir /
Renommer / Supprimer (+ suppression groupée). Toute modif rafraîchit le **cache** (`LabelsChanged`).
Création, renommage (ancien→nouveau) et suppression sont **journalisés**.

---

## 8. Page **Association**

Vue centrée sur les **personnes**, avec gestion fine des libellés.

- **Filtre par libellé en multi-sélection** : aucune sélection = **toutes** les personnes ; sinon
  **union** des membres des libellés cochés.
- **🔎 Recherche avancée** (panneau) : trois listes select2 combinées —
  - **A au moins un de** (OU, filtre principal),
  - **A TOUS ces libellés** (ET : présent dans chaque libellé simultanément),
  - **N'a AUCUN de ces libellés** (exclusion).
  Les listes **s'adaptent** : un libellé choisi en exclusion disparaît des listes d'inclusion et
  inversement. Filtrage par e-mail des membres (cache de session `_memberEmailsCache`).
- **Actions par ligne** : **👁 Voir** (fenêtre stylée `LabelListWindow` listant les libellés utilisateur
  de la personne) et **🏷 Gérer** (`PickLabelsWindow` : select2 multi pré-coché → applique l'ensemble
  voulu en un seul appel).
- **Sélection multiple** : barre avec **👥 Associer (N)** et **✂ Dissocier (N)**, chacune ouvrant une
  `PickLabelsWindow` pour choisir les libellés à ajouter/retirer aux personnes cochées.
- **⏳ Inscriptions non finalisées** : actif quand exactement un libellé est sélectionné ; ouvre §8bis pré-filtré.
- Toutes les associations/dissociations sont **journalisées** (instantané du contact figé).

---

## 8bis. Page **Inscriptions non finalisées** (`PendingView`)

**Lecture seule** (l'appli n'écrit jamais dans le Sheet). Liste les personnes d'un Sheet synchronisé
ayant renseigné des infos (nom/prénom/tél) mais dont l'**e-mail est manquant OU au mauvais format non
rattrapable** (une faute rattrapable — virgule→point, espaces — devient un contact normal). Reconstruite
à chaque synchro.

- **Accès** : bouton de la page Association, ou lien de la cloche (§11.4). Entrée de menu masquée.
- **Colonne E-mail** : **« mail non renseigné »** en rouge si vide, sinon l'adresse en **jaune** +
  « (format incorrect) ».
- **Colonne Correspondance** (`PendingMatcher`) : ● Connue (vert) / ● Doute (jaune) / ● Inconnue (rouge).
- **Filtres** : recherche (nom/prénom/tél/e-mail), par libellé, par niveau de correspondance.
- **👁 Correspondances** : modale lecture seule (`PendingMatchWindow`).
- **✔ Valider** (par ligne) et **✔ Valider (N)** (groupé) : retire de la liste, avec **confirmation**
  rappelant de compléter l'info dans le Sheet (sinon réapparition à la synchro suivante). Boutons en logos.

---

## 9. Page **E-mail**

Multiselect des libellés destinataires ; **🗂 Gérer les modèles** (zone de dépôt `.eml`) ;
**✈ Écrire un mail** (vierge ou à partir d'un modèle) → ouvre Gmail en composition.

---

## 10. Page **Google Sheets**

Liste tous les classeurs accessibles. Filtres : recherche + **période** (Du / Au + ✕ pour effacer).
- **➕ Créer un Sheet** (vierge / à partir d'un modèle + partage).
- Par ligne : 🔗 Ouvrir, 📋 Copier, ✏ Renommer (Drive), ⚙ Options, 🗑 Supprimer. Suppression groupée.
- **⚙ Options** : partage, **⬇ Télécharger CSV** (dialogue ouvert sur le dossier **Téléchargements**,
  `AppPaths.DownloadsFolder`), **⭐ Enregistrer comme modèle** (.xlsx dans `modeles`).
- Création / renommage / suppression **journalisés** (catégorie Sheet).

---

## 11. Page **Synchro auto** (multi-synchros)

Plusieurs synchros reliant chacune **un Google Sheet à un libellé cible**.

### 11.1 Règles
- Exécution concurrente coopérative sur le thread UI ; **un libellé = une seule synchro** (unicité).
- Chaque synchro **complète** s'exécute immédiatement à l'activation puis toutes les 5 min.
- **Brouillon** : une synchro peut être enregistrée **incomplète** (champs manquants). Elle n'est
  **pas lançable** tant qu'elle n'a pas nom + lien du Sheet + libellé + colonne e-mail
  (`AutoSyncConfig.IsComplete`) ; sa ligne est affichée en **orange**. `RunDueSyncs`/`StartSync`
  ignorent les synchros incomplètes.
- Une synchro **en cours d'exécution** ne peut pas être modifiée.

### 11.2 Tableau
Colonnes : **Nom · Libellé cible · Lien · État · Minuteur · Suivi · Actions**.
- **Suivi** : bouton **📋 Trace** (bleu, si trace) + bouton **⚠ Alerte** (jaune, si alertes).
- **Actions** : ▶ Démarrer / ⏸ Arrêter, ✏ Modifier, 🗑 Supprimer. Double-clic → modale.

### 11.3 Modale d'ajout/édition (`AutoSyncEditWindow`)
Nom · **Lien du Sheet** (+ bouton **🔎 Vérifier** : teste l'existence via `SheetExistsAsync`, colore le
contour du champ vert/rouge) · **Libellé cible** (select2) · lignes début/fin · colonnes ·
**✨ Remplissage automatique** · **🔍 Tester** · **☑ Associer automatiquement les personnes « connues »** ·
**Activer**.
- À l'enregistrement, un **lien renseigné mais inexistant** bloque la sauvegarde (vérifié en ligne) ;
  un lien vide est autorisé (brouillon).

### 11.4 Icône synchro & cloche (header)
- **🔄** = zone notifications ; **pastille verte** = nombre de synchros en marche ; **pastille jaune ⚠**
  si au moins une synchro active a des inscriptions non finalisées.
- Popup : par synchro, **⟳** (synchro immédiate de cette ligne) + **⏸** (suspendre), un **⚠** et un lien
  **« Voir les inscriptions non finalisées »** (page §8bis pré-filtrée sur son libellé) si elle a des incomplets.
  La flèche du minuteur tourne en continu (animation).

### 11.5 Moteur (`AppServices.ImportConfigAsync`)
- Lit les lignes (`A{début}:Z{fin}`), mappe par lettre, corrige les e-mails rattrapables, **upsert par
  e-mail**, pousse vers Google les vraies différences, **associe** au libellé cible (ajout des manquants)
  et **dissocie** les membres absents du Sheet.
- **Inscriptions incomplètes** : e-mail manquant ou mal formé non rattrapable → `Pending` (§8bis).
- **Option « connues »** : une personne sans e-mail exploitable mais correspondant de façon **certaine**
  (`MatchLevel.Connue` : ≥ 2 champs, un **seul** contact, avec e-mail valide) est associée via ce contact
  et retirée des non finalisés. En cas de doute, elle y reste. **Aucune écriture dans le Sheet.**
- **Doublons d'e-mail** : personnes ayant saisi le même e-mail valide plusieurs fois → associées quand
  même, mais signalées.
- **Alertes** (`SyncWarningWindow`) : bouton ⚠ → liste infos manquantes + doublons, filtre par type,
  lien vers §8bis pré-filtré.
- **Trace** (`SyncTraceWindow`) : personnes associées au libellé lors de la dernière exécution ; lignes
  **jaunes** = passées uniquement grâce à l'option « connues ».
- Ajouts / modifications / associations / dissociations sont **journalisés** (« Synchro auto « Nom » »),
  uniquement sur de vraies actions (pas de spam en régime stable).

---

## 12. Import de contacts (modale, page Contacts)

Deux modes : **fichier Excel/CSV** (dropzone colorée + mapping par colonnes ou détection d'en-têtes) ou
**coller des e-mails**. Commun : libellés cibles, vérification/correction des e-mails, **upsert par e-mail**,
push Google avec progression. La modale expose aussi les **doublons** et **personnes incomplètes** pour le
message de bilan (§6).

---

## 13. Synchronisation Contacts & appartenances

`AppServices.SyncContactsAsync` (au lancement) réconcilie la liste locale avec Google (suppressions,
mises à jour, rapprochement par e-mail, ajouts). **App → Google** immédiat pour ajout/modif/suppression ;
**Google → App** au lancement.

**Appartenances (libellés) — appel atomique.** Pour fixer les libellés d'un contact,
`GoogleContactsService.SetContactMembershipsAsync` remplace **toute** la liste d'appartenances en **un seul**
`updateContact` (au lieu de plusieurs `members.modify` successifs, qui pouvaient perdre une modification —
dernier libellé non dissocié — à cause de la cohérence à terme de l'API). Les groupes **système** sont
conservés et **`contactGroups/myContacts` est toujours garanti** : l'API refuse qu'un contact n'appartienne
à **aucun** groupe (« Contact must always be in at least one contact group »).

---

## 14. Historique des activités (page **Historique**)

Journal local **par compte** (`activity.json`, 3000 entrées max, le plus récent en tête).

- **Actions journalisées** : Ajout, Modification (avec **ancienne/nouvelle valeur**), Suppression,
  Association, Dissociation — depuis les actions manuelles, la modale « Libellés », l'import manuel et
  la synchro auto.
- **Trois tableaux** choisis par **radios** : **Utilisateurs**, **Libellés**, **Sheets**.
- **Filtres** : par **action** (combo), par **période** (Du / Au), et par **cible** (champ texte).
- Colonnes : **Date · Action** (colorée via `ActionBrush`) **· Cible · Détails · Ancienne · Nouvelle valeur**.
- **Détails contact** : bouton **👁** dans la colonne Cible (entrées Utilisateur) → fenêtre stylée
  `ContactDetailsWindow` affichant **Nom / Prénom / Téléphone / E-mail**. Ces infos sont un **instantané
  figé au moment de l'action** (champs `Target*` de `ActivityEntry`) : elles restent exactes même si le
  contact est ensuite modifié ou supprimé. (Les entrées créées avant cette évolution n'ont pas d'instantané.)

---

## 15. Divers

- **Téléphone** : reformaté « 06 12 34 56 78 », rouge si invalide ; comparé « chiffres seuls ».
- **select2** (`MultiSelectComboBox`) : recherche, multi/simple, focus auto ; `SetOptions` ne relève pas
  l'événement de sélection (pas de récursion lors des adaptations de listes).
- **Confirmations** : `ConfirmWindow` stylée pour suppressions/dissociations.
- **Tableaux** : sélection/copie par cellule (Ctrl+C), désélection au clic hors tableau, virtualisation, tri.
- **Navigateur** : seul paramètre de la page **Paramètres**.
- **Mise à jour** : comparaison avec la dernière Release GitHub à la connexion.
- **Journal d'erreurs** : plantages non gérés → `log_error.txt` (via `ErrorLogger`).

---

## 16. Gestion des erreurs Google (courantes)

| Message | Cause | Solution |
|---|---|---|
| `insufficient authentication scopes` | Permissions non toutes accordées | Se déconnecter/reconnecter en **cochant tout** |
| `access_denied` (Accès bloqué) | Compte non testeur | Ajouter le compte en **Utilisateur de test** |
| `Contact must always be in at least one contact group` | Retrait de la dernière appartenance | Géré : `myContacts` toujours garanti (§13) |
| `People/Sheets/Drive API not enabled` | API non activée | Activer l'API dans le projet Google Cloud |
| `client_secret.json introuvable` | Fichier manquant | Placer `client_secret.json` à côté de l'exe |
