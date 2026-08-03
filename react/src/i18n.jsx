import React, { createContext, useContext, useEffect, useState } from 'react';

const LANGUAGE_STORAGE_KEY = 'bug-tracker-language';

const french = {
  'language.label': 'Langue',
  'language.english': 'Anglais',
  'language.french': 'Français',
  'nav.dashboard': 'Tableau de bord', 'nav.tickets': 'Voir les tickets', 'nav.allocated': 'Anomalies attribuées',
  'nav.submitted': 'Envoyés', 'nav.archived': 'Archivés', 'nav.addBug': 'Ajouter une anomalie',
  'nav.projects': 'Projets', 'nav.users': 'Utilisateurs', 'nav.logs': 'Logs',
  'nav.incidentCommand': 'Gestion des incidents', 'nav.signedInAs': 'Connecté en tant que',
  'nav.user': 'utilisateur', 'nav.logout': 'Se déconnecter', 'nav.toggle': 'Afficher le menu de navigation',
  'nav.unread': 'Notifications non lues', 'nav.noUnread': 'Aucune alerte non lue.',
  'nav.markAllRead': 'Tout marquer comme lu', 'nav.markRead': 'Marquer comme lu', 'nav.ticketNotification': 'Notification de ticket',
  'auth.email': 'E-mail', 'auth.password': 'Mot de passe', 'auth.passwordPlaceholder': 'Saisissez votre mot de passe',
  'auth.signingIn': 'Connexion...', 'auth.signIn': 'Se connecter', 'auth.requestAccess': 'Demander l’accès', 'auth.forgotPassword': 'Mot de passe oublié ?',
  'auth.type': 'Type', 'auth.human': 'Humain', 'auth.confirmEmail': 'Confirmer l’e-mail', 'auth.submitting': 'Envoi...',
  'auth.submitRequest': 'Envoyer la demande', 'auth.backToSignIn': 'Retour à la connexion',
  'auth.passwordLinkHelp': 'Ce lien de mot de passe est associé à cette adresse e-mail.', 'auth.newPassword': 'Nouveau mot de passe',
  'auth.confirmNewPassword': 'Confirmer le nouveau mot de passe', 'auth.atLeast6': 'Au moins 6 caractères',
  'auth.passwordRules': 'Utilisez au moins 6 caractères, avec un chiffre et un caractère spécial.',
  'auth.repeatNewPassword': 'Répétez le nouveau mot de passe', 'auth.saving': 'Enregistrement...', 'auth.setPassword': 'Définir le mot de passe',
  'auth.demoAccess': 'Accès démo public', 'auth.demoWarning': 'Toutes les données sont synthétiques, publiques et modifiables par les autres visiteurs. Elles sont réinitialisées chaque jour à {{resetAtUtc}} UTC. Ne saisissez aucune information personnelle, privée ou confidentielle.',
  'auth.chooseRole': 'Choisissez un rôle', 'auth.demoPasswords': 'Les mots de passe sont visibles uniquement parce que ces comptes de démonstration sont intentionnellement publics.',
  'auth.demoGuidanceTitle': 'Créez votre accès de démonstration', 'auth.demoGuidance': 'Utilisez les comptes prédéfinis ou envoyez une demande pour créer le vôtre. Donnez accès à votre propre agent à cette démo en créant un compte d’agent IA fictif.',
  'auth.demoOnly': 'Démo uniquement : utilisez une adresse e-mail fictive pour vous identifier. Aucun e-mail n’est envoyé et les données de démonstration sont réinitialisées chaque jour. N’utilisez aucune information personnelle ou confidentielle.',
  'auth.demoRequestHelp': 'Après l’envoi, connectez-vous avec le compte administrateur de démonstration, puis ouvrez Utilisateurs et Demandes pour terminer la création du compte et générer son lien de mot de passe ou jeton de serment IA.',
  'auth.recoveryHelp': 'Demandez une réinitialisation de mot de passe pour un compte humain ou la réémission d’un jeton de serment pour un agent IA. Par confidentialité, nous ne confirmons pas l’existence d’un compte.',
  'auth.noEmailDemo': ' Aucun e-mail n’est envoyé par cette démo.', 'auth.accountType': 'Type de compte', 'auth.humanPassword': 'Mot de passe humain', 'auth.agentOathToken': 'Jeton de serment agent IA', 'auth.requestRecovery': 'Demander la récupération',
  'addBug.issueTitle': 'Titre de l’anomalie', 'addBug.issueTitlePlaceholder': 'Décrivez brièvement l’anomalie', 'addBug.issueTitleHelp': 'Donnez au problème un titre court et facile à rechercher.',
  'addBug.environment': 'Environnement', 'addBug.environmentPlaceholder': 'Navigateur, OS, appareil, version de l’API', 'addBug.environmentHelp': 'Facultatif : navigateur, OS, appareil, version de l’API.',
  'addBug.expectedBehavior': 'Comportement attendu', 'addBug.expectedBehaviorPlaceholder': 'Que devait-il se passer ?', 'addBug.actualBehavior': 'Comportement observé', 'addBug.actualBehaviorPlaceholder': 'Que s’est-il passé à la place ?',
  'addBug.stepsToReproduce': 'Étapes pour reproduire', 'addBug.bugType': 'Type d’anomalie', 'addBug.bugTypeHelp': 'Classez le mode de défaillance principal.',
  'addBug.loadingProjects': 'Chargement des projets...', 'addBug.projectHelp': 'Choisissez le projet auquel appartient cette anomalie.',
  'addBug.newProjectName': 'Nom du nouveau projet', 'addBug.newProjectNamePlaceholder': 'Nom du projet (50 caractères maximum)', 'addBug.newProjectNameHelp': 'Utilisez un nom concis de 50 caractères maximum.',
  'addBug.newProjectVisibility': 'Visibilité du nouveau projet', 'addBug.sensitiveProjectHelp': 'Les projets sensibles sont visibles uniquement aux membres explicitement attribués.',
  'addBug.assignTicket': 'Attribuer le ticket (facultatif)', 'addBug.loadingAssignees': 'Chargement des personnes assignables...', 'addBug.sensitiveAssigneeHelp': 'Projet sensible : la personne choisie doit déjà être membre du projet.',
  'addBug.unassignedHelp': 'Laissez non attribué pour créer un ticket à faire. Les personnes assignées à un projet sensible doivent en être membres.',
  'addBug.severityHelp': 'Quel est l’impact sur les utilisateurs ou le système ?', 'addBug.priorityHelp': 'Définissez l’ordre dans lequel ce problème doit être traité.',
  'headers.signIn': 'Se connecter', 'headers.signInDescription': 'Accédez à votre espace de suivi des anomalies et aux tickets de vos projets.',
  'headers.setupPassword': 'Définir ou réinitialiser le mot de passe', 'headers.setupPasswordDescription': 'Utilisez votre lien à usage unique pour définir un nouveau mot de passe en toute sécurité.',
  'headers.requestAccess': 'Demander l’accès', 'headers.requestAccessDescription': 'Envoyez votre e-mail pour demander un compte humain ou agent IA.',
  'headers.recoverCredentials': 'Récupérer les identifiants', 'headers.recoverCredentialsDescription': 'Demandez une réinitialisation de mot de passe ou un nouveau jeton IA.',
  'app.bugOperations': 'Gestion des tickets',
  'headers.dashboard.title': 'Tableau de bord', 'headers.dashboard.description': 'Consultez vos tickets actifs par projet et un aperçu rapide de la charge de travail.',
  'headers.tickets.title': 'Voir les tickets', 'headers.tickets.description': 'Parcourez les tickets actifs et consultez les rapports d’anomalie complets.',
  'headers.allocated.title': 'Anomalies attribuées', 'headers.allocated.description': 'Traitez les tickets qui vous sont attribués et mettez à jour leurs rapports.',
  'headers.submitted.title': 'Rapports envoyés', 'headers.submitted.description': 'Suivez les rapports d’anomalie que vous avez envoyés et modifiez les rapports ouverts.',
  'headers.archived.title': 'Tickets archivés', 'headers.archived.description': 'Consultez les tickets résolus et l’historique de leurs rapports finaux.',
  'headers.addBug.title': 'Ajouter une anomalie', 'headers.addBug.description': 'Créez un nouveau rapport avec le projet, la gravité et les éléments de preuve.',
  'headers.projects.title': 'Gestion des projets', 'headers.projects.description': 'Gérez les projets et attribuez des utilisateurs aux projets.',
  'headers.users.title': 'Utilisateurs', 'headers.users.description': 'Gérez les utilisateurs, les demandes d’accès et l’activité par utilisateur.',
  'headers.auditLogs.title': 'Journaux d’audit', 'headers.auditLogs.description': 'Recherchez l’activité humaine et des agents IA dans l’espace de travail.',
  'common.search': 'Rechercher',
  'common.all': 'Tous',
  'common.any': 'Tous',
  'common.bug': 'Anomalie',
  'common.reportedBy': 'Signalé par',
  'common.assignedTo': 'Assigné à',
  'common.activeSince': 'Actif depuis',
  'common.project': 'Projet',
  'common.severity': 'Gravité',
  'common.loading': 'Chargement...',
  'common.cancel': 'Annuler',
  'common.close': 'Fermer',
  'common.save': 'Enregistrer',
  'common.submit': 'Envoyer',
  'common.back': 'Retour',
  'common.status': 'Statut',
  'common.priority': 'Priorité',
  'common.tags': 'Étiquettes',
  'common.noTags': 'Aucune étiquette',
  'pages.dashboard.title': 'Tableau de bord',
  'pages.dashboard.subtitle': 'Consultez rapidement votre périmètre actuel avant d’ouvrir les détails des tickets.',
  'pages.dashboard.summaryLabel': 'Résumé du tableau de bord',
  'pages.dashboard.activeTickets': 'Tickets actifs',
  'pages.dashboard.allocatedTickets': 'Tickets attribués',
  'pages.dashboard.urgentTickets': 'Tickets urgents',
  'pages.dashboard.unassigned': 'Non attribués',
  'pages.dashboard.exactVisibleTotal': 'Total visible exact',
  'pages.dashboard.assignedToYou': 'Assignés à vous',
  'pages.dashboard.needsTriageOwnership': 'Nécessitent un responsable de triage',
  'pages.dashboard.activeTicketPreview': 'Aperçu des tickets actifs',
  'pages.dashboard.openInViewTickets': 'Ouvrir dans Voir les tickets',
  'pages.dashboard.emptyTitle': 'Aucun ticket actif pour le moment.',
  'pages.dashboard.emptyDescription': 'Les nouveaux signalements apparaîtront ici dès leur arrivée dans la file active.',
  'tickets.columns.bug': 'Anomalie',
  'tickets.columns.status': 'Statut',
  'tickets.columns.reportedBy': 'Signalé par',
  'tickets.columns.assignee': 'Assigné à',
  'tickets.columns.activeSince': 'Actif depuis',
  'tickets.columns.project': 'Projet',
  'tickets.columns.severity': 'Gravité',
  'tickets.columns.priority': 'Priorité',
  'tickets.actions.allocateTo': 'Attribuer à',
  'tickets.actions.viewReports': 'Voir les rapports',
  'tickets.actions.editBugReport': 'Modifier le rapport d’anomalie',
  'tickets.actions.modifySolutionSteps': 'Modifier les étapes de résolution',
  'tickets.actions.createSolution': 'Créer une solution',
  'tickets.actions.editMetadata': 'Modifier les métadonnées',
  'tickets.actions.closeBug': 'Fermer l’anomalie',
  'tickets.filters.search': 'Rechercher des tickets',
  'tickets.filters.searchPlaceholder': 'Titre, rapport, projet, étiquette, priorité...',
  'tickets.filters.urgent': 'Urgent',
  'tickets.filters.recentlyUpdated': 'Mis à jour récemment',
  'tickets.filters.unassigned': 'Non attribués',
  'tickets.filters.server': 'Filtres serveur des tickets',
  'tickets.filters.apply': 'Appliquer les filtres',
  'tickets.filters.reset': 'Réinitialiser les filtres',
  'tickets.filters.tag': 'Étiquette',
  'tickets.filters.projectId': 'ID du projet',
  'tickets.filters.assigneeId': 'ID de l’assigné',
  'tickets.filters.reporterId': 'ID du déclarant',
  'tickets.actions.bulkAssignVisible': 'Attribuer les tickets visibles en masse',
  'tickets.allocate.ariaLabel': 'Attribuer le ticket',
  'tickets.allocate.title': 'Attribuer le ticket',
  'tickets.allocate.close': 'Fermer le panneau d’attribution',
  'validation.issueTitleRequired': 'Le titre de l’anomalie est obligatoire.',
  'validation.descriptionRequired': 'La description est obligatoire.',
  'validation.bugTypeRequired': 'Le type d’anomalie est obligatoire.',
  'validation.severityRequired': 'La gravité est obligatoire.',
  'validation.priorityRequired': 'La priorité est obligatoire.',
  'validation.projectRequired': 'Le projet est obligatoire.',
  'validation.createProjectBeforeSubmitting': 'Créez le nouveau projet avant d’envoyer l’anomalie.',
  'validation.chooseCoreTag': 'Choisissez front-end ou back-end.',
  'validation.chooseOneCoreTag': 'Choisissez front-end ou back-end, pas les deux.'
};

// This fallback keeps untranslated future copy readable while the catalog is extended.
const frenchTerms = [
  ['Unable to load', 'Impossible de charger'], ['Unable to', 'Impossible de'], ['Loading', 'Chargement'],
  ['No active tickets', 'Aucun ticket actif'], ['View Tickets', 'Voir les tickets'], ['Allocated Bugs', 'Anomalies attribuées'],
  ['Archived Tickets', 'Tickets archivés'], ['Add Bug', 'Ajouter une anomalie'], ['Project Management', 'Gestion des projets'],
  ['Audit Logs', 'Journaux d’audit'], ['Request Access', 'Demander l’accès'], ['Sign In', 'Se connecter'],
  ['Password', 'Mot de passe'], ['Email', 'E-mail'], ['Save', 'Enregistrer'], ['Cancel', 'Annuler'], ['Close', 'Fermer'],
  ['Delete', 'Supprimer'], ['Edit', 'Modifier'], ['Create', 'Créer'], ['Update', 'Mettre à jour'], ['Submit', 'Envoyer'],
  ['Project', 'Projet'], ['Tickets', 'Tickets'], ['Ticket', 'Ticket'], ['Report', 'Rapport'], ['Reports', 'Rapports'],
  ['Severity', 'Gravité'], ['Priority', 'Priorité'], ['Status', 'Statut'], ['Assigned', 'Attribué'], ['Unassigned', 'Non attribué'],
  ['Search', 'Rechercher'], ['Filters', 'Filtres'], ['Users', 'Utilisateurs'], ['Notifications', 'Notifications'], ['Log Out', 'Se déconnecter']
];

function interpolate(value, variables) {
  return String(value).replace(/{{(\w+)}}/g, (_match, key) => String(variables?.[key] ?? ''));
}

function translateFallback(fallback) {
  return frenchTerms.reduce((result, [english, translated]) => result.replaceAll(english, translated), fallback);
}

const I18nContext = createContext({ language: 'en-GB', setLanguage: () => {}, t: (_key, fallback = '', variables) => interpolate(fallback, variables) });

export function I18nProvider({ children }) {
  const [language, setLanguageState] = useState(() => localStorage.getItem(LANGUAGE_STORAGE_KEY) === 'fr-FR' ? 'fr-FR' : 'en-GB');

  useEffect(() => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    document.documentElement.lang = language;
  }, [language]);

  function t(key, fallback = key, variables) {
    const value = language === 'fr-FR' ? french[key] || translateFallback(fallback) : fallback;
    return interpolate(value, variables);
  }

  return <I18nContext.Provider value={{ language, setLanguage: setLanguageState, t }}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  return useContext(I18nContext);
}
