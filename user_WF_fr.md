# Parcours des utilisateurs et des accès

## Objectif et périmètre

Ce guide explique les rôles des utilisateurs, l’accès aux projets, la configuration des utilisateurs humains et l’accès des agents IA dans le Bug Tracker. Il est rédigé pour les utilisateurs non techniques et les administrateurs.

## Commencer par le modèle d’accès

### Rôles et types d’utilisateurs

| Élément | Signification |
| --- | --- |
| Développeur (`dev`) | Un utilisateur humain ou IA qui travaille dans les projets auxquels il est rattaché. |
| Senior | Un utilisateur humain disposant d’un accès plus large aux projets normaux et de responsabilités définies de gestion de projets. |
| Administrateur | Un utilisateur humain disposant d’un accès d’administration à l’échelle de l’organisation. |
| Humain | Une personne qui se connecte avec une adresse e-mail et un mot de passe. |
| Agent IA | Un utilisateur non humain qui se connecte avec un nom d’utilisateur et un jeton de serment. |

Seul un administrateur peut modifier le rôle d’un utilisateur humain. Un administrateur ne peut pas rétrograder son propre rôle ni celui d’autres administrateurs.

### Projets et rattachements

| Concept | Signification |
| --- | --- |
| Projet normal | Les seniors peuvent le voir dans toute l’organisation. Les développeurs et les agents IA doivent y être rattachés pour le découvrir ou y créer des tickets. |
| Projet sensible | L’accès requiert un rattachement au projet pour les utilisateurs non administrateurs, y compris les seniors. |
| Rattachement | L’enregistrement officiel d’appartenance à un projet. Les rattachements font foi pour l’accès. |
| Propriétaire | Un senior humain ou un administrateur qui est le contact d’accès du projet. La propriété ne permet pas de contourner les règles d’accès aux tickets ou aux projets. |

Les développeurs et les agents IA ne peuvent découvrir des projets et y créer des tickets que s’ils y sont rattachés. Les administrateurs disposent d’un accès global.

## Utilisateurs humains

### Demander et configurer un compte humain

1. Une personne qui n’est pas connectée soumet une **Demande d’accès** (**Access Request**) avec sa demande de compte humain et son adresse e-mail.
2. Un administrateur examine la demande.
3. Si elle est approuvée, l’administrateur peut ajuster le nom d’utilisateur et envoie un lien de configuration.
4. La personne utilise le lien de configuration dans les 30 minutes pour définir un mot de passe. (Un service d’e-mail peut être connecté pour envoyer ces messages aux utilisateurs d’une organisation.)
5. Le nouveau compte débute comme développeur humain.
6. L’utilisateur se connecte avec son adresse e-mail et son mot de passe.

Les sessions humaines expirent après 24 heures ou 45 minutes d’inactivité. Reconnectez-vous lorsque la session expire.

### Gérer les rôles humains

1. Un administrateur examine l’utilisateur et les responsabilités qui lui sont nécessaires.
2. L’administrateur modifie le rôle au besoin : développeur, senior ou administrateur.
3. L’administrateur confirme que l’utilisateur possède les rattachements aux projets nécessaires.

Seul un administrateur modifie les rôles. Un utilisateur ne peut pas se promouvoir lui-même, et un administrateur ne peut pas se rétrograder lui-même.

### Créer et gérer des projets

#### Créer un projet

1. Un senior ou un administrateur crée un projet.
2. Le créateur devient le propriétaire initial et reçoit un rattachement au projet.
3. Conservez le rattachement du propriétaire tant qu’il est propriétaire du projet.

Un senior ne peut créer qu’un projet normal. Seul un administrateur peut créer un projet sensible ou modifier la visibilité d’un projet.

#### Transférer la propriété

1. Choisissez un nouveau propriétaire admissible.
2. Transférez la propriété avant de supprimer le rattachement du propriétaire actuel.
3. Confirmez que le nouveau propriétaire reste rattaché au projet.

Un administrateur peut transférer la propriété de tout projet. Un senior peut transférer un projet normal à un senior ou administrateur admissible. La propriété d’un projet sensible doit être détenue par un administrateur.

### Gérer les rattachements

1. Identifiez le projet et l’utilisateur qui a besoin d’un accès.
2. Ajoutez ou supprimez le rattachement de l’utilisateur.
3. Vérifiez le travail sur les tickets et la propriété avant de retirer l’accès.

Les administrateurs gèrent les rattachements de tous les projets. Les seniors gèrent les membres des projets normaux auxquels ils peuvent accéder. Les demandes d’accès peuvent être approuvées par un administrateur ou par le senior propriétaire d’un projet normal.

Le retrait du rattachement à un projet sensible supprime immédiatement l’accès de l’utilisateur à ses tickets, y compris aux tickets qu’il a signalés ou auxquels il était assigné. Dans un projet normal, l’auteur du signalement ou la personne assignée peut conserver l’accès à ce ticket précis après le retrait du rattachement.

### Réduire les accès d’un utilisateur humain

1. Supprimez les rattachements aux projets qui ne sont plus nécessaires.
2. Transférez toute propriété de projet avant de supprimer le rattachement du propriétaire.
3. Si cela est approprié, un administrateur modifie le rôle de la personne.

Il n’existe pas de processus général de désactivation ou de suppression d’utilisateur. Utilisez les modifications de rattachement et de rôle pour réduire les responsabilités liées aux projets ; cela ne décrit pas la suppression d’un compte actif.

## Agents IA

### Mettre en place un agent IA

Il existe deux façons de commencer : un agent IA demande l’accès `ai_agent`, ou un administrateur crée directement l’agent.

1. Un administrateur approuve la demande ou crée l’agent.
2. L’administrateur choisit le nom d’utilisateur de l’agent et délivre un jeton de serment.
3. Conservez le jeton de serment en lieu sûr lorsqu’il est affiché. Il n’est affiché que lors de sa délivrance ou de sa réémission.
4. Définissez la durée de validité du jeton de serment entre 1 et 62 jours ; la valeur par défaut est 30 jours.
5. Rattachez l’agent aux projets dans lesquels il doit travailler.
6. L’agent se connecte avec son nom d’utilisateur et son jeton de serment.

Un agent IA débute comme développeur. Sa session porteuse dure jusqu’à l’expiration du jeton de serment. Les adresses e-mail de contact sont masquées pour les agents.

### Ce qu’un agent IA peut et ne peut pas faire

Un agent peut découvrir des tickets et en créer uniquement dans les projets auxquels il est rattaché. Il peut gérer les tickets dont il est l’auteur du signalement ou auxquels il est assigné, mais l’appartenance au projet sensible reste obligatoire.

Un agent ne peut pas créer ni modifier des projets, des rattachements, la propriété ou les assignations de tickets.

### Traiter une notification de travail destinée à une IA

1. Recevez une notification de travail sur un ticket.
2. Récupérez et lisez le ticket complet avant d’agir, en utilisant la version la plus récente du ticket.
3. Effectuez le travail sûr autorisé par le ticket.
4. Si l’agent est bloqué ou n’est pas certain qu’une modification soit sûre, il ajoute un commentaire indiquant ses constatations, le blocage et la prochaine action nécessaire de la part d’un humain.
5. Ne marquez la notification comme lue qu’après avoir traité le travail ou documenté le blocage.

Si l’accès à un ticket est refusé à l’agent, il doit demander l’accès au projet et attendre l’approbation. Après une reconnexion, il doit vérifier les notifications non lues afin de récupérer le travail manqué hors ligne.

### Réémettre ou réduire l’accès d’une IA

1. Réémettez un jeton de serment lorsqu’un identifiant de remplacement est nécessaire ; l’ancien jeton de serment ne peut plus être utilisé pour de futures connexions.
2. Supprimez une demande d’accès IA approuvée lorsqu’il faut empêcher de futures connexions avec un jeton de serment.
3. Supprimez les rattachements aux projets lorsque l’agent n’a plus besoin d’un projet.
4. Utilisez la déconnexion pour révoquer une session porteuse existante.

Le retrait d’un rattachement à un projet sensible bloque immédiatement l’accès aux tickets de ce projet. Il ne révoque pas explicitement une session porteuse déjà délivrée, mais chaque demande fait de nouveau l’objet d’une vérification d’autorisation. Il n’existe pas de processus général de désactivation ou de suppression d’utilisateur ; utilisez les modifications d’identifiants et d’accès plutôt que de décrire cela comme une suppression de compte.

## Questions fréquentes

### Qui peut voir un projet sensible ?

Les administrateurs peuvent y accéder. Les autres utilisateurs, y compris les seniors, doivent y être rattachés.

### Le fait d’être propriétaire d’un projet donne-t-il un accès illimité ?

Non. Le propriétaire est le contact d’accès. La propriété ne permet pas de contourner les règles d’autorisation des tickets ou de rattachement.

### Un senior peut-il créer un projet sensible ?

Non. Seul un administrateur humain peut créer des projets sensibles ou modifier la visibilité d’un projet.

### Que faut-il faire avant de retirer l’accès d’un propriétaire ?

Transférez d’abord la propriété, puis retirez le rattachement de l’ancien propriétaire si cela est approprié.

### Un agent IA peut-il s’assigner un ticket ?

Non. Les agents IA ne peuvent pas créer ni modifier des assignations. Un senior humain ou un administrateur gère l’assignation.

### Retirer un agent d’un projet le déconnecte-t-il partout ?

Non. Le retrait du rattachement modifie l’autorisation des demandes liées au projet, notamment l’accès immédiat aux projets sensibles. Utilisez la déconnexion pour révoquer une session porteuse, et réémettez ou supprimez l’accès par jeton de serment lorsqu’il faut interrompre l’accès par identifiant.
