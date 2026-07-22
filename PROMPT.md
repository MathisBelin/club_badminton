Je développe une application de bureau Windows (WPF, .NET 8, C#) pour gérer les
adhérents d'un club de badminton, synchronisée avec Google Contacts, Google Sheets
et Gmail. Interface 100 % en français. Reprends le développement dessus.

CONTEXTE PROJET
- Dépôt local : F:\Projets\Pro\Bad  (branche main, GitHub : MathisBelin/club_badminton)
- Pile : C# / WPF / .NET 8 (net8.0-windows), code-behind (PAS de MVVM lourd, PAS de DI).
  NuGet : Google.Apis.{Auth,PeopleService.v1,Sheets.v4,Drive.v3,Forms.v1}, ClosedXML.
  Persistance : JSON (System.Text.Json) dans %LOCALAPPDATA%\BadmintonClub (par compte).
- Doc à jour et complète : lis d'abord docs/DOCUMENTATION.md (architecture, pages,
  Google Forms & réponses, historique, stockage) et docs/DEPLOIEMENT.md (build/version/installeur).

COMMENT JE TRAVAILLE (à respecter)
- Après CHAQUE modif de code : `dotnet build BadmintonClub.csproj -nologo` et corriger
  jusqu'à 0 erreur / 0 avertissement.
- Avant de builder, tuer l'instance en cours (elle verrouille l'exe) :
  Get-Process -Name BadmintonClub -EA SilentlyContinue | Stop-Process -Force
- Pour vérifier qu'il n'y a pas de crash au démarrage, lancer l'exe quelques secondes
  puis le tuer : bin\Debug\net8.0-windows\BadmintonClub.exe (MainWindow, ContactsView,
  HistoryView… sont instanciés au démarrage → un crash XAML se voit tout de suite).
- Astuce build WPF : après ajout d'un NOUVEAU fichier .xaml, l'incrémental peut sortir des
  erreurs BG1002 « .baml introuvable » → faire un clean complet (supprimer obj\ et bin\) puis rebuild.
- Réponses en français, concises.

RÈGLES IMPORTANTES
- NE PAS git push sans demande explicite (committer en local est OK).
- NE JAMAIS committer client_secret.json / config.json (déjà dans .gitignore).
- L'appli NE DOIT PAS écrire dans les Google Sheets ni dans les Google Forms de l'utilisateur
  (la lecture des réponses de formulaire est en lecture seule). SEULE EXCEPTION autorisée :
  emailCollectionType=VERIFIED écrit à la création d'un formulaire (voir ÉTAT ACTUEL).
- L'appli exige une connexion Google : pour tester l'UI hors connexion, il n'y a plus de
  bypass (il a été retiré) — me demander si besoin d'en réintroduire un temporaire.
- NE PAS lancer l'exe pour tester quand un jeton Google peut exister : le démarrage tente une
  connexion silencieuse et peut ouvrir un onglet OAuth. Se limiter au build (0 err/0 warn).

ÉTAT ACTUEL
- Grosses fonctionnalités récentes :
  • Auth robuste : AuthorizeAsync vérifie que le jeton couvre les scopes critiques (contacts/
    sheets/drive) et relance UN consentement complet sinon (garde-fou anti-boucle) ; scopes
    Forms ajoutés (forms.body + forms.responses.readonly).
  • Page « Google Forms » : liste (Drive), créer (vierge / depuis un MODÈLE LOCAL : liste ou import
    fichier, recréé via l'API), ⚙ Configuration (renommer, libellé unique, correspondances
    réponse→colonne du contact [FieldMap : prénom/nom/tél/e-mail/mails secondaires, questions texte],
    règles de réponses par option : liste d'attente / annulation, ⭐ enregistrer comme modèle),
    👥 Réponses, lien hypertexte (Ctrl+C ok), supprimer. Collecte e-mail VÉRIFIÉ activée
    automatiquement à la création. Bandeau + fenêtre de rappel (capture réelle) pour les réglages
    non pilotables par l'API (« autoriser la modification » et « limiter à 1 réponse »).
  • Modèles de formulaire = fichiers JSON locaux (modeles_forms), FormTemplate/FormTemplateRepository.
  • Page « Préinscriptions » = SÉLECTEUR de formulaire (tableau) → réponses d'un formulaire (contacts,
    mêmes colonnes ; nom du formulaire en titre). Identité = e-mail vérifié (regroupement insensible à
    la casse, plus récente = état courant). Statuts : En préinscription / En attente / Annulée.
    Vue par défaut = préinscrits seuls ; ⏳ liste d'attente (colonne Rang) triée par date.
    Colonnes : Alerte (bouton→message box : pas dans contacts / déjà associé / infos différentes /
    e-mail invalide), Mails secondaires (bouton→message box), e-mail rouge si invalide (virgule→point
    auto). Ligne ROUGE si absent des contacts (bouton ➕ Ajouter), JAUNE si infos différentes.
    Modal 👁 : surlignage jaune des réponses différentes + bouton mettre à jour le contact.
    Validation groupée en adhérents (association au libellé).
  • Ancien système de préinscription (formulaire fixe + moteur + cloche) et éditeur de formulaire
    SUPPRIMÉS.
- ⚠️ L'API Google Forms doit être ACTIVÉE dans le projet Google Cloud (sinon 403) pour créer/
  configurer/lire les formulaires.
- ⚠️ EXCEPTION à « pas d'écriture dans les Forms » : la collecte e-mail VÉRIFIÉ est écrite
  automatiquement à la CRÉATION d'un formulaire (emailCollectionType=VERIFIED). Le reste des Forms/
  réponses demeure en lecture seule. « Limiter à 1 réponse » et « autoriser la modification » ne sont
  PAS exposés par l'API (à activer à la main → d'où le bandeau/rappel).
- Beaucoup de travail EN LOCAL non commité (lot Google Forms / réponses). Me demander avant de commit.

Confirme que tu as lu docs/DOCUMENTATION.md puis attends ma prochaine demande.