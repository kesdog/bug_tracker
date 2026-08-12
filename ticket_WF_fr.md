# Cycle de vie des tickets

## Objectif et périmètre

Ce guide explique comment un ticket de bug passe de sa soumission à son archivage dans le Bug Tracker. Il s’adresse aux personnes qui utilisent l’application, et non à la configuration technique ni à l’utilisation de l’API.

## Termes clés

| Terme | Signification en termes simples |
| --- | --- |
| Auteur du signalement | La personne qui a créé le ticket. Cette information ne change pas. |
| Personne assignée | La personne ou l’agent IA actuellement responsable du travail sur le ticket. |
| Rapport de bug initial | La description soumise à l’origine et les images éventuellement envoyées. |
| Rapport de solution / correctif | Le relevé du travail d’investigation et de résolution, y compris ses images. |
| Notes de résolution | Les notes obligatoires à fournir lors de la fermeture d’un ticket. |
| Actif | Un ticket dont l’état est `todo`, `open` ou `reopened`. |
| Archivé | Un ticket dont l’état est `closed`, y compris un ticket annulé. |

## Cycle de vie d’un ticket

```text
                          assigner ou réassigner
                               +-----------+
                               |           v
Créer sans assigner --> [ À FAIRE ] -----> [ OUVERT ]
                               ^              |
                               |              | fermer avec des notes de résolution
                               |              v
                               +-------- [ FERMÉ / ARCHIVÉ ]
                                 rouvrir avec un motif    |
                                                        |
                        annuler sans solution ----------+
```

Lorsqu’un ticket fermé est rouvert, son état devient `reopened`. Le fait de l’assigner à nouveau le fait passer à `open`.

## États en un coup d’œil

| État | Où il apparaît | Ce qu’il signifie |
| --- | --- | --- |
| `todo` | **Voir les tickets** (**View Tickets**) | Soumis, mais non assigné. |
| `open` | **Voir les tickets** (**View Tickets**) ou **Bugs attribués** (**Allocated Bugs**) | Un travail actif est assigné. |
| `reopened` | **Voir les tickets** (**View Tickets**) ou **Bugs attribués** (**Allocated Bugs**) | Un ticket précédemment fermé a été remis en travail actif. |
| `closed` | **Archivés** (**Archived**) | Résolu ou annulé. |

## Processus étape par étape

### 1. Créer un ticket

1. Ouvrez **Ajouter un bug** (**Add Bug**).
2. Saisissez les informations obligatoires : titre, description/rapport initial, type de bug, projet et gravité.
3. Ajoutez des images au **Rapport de bug initial** (**Initial Bug Report**) si elles aident à expliquer le problème.
4. Soumettez le ticket.

Un ticket non assigné commence à l’état `todo`. Son créateur en devient l’auteur du signalement, et cette identité ne peut pas être modifiée.

Un senior humain ou un administrateur peut assigner le ticket dès sa création. Dans ce cas, il commence à l’état `open` et reçoit une heure **Assigné le** (**Assigned At**).

### 2. Consulter le travail actif

1. Ouvrez **Voir les tickets** (**View Tickets**) pour consulter les tickets actifs.
2. Ouvrez **Bugs attribués** (**Allocated Bugs**) pour vous concentrer sur les tickets qui vous sont assignés.
3. Choisissez **Voir les rapports** (**View Reports**) pour lire le **Rapport de bug initial** (**Initial Bug Report**) et, lorsqu’il est disponible, le **Rapport de solution / correctif** (**Solution / Fix Report**).

### 3. Assigner ou réassigner un ticket

1. Un senior humain ou un administrateur choisit un ticket actif.
2. Il sélectionne comme personne assignée une personne active ou un agent IA admissible.
3. Le ticket passe à l’état `open`.

L’heure **Assigné le** (**Assigned At**) n’est enregistrée que lors de la première assignation ; une réassignation ne la remplace pas. Pour les projets sensibles, la personne assignée doit être rattachée au projet. Un agent IA ne peut être assigné que si le projet compte au moins un développeur ou senior humain actif et rattaché au projet.

### 4. Consigner le travail et fermer le ticket

1. Une personne autorisée ajoute les détails de l’investigation, du correctif ou de la vérification au **Rapport de solution / correctif** (**Solution / Fix Report**).
2. Lorsque le travail est terminé, elle ferme le ticket et fournit des **Notes de résolution** (**Resolution Notes**).
3. Le ticket passe à l’état `closed` et est déplacé vers **Archivés** (**Archived**).

La fermeture enregistre l’heure de résolution et la personne qui a résolu le ticket. Le **Rapport de bug initial** (**Initial Bug Report**) d’origine reste la soumission initiale.

### 5. Rouvrir un ticket fermé

1. Ouvrez le ticket dans **Archivés** (**Archived**).
2. Rouvrez-le et indiquez un motif.
3. Poursuivez le travail tant que le ticket est à l’état `reopened`.

La réouverture efface l’heure de résolution enregistrée et l’identité de la personne ayant résolu le ticket. Elle conserve le **Rapport de solution / correctif** (**Solution / Fix Report**) existant et ses images afin que le travail antérieur ne soit pas perdu. Un ticket rouvert peut ensuite être assigné, ce qui le fait passer à l’état `open`.

> **Annulation : archiver sans solution**  
> L’interface peut appeler cette action **Annuler le ticket sans solution** (**Cancel Ticket Without A Solution**) ou **Archiver comme annulé** (**Archive As Cancelled**). Un motif est obligatoire. L’annulation n’est autorisée que s’il n’existe ni texte ni image de solution/résolution. Le ticket est enregistré avec l’état `closed`, apparaît dans **Archivés** (**Archived**) et peut actuellement être rouvert. Il ne s’agit pas d’un état distinct et permanent.

## Autorisations en un coup d’œil

| Personne | Ce qu’elle peut faire |
| --- | --- |
| Administrateur | Gérer tous les tickets, sous réserve des règles normales applicables aux tickets. |
| Senior | Gérer les tickets de projets normaux dans toute l’organisation. Pour les projets sensibles, il doit être rattaché au projet. Les seniors humains peuvent assigner et réassigner des tickets actifs. |
| Développeur | Gérer un ticket lorsqu’il en est l’auteur du signalement ou la personne assignée, sous réserve de l’appartenance au projet sensible. |
| Agent IA | Gérer un ticket lorsqu’il en est l’auteur du signalement ou qu’il lui est assigné, sous réserve de l’appartenance au projet sensible. Il ne peut pas assigner de tickets. |

Les mêmes règles d’accès s’appliquent à la lecture des commentaires : vous devez d’abord être autorisé à lire le ticket.

Dans les projets normaux, l’auteur du signalement ou la personne assignée peut conserver l’accès à ce ticket précis même après le retrait de son rattachement au projet. Dans les projets sensibles, le retrait du rattachement au projet supprime immédiatement l’accès au ticket, y compris pour l’auteur du signalement ou la personne assignée.

## Règles importantes de modification

- Les métadonnées d’un ticket ne peuvent être modifiées que tant que le ticket est actif.
- Le **Rapport de bug initial** (**Initial Bug Report**) ne peut pas être modifié lorsque le ticket est fermé. Rouvrez d’abord le ticket.
- Le **Rapport de solution / correctif** (**Solution / Fix Report**) peut être modifié par une personne autorisée même après la fermeture.
- Si quelqu’un d’autre enregistre une modification avant vous, vous devrez peut-être actualiser la page et réessayer votre modification.

## Travailler en toute sécurité lorsque plusieurs personnes mettent à jour un ticket

Plusieurs personnes peuvent consulter le même ticket en même temps. Pour éviter que la mise à jour d’une personne ne remplace silencieusement le travail d’une autre, chaque ticket possède un **numéro de version** caché.

### Rôle du numéro de version

1. Lorsque vous ouvrez un ticket, l’application reçoit également son numéro de version actuel.
2. Lorsque vous enregistrez une modification, l’application envoie ce numéro de version avec la modification.
3. Si personne n’a modifié le ticket entre-temps, la modification est enregistrée et le ticket reçoit un nouveau numéro de version.
4. Si quelqu’un d’autre a enregistré une modification auparavant, l’application bloque l’enregistrement au lieu d’écraser son travail plus récent.

Cela peut se produire lorsque deux personnes modifient le ticket, changent l’assignation ou les métadonnées, ferment un ticket, le rouvrent ou effectuent une autre action presque au même moment.

### Que faire en cas de conflit

1. Lisez le message indiquant que le ticket a changé pendant que vous travailliez.
2. Actualisez ou rouvrez le ticket pour charger la version la plus récente.
3. Examinez les modifications de l’autre personne avant de décider de ce qui doit être conservé.
4. N’appliquez de nouveau votre modification que si cela reste approprié.

L’application ne fusionne pas automatiquement deux modifications différentes apportées au même texte de rapport. Examiner d’abord la version la plus récente du ticket aide à éviter la perte accidentelle d’informations utiles.

### Assignations en masse et autres actions de groupe

Lors de l’assignation simultanée de plusieurs tickets, chaque ticket est vérifié séparément. Les tickets qui n’ont pas changé peuvent être mis à jour. Les tickets modifiés par quelqu’un d’autre sont signalés comme des conflits afin qu’ils puissent être actualisés et traités individuellement. Cela évite qu’une action groupée ne remplace silencieusement un travail plus récent.

## Questions fréquentes

### Puis-je changer l’auteur du signalement ?

Non. La personne qui crée le ticket reste l’auteur du signalement.

### Pourquoi mon nouveau ticket est-il `todo` plutôt que `open` ?

Il a été créé sans personne assignée. Un senior humain ou un administrateur peut assigner un ticket actif pour le faire passer à l’état `open`. Un ticket peut aussi être assigné directement sur l’écran de création de bug s’il est créé par un développeur senior ou un administrateur.

### Tout le monde peut-il assigner un ticket ?

Non. Seul un senior humain ou un administrateur peut assigner ou réassigner des tickets actifs.

### Puis-je modifier un ticket fermé ?

Vous pouvez modifier le **Rapport de solution / correctif** (**Solution / Fix Report**) si vous y êtes autorisé. Pour modifier le **Rapport de bug initial** (**Initial Bug Report**) ou des métadonnées modifiables uniquement lorsqu’un ticket est actif, rouvrez d’abord le ticket.

### Que se passe-t-il si je perds mon rattachement au projet ?

Pour un projet sensible, l’accès à ses tickets s’arrête immédiatement. Pour un projet normal, vous pouvez toujours avoir accès à un ticket précis si vous en êtes l’auteur du signalement ou la personne assignée.

### Un ticket annulé a-t-il disparu définitivement ?

Non. Il est archivé comme un ticket fermé avec un motif d’annulation et peut actuellement être rouvert.
