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
- **Formulaires Google & réponses** : gestion des Google Forms (créer, configurer, associer un
  libellé, paramétrer les réponses) et **visualisation des répondants** d'un formulaire sous forme
  de contacts, avec **liste d'attente** (par date de réponse) et **validation** en adhérents.
- **Historique des activités** : journal local par compte (ajouts, modifs, suppressions,
  associations, dissociations).
- **Hors ligne** : l'application reste utilisable ; seules les actions Google nécessitent Internet.

---

## 2. Architecture

### 2.1 Pile technique
- **C# / WPF / .NET 8** (`net8.0-windows`), code-behind propre (pas de MVVM lourd, pas de DI).
- **NuGet** : `Google.Apis.Auth`, `Google.Apis.PeopleService.v1`, `Google.Apis.Sheets.v4`,
  `Google.Apis.Drive.v3`, `Google.Apis.Forms.v1`, `ClosedXML` (lecture Excel).
- **Persistance** : fichiers **JSON** (`System.Text.Json`).

### 2.2 Organisation du code
```
BadmintonClub.csproj        Projet, dépendances, icône, version
App.xaml(.cs)               Ressources/styles globaux, convertisseurs, gestion d'exceptions
MainWindow.xaml(.cs)        Coquille : connexion, menu latéral, header (synchro + cloche), navigation, timer 1 s
app.manifest               DPI-aware
assets/app.ico             Icône de l'application

Models/                    Objets de données (POCO + INotifyPropertyChanged)
  Adherent.cs              Adhérent (Id, Nom, Prenom, Telephone, Email, SecondaryEmails, DateInscription, GoogleResourceName…)
  AppSettings.cs           Paramètres persistés (navigateur, compte…)
  LabelItem.cs             Libellé (Nom, ResourceName, NombreMembres, IsSelected)
  SheetRecord.cs           Classeur listé (id, nom, url, date, IsSelected)
  FormRecord.cs            Google Form listé (id, nom, lien, date, libellé associé, FieldMap réponse→colonne, AnswerRules)
  FormTemplate.cs          Modèle de formulaire local (titre + questions typées) réutilisable — voir §10bis
  ActivityEntry.cs         Une entrée de l'historique (catégorie, action, date, cible, ancien/nouveau + instantané contact)
  CheckOption.cs           Option cochable générique (utilisée par le select2)

Services/                  Logique métier et accès Google
  AppServices.cs           Conteneur partagé : settings, repos, cache libellés, synchro contacts,
                           compte courant, historique (LogActivity/LogContactActivity), SyncFormsAsync
  AppPaths.cs              Chemins des dossiers/fichiers (par compte, modèles, mails, activité, formulaires, Téléchargements)
  GoogleAuth.cs            OAuth2 (récepteur loopback, scopes unifiés, vérif des scopes du jeton, déconnexion)
  GoogleContactsService.cs People API : contacts, libellés, membres, appartenances (dont SetContactMembershipsAsync)
  GoogleSheetsService.cs   Sheets/Drive : création, partage, export CSV/XLSX, renommage, lecture de plage, existence
  GoogleFormsService.cs    Forms API + Drive : lister, créer (vierge / depuis modèle local via CreateItem),
                           activer la collecte e-mail vérifié à la création, exporter la structure (modèle),
                           renommer, supprimer, lire questions (type/options) et réponses (e-mail vérifié)
  GoogleErrors.cs          Traduction des erreurs Google en messages FR
  AdherentRepository.cs    Lecture/écriture adherents.json
  SheetRepository.cs       Lecture/écriture worksheets.json
  ActivityRepository.cs    Lecture/écriture activity.json (historique, enums en texte)
  FormRepository.cs        Registre local des Google Forms (forms.json : libellé, mapping, règles de réponses)
  FormTemplateRepository.cs Modèles de formulaire locaux (fichiers JSON dans modeles_forms : lister/charger/enregistrer)
  SettingsService.cs       Lecture/écriture settings.json (+ seed config.json)
  BrowserService.cs        Détection navigateurs, ouverture liens, Gmail compose
  CsvContactImporter.cs    CSV/Excel → adhérents : mapping par lettre, détection d'en-têtes, incomplets, conversions
  ExcelContactImporter.cs  Lecture d'un .xlsx (ClosedXML) → lignes → CsvContactImporter
  CsvContactExporter.cs    Export adhérents → CSV
  EmailValidator.cs        Validation + correction d'e-mails (Suggest, IsValidOrFixable)
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
  SyncConverters.cs        bool → Visibility, ActionBrush (couleur d'action d'historique)

Views/                     Écrans (UserControl) et fenêtres (Window)
  FormsView                Page Google Forms : liste + créer + Réponses/Configuration/Supprimer + bandeau de rappel
  CreateFormWindow         Modale de création d'un Form (nom + vierge / modèle local via liste ou import fichier)
  FormConfigWindow         Configuration d'un Form : renommer, libellé, correspondances champ↔question,
                           règles de réponses (choix unique), ⭐ enregistrer comme modèle
  FormSettingsReminderWindow Rappel illustré (capture réelle) des réglages Forms à activer à la main
  ResponseDetailWindow     Modale : réponses d'un répondant (surlignage jaune des diffs + maj du contact)
  PreinscriptionView       Page Préinscriptions : sélecteur de formulaire → réponses (contacts) + alertes + validation
  … + fenêtres partagées : PickLabelsWindow (select2 multi générique), LabelListWindow (puces libellés),
    ContactDetailsWindow (détails figés d'un contact), ConfirmWindow (confirmations stylées),
    InputDialog (saisie simple)
```

### 2.3 Navigation
`MainWindow` héberge un menu latéral (RadioButtons) et un `ContentControl`. Les pages sont des
`UserControl` instanciés une fois puis affichés par échange de contenu avec animation.
Pages : **Contacts, Libellés, Association, E-mail, Google Sheets, Formulaires, Préinscriptions, Historique, Paramètres**.

> **Formulaires** (`FormulairesView`) est le point d'accès à l'**application web** des formulaires
> d'inscription (projet `bad-web`) : elle affiche l'adresse du site et l'ouvre dans le navigateur
> (`BrowserService.Open`). La page **Google Forms** (`FormsView`, §10bis) est **mise en veille** :
> son entrée de menu `NavForms` est `Visibility="Collapsed"` dans `MainWindow.xaml`. Le code et la vue
> restent en place (la synchro Drive au démarrage continue, car **Préinscriptions** s'appuie sur le
> registre des formulaires) ; pour la réactiver, retirer `Visibility="Collapsed"`.
La page **Préinscriptions** (`PreinscriptionView`, §11) s'ouvre sur un **sélecteur de formulaire** (tableau)
puis affiche les **réponses** du formulaire choisi ; on peut aussi y arriver directement depuis la page
**Google Forms** (bouton **👥** ou double-clic).

Chaque page peut implémenter `IActivableView.OnActivated()` (rafraîchissement à l'affichage).

Un **class handler global** (`App.xaml.cs`) désélectionne la cellule de tout DataGrid dès qu'il perd
le focus clavier. Sa remontée d'arbre gère les `ContentElement` (ex. `Hyperlink`) via l'arbre logique
(sinon `VisualTreeHelper.GetParent` lève « n'est pas un objet Visual »).

### 2.4 État partagé (`AppServices`)
Instance unique passée à toutes les vues. Contient :
- `Settings`, `SettingsService`, `Contacts`, `Sheets`, `Forms`, repositories, `Adherents` (collection observable),
- le **compte courant**, le **cache des libellés** (+ événement `LabelsChanged`),
- la collection **`Activities`** (historique) + `LogActivity(...)` / `LogContactActivity(...)`,
- `GetLabelsAsync(forceRefresh)` (cache libellés + `LabelsChanged`) et `SyncFormsAsync()` (registre Forms
  synchronisé depuis Drive, en préservant libellé/mapping/règles par formulaire).

### 2.5 Boucle temps réel (timer 1 s)
`MainWindow` fait tourner un unique `DispatcherTimer` (1 s) qui affiche/masque la **bannière hors ligne**
quand l'appli est visible.

---

## 3. Stockage des données

Racine : `%LOCALAPPDATA%\BadmintonClub\`
```
settings.json                            Paramètres globaux (navigateur, compte courant)
log_error.txt                            Journal des plantages (diagnostic)
google_token/                            Jetons OAuth (par « user »)
accounts/<email>/adherents.json          Adhérents du compte
accounts/<email>/worksheets.json         Registre des Sheets créés par le compte
accounts/<email>/forms.json              Registre des Google Forms du compte (libellé, mapping, règles)
accounts/<email>/activity.json           Historique des activités du compte
modeles/                                 Modèles de Sheets (Excel/CSV)
modeles_forms/                           Modèles de Google Forms (structure JSON réutilisable)
mails/                                   Modèles de mail (.json : nom, objet, corps)
```
Le compte « par défaut » (avant connexion) conserve les fichiers à la racine (rétro-compat + migration).

---

## 4. Intégration Google (OAuth & scopes)

- **Fichier requis** : `client_secret.json` (identifiants OAuth « Application de bureau »).
- **Autorisation unique partagée** (`GoogleAuth.AllScopes`) : `contacts`, `userinfo.email`,
  `userinfo.profile`, `spreadsheets`, `drive`, `forms.body`, `forms.responses.readonly` → un seul
  écran de consentement, un seul jeton.
- **Vérification des scopes** : après autorisation, `AuthorizeAsync` vérifie que le jeton stocké
  couvre bien les scopes critiques (contacts, spreadsheets, drive, forms). Sinon (jeton ancien ou
  case décochée), il est supprimé et un **consentement complet** est redemandé automatiquement.
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
(lien Gmail), **Mails secondaires** (bouton **✉ N** → message box listant les adresses, « N/A » si aucune),
**Ajouté le** (`DateInscription`), Actions (✏ Modifier / 🏷 Libellés / 🗑 Supprimer).

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
- **📝 Préinscriptions** : ouvre la page **Préinscriptions** (§11 ; choisir un formulaire depuis Google Forms).
- Toutes les associations/dissociations sont **journalisées** (instantané du contact figé).

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

## 10bis. Page **Google Forms**

Calquée sur la page Google Sheets : gère des Google Forms génériques (registre local `forms.json`,
synchronisé depuis Drive via `mimeType='application/vnd.google-apps.form'`). Filtres recherche + période.

- **➕ Créer** (`CreateFormWindow`) : **nom modifiable** + **Vierge** (`GoogleFormsService.CreateBlankFormAsync`)
  ou **À partir d'un modèle** (modèle **local** choisi dans la liste **ou importé depuis un fichier** ; voir « Créer » ci-dessous).
- **Par ligne** : **👥 Réponses** (ouvre la page **Préinscriptions** §11 sur ce formulaire ; idem
  **double-clic**), **⚙ Configuration**, 🗑 Supprimer (Drive). Suppression groupée. Colonnes **Lien du
  formulaire** (**hyperlien** cliquable → édition) et **Libellé** (libellé Contacts associé).
- **À la création** : la **collecte d'e-mail « Vérifiées »** (`emailCollectionType=VERIFIED`) est activée
  **automatiquement** (best-effort, `TryEnableVerifiedEmailAsync`) — les répondants se connectent, ce qui
  fiabilise le regroupement des réponses par personne.
- **Bandeau de rappel** (au-dessus du tableau de la page) : rappelle d'activer **à la main** les deux
  réglages non exposés par l'API — **« Autoriser la modification des réponses »** et **« Limiter à une
  réponse »** ; bouton **🖼 Voir l'explication** → `FormSettingsReminderWindow` (maquette illustrée).
- **⚙ Configuration** (`FormConfigWindow`) : **renommer** le formulaire (Drive + titre interne),
  **associer un libellé** (sélection unique), **associer les réponses aux colonnes du contact**
  (Prénom / Nom / Téléphone / E-mail / **Mails secondaires** → une **question texte** du formulaire, ou
  « (aucune correspondance) » ; seules les questions texte sont proposées ; stocké dans `FormRecord.FieldMap`),
  **paramétrer les réponses des questions à choix unique** — pour chaque option : *Aucun* /
  **Ajouter à la liste d'attente** / **Annuler l'inscription** (stocké dans `FormRecord.AnswerRules`), et
  **⭐ Enregistrer comme modèle** (exporte la structure vers un fichier local, voir ci-dessous).
- **Créer** : **vierge** (`CreateBlankFormAsync`) ou **à partir d'un modèle**. Les modèles sont des
  **fichiers locaux** (`FormTemplate` JSON dans `%LOCALAPPDATA%\BadmintonClub\modeles_forms`, dépôt
  `FormTemplateRepository`) décrivant la structure (titre + questions : texte court/long, choix unique/multiple,
  liste déroulante, date). À la création, on choisit un modèle **dans la liste** (modèles locaux) **ou on
  importe un fichier** (dropzone / Parcourir) ; dans les deux cas l'app **recrée un nouveau Google Form**
  via l'API (`CreateFormFromTemplateAsync`, un `batchUpdate` de `CreateItem`). Nécessite l'**API Forms** activée.

---

## 11. Page **Préinscriptions** (réponses d'un formulaire)

La page s'ouvre sur un **sélecteur de formulaire** : un tableau des formulaires (Nom · Libellé · Créé le ·
bouton **👥 Voir les réponses** ; double-clic aussi). On y accède depuis le menu latéral, la page
**Association** (📝) ou la page **Google Forms** (👥, qui ouvre directement les réponses).

Le **visualiseur des réponses** d'un formulaire affiche en **titre le nom du formulaire** (avec un bouton
**← Formulaires** pour revenir au sélecteur) et les répondants **comme des contacts** (mêmes colonnes) :
**Rang · Nom · Prénom · Téléphone · E-mail · Mails secondaires · Répondu le · Modifié le · Statut**.
Sous le titre : un **filtre de recherche** (comme la page Contacts) + boutons **⏳ Liste d'attente** et **✔ Valider**.

- **Statut** : **En préinscription** (ni en attente, ni annulée), **En attente** (règle liste d'attente) ou
  **Annulée** (règle annulation).
- **Vue par défaut** : uniquement les **préinscrits** (**En préinscription**). Les personnes **En attente**
  n'apparaissent **que** dans **⏳ Liste d'attente** ; la colonne **Rang** n'est visible que dans ce mode.
- **Sélection** : case **« Tout sélectionner »** dans l'**en-tête** du tableau (comme les autres pages).
- **Colonne Alerte** : bouton **⚠** (→ message box listant **toutes** les alertes du répondant) ou **« N/A »**
  s'il n'y en a aucune. Alertes possibles : **absent de mes contacts**, **déjà associé au libellé**,
  **infos différentes** du contact (champs listés), **e-mail au format invalide**.
- **Ligne rouge + bouton ➕ Contacts** : répondant **absent de mes contacts** → bouton pour l'ajouter
  directement aux contacts (crée le contact, push Google, journalisé).
- **Ligne jaune** : répondant présent dans mes contacts mais dont une **info diffère** (nom, prénom ou téléphone).
- **E-mail en rouge** : format d'e-mail invalide (les fautes rattrapables — virgule→point, espaces — sont
  **corrigées automatiquement**).
- **Regroupement** : les soumissions multiples d'une même personne (même e-mail, insensible à la casse) sont
  **fusionnées en une ligne** (la plus récente fait foi).
- **Mails secondaires** : bouton **✉ N** (→ message box) = union du contact existant et de la réponse
  (si la colonne « Mails secondaires » est mappée) ; « N/A » sinon. Repris à la validation.
- **Modifié le** : date de **dernière modification** du formulaire par le répondant (`LastSubmittedTime`
  postérieure à la 1re soumission) ; « N/A » s'il n'a pas modifié sa réponse.
- **👁 Réponses** → détail avec **surlignage jaune** des réponses qui **diffèrent** du contact (affiche la
  valeur actuelle) + bouton **💾 Mettre à jour le contact** pour appliquer les nouvelles infos.

### 11.1 Lecture des réponses
- `GoogleFormsService.GetFormQuestionsAsync` (id + intitulé) puis `ListResponsesAsync` (réponses + e-mail
  vérifié). Les répondants sont **regroupés par e-mail vérifié** (identité « qui a répondu », et **non**
  l'e-mail saisi) ; la **dernière soumission** fait foi, la **1re** donne la place dans la file d'attente.
- Champs **Prénom/Nom/Téléphone/Mails secondaires** extraits via le **mapping** manuel du formulaire
  (`FormRecord.FieldMap`) ; **repli sur l'auto-détection** (`DetectContactField`) uniquement si **aucune**
  correspondance n'a été configurée. L'**E-mail** affiché est l'e-mail vérifié du répondant.
- **👁 Réponses** (par ligne) → `ResponseDetailWindow` : **toutes les réponses** de la personne, question
  par question.

### 11.2 Liste d'attente & validation
- **⏳ Liste d'attente** : bascule qui n'affiche que les répondants dont une réponse déclenche la règle
  « liste d'attente » (§10bis Configuration), **triés par date de réponse croissante** (priorité au plus
  ancien) avec un **rang** (#1, #2…).
- **Colonne Statut** : *En préinscription* / *En attente* / *Annulée*, déduite des règles de réponses du formulaire.
- **✔ Valider (N)** (sélection multiple) : upsert des adhérents **par e-mail**, contact Google créé/mis à
  jour, **association au libellé du formulaire**, journalisation. Les réponses **annulées** sont exclues ;
  nécessite un **libellé associé** (page Google Forms → ⚙ Configuration).
- **Lecture seule** côté Google : aucune écriture dans le formulaire ni ses réponses.

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
  la validation d'une préinscription.
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
| `People/Sheets/Drive/Forms API not enabled` | API non activée | Activer l'API concernée (dont **Google Forms API** pour les préinscriptions) dans le projet Google Cloud |
| `client_secret.json introuvable` | Fichier manquant | Placer `client_secret.json` à côté de l'exe |
