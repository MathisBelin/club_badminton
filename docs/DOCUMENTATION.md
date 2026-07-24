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
- **Formulaires d'inscription & réponses** : les formulaires sont créés et configurés dans
  l'**application web** (projet `bad-web`) ; le desktop les **lit** via son API d'intégration et
  affiche les répondants **sous forme de contacts**, avec **liste d'attente** (par date de réponse),
  comparaison avec les contacts existants et **validation** en adhérents (§11).
  L'ancienne page **Google Forms** est conservée mais **en veille** (§10bis).
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
  WebFormsService.cs       Formulaires d'inscription de l'APPLICATION WEB (API d'intégration, x-api-key) :
                           liste par compte, questions (+ FieldMap/AnswerRules déduits), réponses — voir §11
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
  ResponseDetailWindow     Modale : réponses d'un répondant (diffs en couleur bleu/ambre + maj du contact)
  AlertWindow              Fenêtre d'alerte stylée d'un répondant (liste + « 👁 Voir les changements »)
  EmailListWindow          Fenêtre stylée : liste des e-mails secondaires d'une personne (lecture seule)
  ValidateChangesWindow    Choix groupé maj/ignorer des infos différentes à la validation
  PreinscriptionView       Page Préinscriptions : sélecteur de formulaire → réponses (contacts) + alertes + validation + page Inscrits
  … + fenêtres partagées : PickLabelsWindow (select2 multi générique), LabelListWindow (puces libellés),
    ContactDetailsWindow (détails figés d'un contact), ConfirmWindow (confirmations stylées),
    InputDialog (saisie simple)
```

### 2.3 Navigation
`MainWindow` héberge un menu latéral (RadioButtons) et un `ContentControl`. Les pages sont des
`UserControl` instanciés une fois puis affichés par échange de contenu avec animation.
Pages : **Contacts, Libellés, Association, E-mail, Google Sheets, Formulaires d'inscription, Historique, Paramètres**.

> La page **Formulaires d'inscription** (`PreinscriptionView`, §11 — ex-« Préinscriptions ») est le
> point d'entrée unique : elle liste les formulaires de l'**application web** (projet `bad-web`) créés
> par le compte connecté, ouvre le site dans le navigateur (bouton **🌐**) et affiche les **réponses**
> du formulaire choisi. L'ancienne page « Formulaires » (`FormulairesView`, simple lien vers le site) a
> été **supprimée** et fusionnée dedans.
> La page **Google Forms** (`FormsView`, §10bis) est **mise en veille** : son entrée de menu `NavForms`
> est `Visibility="Collapsed"` dans `MainWindow.xaml` et sa **synchro Drive au démarrage est retirée**
> (`forms.json` sert désormais de registre local des formulaires WEB : libellé associé). Pour la
> réactiver, retirer `Visibility="Collapsed"` — attention, sa synchro écrase `forms.json`.

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
settings.json                            Paramètres globaux (navigateur, compte courant,
                                         adresse + clé d'API de l'application web des formulaires)
log_error.txt                            Journal des plantages (diagnostic)
google_token/                            Jetons OAuth (par « user »)
accounts/<email>/adherents.json          Adhérents du compte
accounts/<email>/worksheets.json         Registre des Sheets créés par le compte
accounts/<email>/forms.json              Registre local des formulaires WEB du compte : identifiant,
                                         nom, dates + **libellé Contacts associé** (le mapping des
                                         champs et les règles de réponses viennent désormais du site)
accounts/<email>/form_states.json        Décisions locales par formulaire : inscriptions validées
                                         (historique) et statuts forcés à la main
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
(lien Gmail), **Mails secondaires** (bouton **✉ N** → **fenêtre stylée** `EmailListWindow` listant les
adresses, « N/A » si aucune), **Ajouté le** (`DateInscription`), Actions (✏ Modifier / 🏷 Libellés / 🗑 Supprimer).

- **Pagination** : 20 / 50 / 100 par page ; tri par en-tête ; copie de cellule (Ctrl+C).
- **Filtres (barre)** : recherche, **filtre par libellé** (multiselect + « (Sans libellé) »),
  case **« Masquer les contacts sans nom / prénom / tél »**, bouton **🔎 Recherche avancée**.
- **Recherche avancée** (panneau dépliable) :
  - **Filtre par période d'ajout** (Du / Au sur `DateInscription`, bornes incluses, ✕ pour effacer) ;
  - bascule **🔁 Doublons** : n'affiche que les **homonymes** (même nom + prénom présents ≥ 2 fois).
- **Ajouter / Modifier** (`AdherentEditWindow`) : formulaire **Nom** (en premier) · **Prénom** ·
  **Téléphone** · **E-mail** · **Mails secondaires** — champ **par adresse** façon Google Contacts
  (un champ, un bouton **＋ Ajouter une adresse** qui ajoute un champ, **✕** pour retirer). Le bouton
  d'ajout est **désactivé tant qu'une adresse en cours est vide ou mal formée**. Adresses validées,
  l'e-mail principal en est exclu, poussées vers Google comme adresses supplémentaires →
  mise à jour + push Google immédiat (journalisé, ancien/nouveau).
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

## 11. Page **Formulaires d'inscription** (réponses d'un formulaire)

> **Source des données : l'application web** (`bad-web`), via son API d'intégration en lecture seule
> (`WebFormsService`, en-tête `x-api-key`). Les Google Forms ne sont plus utilisés. Réglages dans
> **Paramètres** : *adresse du site* (vide = `https://bad-web-rho.vercel.app`) et *clé d'API*
> (= `INTEGRATION_API_KEY` du site), stockés dans `settings.json` (jamais versionné).
>
> - Le **sélecteur** liste les formulaires **créés par le compte Google connecté** (filtre `owner`),
>   avec **🔄 Actualiser** et **🌐 Ouvrir l'application web**.
> - La **correspondance champ contact ↔ question** (`FieldMap`) et les **règles de liste d'attente**
>   (`AnswerRules`) ne se configurent plus dans le desktop : elles sont **déduites du formulaire web**
>   (champ de contact associé à la question, option marquée « Ajouter à la liste d'attente »).
> - Le **libellé Contacts** du formulaire est désormais choisi **sur le site, à la création**
>   (obligatoire, un seul) et lu ici : la colonne *Libellé* et ✔ Valider s'en servent. Le bouton local
>   « 🏷 Libellé » a été retiré ; `forms.json` ne sert plus que de repli pour les formulaires créés
>   avant cette évolution.
> - Les **libellés** ne sont plus envoyés au site : celui-ci lit **lui-même** Google Contacts
>   (People API, lecture seule) après une autorisation donnée par l'admin sur le site
>   (« Connecter Google Contacts » puis « 🔄 Actualiser »). Rien à faire ici.
> - Tout le reste (alertes, comparaison avec les contacts, ⏳ liste d'attente, ✔ validation,
>   👁 Réponses avec surlignage des différences) est **inchangé** : seule la source a changé.

La page s'ouvre sur un **sélecteur de formulaire** : un **filtre de recherche** (nom ou libellé, avec le
compte « N sur M ») puis un tableau des formulaires (Nom · Libellé · Créé le ·
bouton **👥 Voir les réponses** ; double-clic aussi). On y accède depuis le menu latéral, la page
**Association** (📝) ou la page **Google Forms** (👥, qui ouvre directement les réponses).

Le **visualiseur des réponses** d'un formulaire affiche en **titre le nom du formulaire** (avec un bouton
**← Formulaires** pour revenir au sélecteur) et les répondants **comme des contacts** (mêmes colonnes) :
**Rang · Nom · Prénom · Téléphone · E-mail · Mails secondaires · Répondu le · Modifié le · Statut**.
Sous le titre : un **filtre de recherche** (comme la page Contacts) + boutons **✅ Inscrits**,
**⬇ Exporter CSV** et **⏳ Liste d'attente**. Les actions sur les personnes sont dans la **barre de
sélection**, qui n'apparaît qu'une fois des lignes cochées (voir plus bas).

> **Décisions locales** (fichier `form_states.json` du compte, par identifiant de formulaire) :
> inscriptions **validées** et **statuts forcés** à la main — rien de cela n'est envoyé au site.
> Le desktop n'écrit sur le site que dans **deux** cas : la **correction d'une réponse**
> (bouton « ignorer ») et la **suppression d'une préinscription** (🗑).

- **Statut** : **En préinscription** (ni en attente, ni annulée), **En attente** (règle liste d'attente) ou
  **Annulée** (règle annulation).
- **Vue par défaut** : uniquement les **préinscrits** (**En préinscription**). Les personnes **En attente**
  n'apparaissent **que** dans **⏳ Liste d'attente** ; la colonne **Rang** n'est visible que dans ce mode.
- **Sélection** : case **« Tout sélectionner »** dans l'**en-tête** du tableau (comme les autres pages).
- **Colonne Alerte** : bouton **⚠** → **fenêtre d'alerte stylée** (`AlertWindow`) listant **toutes** les
  alertes du répondant, avec un bouton **👁 Voir les changements** qui ouvre le **détail de la réponse**
  (§ 👁 Réponses) ; **« N/A »** s'il n'y a aucune alerte. Alertes possibles : **absent de mes contacts**, **déjà associé au libellé**,
  **associé au libellé mais absent de mes contacts** (incohérence), **infos différentes** du contact
  (champs listés), **e-mail au format invalide**.
- **Incohérence « associé au libellé mais pas dans mes contacts »** : quand un répondant est **membre
  du libellé** du formulaire (côté Google) mais **absent de mes contacts** locaux, la ligne est
  surlignée en **jaune** (au lieu du rouge « pas dans mes contacts ») et une **alerte dédiée** le
  signale : il faut l'**ajouter** (➕, qui relie au contact Google existant) ou **resynchroniser**,
  plutôt que le traiter comme un inconnu.
- **Contenu des colonnes** : ce sont les **informations du contact** rapproché qui sont affichées (la page
  se lit donc comme la page Contacts). Si le répondant **n'est pas** dans mes contacts, on affiche les
  **valeurs de sa réponse** (quand les questions sont associées à des champs de contact) ; sinon seulement
  son **e-mail**, avec la mention **« (pas dans mes contacts) »** sous la colonne Nom.
- **Ligne rouge + bouton ➕ Contacts** : répondant **absent de mes contacts** → bouton pour l'ajouter
  directement aux contacts (crée le contact, push Google, journalisé).
- **Ligne jaune** : répondant présent dans mes contacts mais dont une **info diffère** (nom, prénom,
  téléphone ou e-mail). Les **cellules concernées** sont surlignées en **jaune plus soutenu**, avec
  info-bulle **« Réponse : … »** donnant la valeur saisie dans le formulaire.
- **Rafraîchissement immédiat** : ajout aux contacts (➕), mise à jour depuis 👁 Réponses et ✔ Valider
  reconstruisent le rapprochement et **rafraîchissent la page** (couleurs, colonnes et alertes) sans recharger.
- **E-mail en rouge** : format d'e-mail invalide (les fautes rattrapables — virgule→point, espaces — sont
  **corrigées automatiquement**).
- **Regroupement** : les soumissions multiples d'une même personne (même e-mail, insensible à la casse) sont
  **fusionnées en une ligne** (la plus récente fait foi).
- **Mails secondaires** : bouton **✉ N** (→ fenêtre stylée `EmailListWindow`) = **ceux de la réponse
  s'il y en a** (colonne « Mails secondaires » mappée), **sinon ceux du contact** ; « N/A » si aucun.
  À la validation (ou mise à jour), les mails secondaires de la réponse **remplacent** ceux du contact
  (ils ne sont plus fusionnés) ; si la réponse n'en fournit aucun, ceux du contact sont conservés.
- **Modifié le** : date de **dernière modification** du formulaire par le répondant (`LastSubmittedTime`
  postérieure à la 1re soumission) ; « N/A » s'il n'a pas modifié sa réponse.
- **👁 Réponses** → détail des champs qui **diffèrent** du contact, avec un **code couleur** : la
  **réponse du formulaire** dans un encadré **bleu** et le **contact actuel** dans un encadré **ambre**.
  Les boutons sont assortis : **✎ Mettre à jour ce champ** (bleu = applique la réponse) et **✖ Ignorer**
  (ambre = garde l'état actuel) ; en bas, **💾 Mettre à jour le contact** (bleu, tout appliquer) et
  **✖ Garder l'état actuel** (ambre, tout ignorer).
- **Ignorer une différence** : le contact **n'est pas modifié** ; c'est **la réponse de la personne qui
  est corrigée sur le site** avec la valeur actuelle du contact
  (`PATCH /api/integration/forms/…/responses/…`), puis les réponses sont relues. Si le champ n'a pas de
  question associée sur le site, un avertissement le signale et rien n'est écrit.
  Dans la modale, la ligne ignorée bascule **aussitôt** sur la valeur conservée (celle du contact), au
  lieu de garder l'ancienne réponse affichée : c'est bien cette valeur qui remplacera la réponse. À
  l'inverse, **✎ Mettre à jour ce champ** copie la valeur de la réponse dans le contact (via Google).
- **⏳ / ↩ par ligne** : forcer le statut d'une personne — la mettre **en liste d'attente** ou la
  **remettre en préinscription**. Un statut forcé est suivi d'un **astérisque** (`En attente *`) et prime
  sur les règles du formulaire ; il est oublié à la validation.
- **🗑 par ligne** : supprime la **préinscription sur le site** (après confirmation). La personne peut
  de nouveau remplir le formulaire ; son contact et son éventuel adhérent ne sont **pas** touchés.
- **Barre de sélection** : dès qu'une case est cochée, une barre verte apparaît sous le filtre avec le
  nombre de sélectionnés et les actions **du mode courant** —
  *préinscrits* : **✔ Valider**, **⏳ Mettre en liste d'attente**, **🗑 Supprimer** ;
  *liste d'attente* : **↩ Remettre en préinscription**, **🗑 Supprimer** (pas de validation directe
  depuis la liste d'attente : il faut d'abord repasser la personne en préinscription).
- **✔ Valider** : les personnes validées **disparaissent** de la liste des réponses et rejoignent
  **✅ Inscrits**, d'où **↩ Remettre en préinscription** les fait réapparaître dans les réponses
  (l'adhérent créé, lui, reste, mais la personne est **dissociée du libellé** — voir ✅ Inscrits).
  La validation **enregistre le libellé associé** (nom + ressource Google) sur l'inscription, ce qui
  permet ensuite de la dissocier du bon libellé. En cas d'erreur Google sur une personne, elle
  **reste** dans la liste.
- **Changement de libellé du formulaire** : si le libellé du formulaire est modifié **sur le site**
  après des validations, l'ouverture des réponses **réaligne les inscrits** : chaque personne déjà
  inscrite est **dissociée de l'ancien libellé** et **associée au nouveau** (Google Contacts), le
  libellé enregistré sur l'inscription est mis à jour, et un message récapitule le nombre de personnes
  déplacées. Sans effet si le libellé n'a pas changé (aucune écriture Google) ; une personne absente
  de mes contacts est ignorée (retentée au prochain chargement).
- **Retour automatique dans la liste** : une personne **dissociée** du libellé peut de nouveau remplir
  le formulaire (le site le voit en moins d'une minute). Si sa **nouvelle réponse est postérieure à sa
  validation**, elle **ressort des inscrits** et **réapparaît** dans les réponses (en préinscription
  ou en attente selon ses réponses) — c'est une nouvelle inscription.
- **✅ Inscrits (personnes inscrites)** : **page inline** (comme ⏳ Liste d'attente, pas une fenêtre)
  ouverte par le bouton **✅ Inscrits**. Son **titre reprend le nom du formulaire** (« Personnes
  inscrites — <formulaire> ») ; en-tête : **← Formulaires**, un **filtre de recherche** et une barre
  d'outils pour **naviguer** — **👥 Préinscrits**, **⏳ Liste d'attente** et **⬇ Exporter CSV** (des
  inscrits affichés, `CsvRegisteredExporter`). Elle affiche les personnes déjà validées **comme la page
  Contacts** (le contact tel quel : Nom · Prénom · Téléphone · E-mail · Mails secondaires · Ajouté le)
  et un bouton **↩ Remettre en préinscription** par ligne : la personne redevient une réponse à traiter
  **et est dissociée du
  libellé** auquel elle avait été associée (l'adhérent reste ; si la dissociation Google échoue, la
  remise en préinscription est annulée). Le retour aux réponses recharge la liste. Les colonnes
  montrent le **contact d'aujourd'hui**, pas
  l'instantané de la validation : modifier le contact met la page à jour. Si le contact a été supprimé
  depuis, la mention *« (plus dans mes contacts) »* apparaît et les valeurs figées à la validation
  sont affichées.
  > **Lien réponse/inscrit ↔ contact stable** : le rapprochement ne dépend plus du seul e-mail courant
  > du contact. Deux mécanismes complémentaires :
  > - **Inscrits** : l'inscription mémorise l'**identifiant du contact** (`Adherent.Id`) à la validation ;
  >   le contact est retrouvé par cet identifiant.
  > - **Réponses (préinscrits / attente) et anciens inscrits** : dès qu'une personne est rapprochée
  >   **par e-mail vérifié**, on enregistre un **lien durable** `e-mail vérifié → identifiant du contact`
  >   (`FormState.ContactLinks`). Aux chargements suivants, le contact est retrouvé **par cet identifiant**,
  >   donc **modifier l'e-mail du contact ne casse plus le lien** (plus de « pas dans mes contacts » avec
  >   l'ancien e-mail).
  >
  > L'e-mail stocké côté réponse/inscription reste, lui, l'**identité de la réponse** (e-mail vérifié)
  > et n'est **pas** réécrit — c'est lui qui masque l'inscrit et porte les statuts forcés *préinscription
  > / attente*. À la **validation**, un contact déjà rapproché n'est **pas dupliqué** et son e-mail
  > (modifié à la main) n'est **pas écrasé** ; seul un **nouveau** contact reçoit l'e-mail vérifié.
  > *Limite* : une personne dont l'e-mail a été modifié **avant** qu'un lien ait pu être mémorisé
  > (aucun rapprochement par e-mail n'a eu lieu) ne peut pas être retrouvée automatiquement — il faut la
  > rapprocher une fois (afficher le formulaire pendant que l'e-mail correspond encore, ou la ré-ajouter).
- **⬇ Exporter CSV** : exporte **les lignes affichées** (recherche et mode ⏳ compris ; colonne *Rang* en
  liste d'attente), avec les colonnes du tableau + *Dans mes contacts* et *Infos différentes*
  (`CsvResponseExporter`, séparateur `;`, UTF-8 BOM, dialogue ouvert sur **Téléchargements**).

### 11.1 Lecture des réponses

> **Fuseau horaire** : le site renvoie ses dates en **UTC** (`…Z`). `WebFormsService` les ramène à
> l'**heure locale** à la lecture. Sans cette conversion, « Répondu le » / « Modifié le » sont
> décalés et surtout les comparaisons avec les dates locales de l'application (ex. la date de
> validation) sont fausses — une nouvelle réponse ne faisait alors pas réapparaître la personne.

- `GoogleFormsService.GetFormQuestionsAsync` (id + intitulé) puis `ListResponsesAsync` (réponses + e-mail
  vérifié). Les répondants sont **regroupés par e-mail vérifié** (identité « qui a répondu », et **non**
  l'e-mail saisi) ; la **dernière soumission** fait foi, la **1re** donne la place dans la file d'attente.
- Le **mapping** vient du site : `WebFormsService` traduit le `contactField` de chaque question
  (`FIRST_NAME`/`LAST_NAME`/`PHONE`/`EMAIL`/`SECONDARY_EMAIL`) en clés `FieldMap` **`prenom` / `nom` /
  `tel` / `email` / `secondaryEmails`**. Ces clés doivent rester **exactement** celles-là : toute autre
  graphie fait que plus aucune valeur de réponse n'est lue (colonnes vides, aucune différence détectée).
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
  Un champ **non renseigné** dans la réponse **n'écrase pas** la valeur actuelle du contact (nom, prénom,
  téléphone) : on garde l'état actuel (idem détection des différences et ✎ mise à jour d'un champ).
  **Choix groupé** : si des personnes sélectionnées ont des **infos différentes** de leur contact, une
  fenêtre (`ValidateChangesWindow`) propose, **pour toutes**, **💾 Mettre à jour** les contacts avec les
  réponses ou **✖ Garder l'état actuel** (ou Annuler) ; « garder » valide sans modifier les contacts
  existants. Sans aucune différence, c'est la confirmation simple habituelle.
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

**E-mails (principal + secondaires).** `ListContactsAsync` lit **tous** les e-mails d'un contact : le
**1er** est le principal, les **suivants** les secondaires. La réconciliation (`ApplyGoogleToLocal`)
aligne aussi les **e-mails secondaires** sur Google (comparaison d'ensemble) : **supprimer un e-mail
secondaire côté Google le retire donc en local** au prochain sync. Côté écriture, `BuildEmails` pousse
principal puis secondaires.

**Dédoublonnage local (au début de la synchro).** Plusieurs adhérents locaux ont pu, au fil des
ajouts/validations, pointer sur le **même contact Google** (même `GoogleResourceName`) — ce qui gonflait
le total local par rapport à Google. La synchro **n'en garde qu'un par ressource** (fusion des e-mails
secondaires, retrait des doublons) avant la réconciliation. Les contacts **liés à une ressource Google
absente en ligne** (contact supprimé côté Google) restent, eux, retirés par la réconciliation.

**Lecture autoritaire & bilan.** Après une **lecture Google réussie** (People API paginée), la liste
locale reflète **exactement** les contacts renvoyés par l'API (un adhérent par ressource) :
`SyncContactsAsync` renvoie un **`SyncReport`** (compte Google, compte local, ajoutés/retirés/poussés/
fusionnés). La synchro **de démarrage** reste silencieuse (tolérance hors ligne) ; le bouton
**🔄 Resynchroniser** (page Contacts) force une réconciliation complète, **affiche le bilan** et
**remonte les erreurs**. Si la lecture échoue, la liste locale n'est **pas** laissée à moitié
réconciliée. À noter : le compte de l'**API People** peut différer du nombre affiché dans l'**interface
web** Google Contacts (contacts sans nom, « Autres contacts », éléments en corbeille non purgés…) — le
bilan montre le compte **vu par l'appli** pour lever l'ambiguïté.

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
- **Pagination** : le résultat filtré est **paginé à 1000 lignes par page** (◀ Précédent / Suivant ▶ +
  « Page X / Y ») ; le compteur affiche le **total** filtré. Le changement de filtre revient page 1.
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
  **Fenêtre réduite** : les colonnes ne s'écrasent plus. Les tableaux principaux (Contacts,
  Préinscriptions, Historique) ont des colonnes à **largeur fixe** et **une seule colonne extensible**
  (l'e-mail, ou « Nouvelle valeur » pour l'historique) qui absorbe l'espace quand la fenêtre est large.
  Quand elle rétrécit, un **défilement horizontal** standard apparaît (barre en bas,
  `HorizontalScrollBarVisibility=Auto`) — pas de recalcul « étoilé » instable au redimensionnement.
  Le **défilement tactile** (panning horizontal + vertical) est activé sur le ScrollViewer interne
  (`DataGrid_Loaded` dans `App.xaml.cs`, `PanningMode=Both`).
- **Page Paramètres** : navigateur d'ouverture des liens, **adresse de l'application web** des
  formulaires d'inscription (vide = valeur par défaut) et **clé d'API d'intégration** (champ masqué,
  = `INTEGRATION_API_KEY` du site). Enregistrés dans `settings.json`, jamais versionnés.
- **Mise à jour** : comparaison avec la dernière Release GitHub à la connexion.
- **Journal d'erreurs** : plantages non gérés → `log_error.txt` (via `ErrorLogger`).

---

## 15bis. Liaison avec l'application web (dépannage)

`WebFormsService` traduit chaque situation en message lisible dans la page :

| Symptôme | Cause | Correction |
|---|---|---|
| « Liaison … non configurée » | clé d'API vide | Paramètres → coller la clé du site |
| « Clé d'API refusée » (401) | clé différente de `INTEGRATION_API_KEY` | recopier la valeur exacte |
| « Le site ne propose pas encore l'API » (3xx) | site déployé sans les routes d'intégration | redéployer l'application web |
| « Cette fonction n'existe pas sur le site » (404 + page HTML) | route absente de la **version déployée** (site en retard sur le code local) | redéployer, ou pointer Paramètres sur `http://localhost:3000` |
| « Cette action n'existe pas encore sur le site » (405) | la route existe mais pas cette méthode (ex. `DELETE` d'une réponse) : même cause, version déployée antérieure | idem |

Les erreurs de liaison (`WebFormsException`) sont traitées comme les erreurs Google par
`ProgressRunner` : leur message s'affiche **dans la page ou la boîte de dialogue** concernée, jamais
en « erreur inattendue ».
| « Introuvable : formulaire ou réponse supprimé(e) » (404 + JSON) | la donnée n'existe plus côté site | actualiser la liste |
| « pas de clé d'intégration configurée » (503) | `INTEGRATION_API_KEY` absente côté site | l'ajouter aux variables Vercel puis redéployer |
| « Application web injoignable » | réseau / site arrêté | vérifier la connexion et l'adresse |
| Liste vide sans erreur | aucun formulaire **créé par le compte connecté** | se connecter avec le compte propriétaire, ou en créer un sur le site |

Le client **ne suit pas les redirections** : une redirection vers la page de connexion signifie que
l'API n'est pas déployée, et non que la clé est mauvaise.

---

## 16. Gestion des erreurs Google (courantes)

| Message | Cause | Solution |
|---|---|---|
| `insufficient authentication scopes` | Permissions non toutes accordées | Se déconnecter/reconnecter en **cochant tout** |
| `access_denied` (Accès bloqué) | Compte non testeur | Ajouter le compte en **Utilisateur de test** |
| `Contact must always be in at least one contact group` | Retrait de la dernière appartenance | Géré : `myContacts` toujours garanti (§13) |
| `People/Sheets/Drive/Forms API not enabled` | API non activée | Activer l'API concernée (dont **Google Forms API** pour les préinscriptions) dans le projet Google Cloud |
| `client_secret.json introuvable` | Fichier manquant | Placer `client_secret.json` à côté de l'exe |
