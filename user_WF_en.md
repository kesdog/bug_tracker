# User and Access Workflow

## Purpose and scope

This guide explains user roles, project access, human-user setup, and AI-agent access in the Bug Tracker. It is written for non-technical users and administrators.

## Start with the access model

### Roles and user types

| Item | Meaning |
| --- | --- |
| Developer (`dev`) | A human or AI user who works in projects they are allocated to. |
| Senior | A human user with broader normal-project access and defined project-management responsibilities. |
| Admin | A human user with organization-wide administration access. |
| Human | A person who signs in with email and password. |
| AI agent | A non-human user that signs in with a username and oath token. |

Only an admin can change a human user's role. An admin cannot demote themseves or other admins.

### Projects and allocations

| Concept | Meaning |
| --- | --- |
| Normal project | Seniors can see it across the organization. Developers and AI agents need allocation to discover or create tickets in it. |
| Sensitive project | Access requires project allocation for non-admin users, including seniors. |
| Allocation | The official project membership record. Allocations are the source of truth for access. |
| Owner | One human senior or admin who is the access contact for the project. Ownership does not bypass ticket or project access rules. |

Developers and AI agents can discover projects and create tickets only in projects where they are allocated. Admins have global access.

## Human users

### Request and set up a human account

1. A person who is not signed in submits an **Access Request** with their human-user request and email address.
2. An admin reviews the request.
3. If approved, the admin may adjust the username and sends a setup link.
4. The person uses the setup link within 30 minutes to set a password. ( an email service can be hooked up to deliver to users in an organisation)
5. The new account begins as a human developer.
6. The user signs in with their email address and password.

Human sessions expire after 24 hours or 45 minutes of innactivity. Sign in again when the session expires.

### Manage human roles

1. An admin reviews the user and their required responsibilities.
2. The admin changes the role when needed: developer, senior, or admin.
3. The admin confirms that the user has the needed project allocations.

Only an admin changes roles. A user cannot promote themself, and an admin cannot demote themselves.

### Create and manage projects

#### Create a project

1. A senior or admin creates a project.
2. The creator becomes the initial owner and receives an allocation to the project.
3. Keep the owner allocated while they own the project.

A senior can create only a normal project. Only an admin can create a sensitive project or change project visibility.

#### Transfer ownership

1. Choose an eligible new owner.
2. Transfer the ownership before removing the current owner allocation.
3. Confirm the new owner remains allocated.

An admin can transfer ownership for any project. A senior can transfer a normal project to an eligible senior or admin. Sensitive-project ownership must be held by an admin.

### Manage allocations

1. Identify the project and the user who needs access.
2. Add or remove the user's allocation.
3. Check ticket work and ownership before removing access.

Admins manage allocations for all projects. Seniors manage membership on normal projects they can access. Access-request approvals can be made by an admin or by the senior owner of a normal project.

Removing allocation from a sensitive project immediately removes the user's access to its tickets, including tickets they reported or were assigned. In a normal project, a reporter or assignee may retain access to that exact ticket after allocation removal.

### Reduce a human user's access

1. Remove unneeded project allocations.
2. Transfer any project ownership before removing the owner's allocation.
3. If appropriate, an admin changes the person's role.

There is no general deactivate-or-delete-user workflow. Use allocation and role changes to reduce project responsibilities; this does not describe deleting an active account.

## AI agents

### Provision an AI agent

There are two ways to start: an AI agent requests `ai_agent` access, or an admin creates the agent directly.

1. An admin approves the request or creates the agent.
2. The admin chooses the agent username and issues an oath token.
3. Store the oath token securely when it is shown. It is shown only when issued or reissued.
4. Set the oath-token validity from 1 to 62 days; the default is 30 days.
5. Allocate the agent to the projects where it should work.
6. The agent signs in with its username and oath token.

An AI agent starts as a developer. Its bearer session lasts until the oath token expires. Contact email addresses are redacted for agents.

### What an AI agent can and cannot do

An agent can discover and create tickets only in allocated projects. It can manage tickets where it is the reporter or assignee, but sensitive-project membership is still required.

An agent cannot create or change projects, allocations, ownership, or ticket assignments.

### Handle an AI work notification

1. Receive a ticket work notification.
2. Fetch and read the full ticket before acting, using the latest ticket version.
3. Do the safe work that the ticket allows.
4. If blocked or unsure that a change is safe, add a comment with findings, the blocker, and the next action needed from a human.
5. Only after handling the work or documenting the blocker, mark the notification as read.

If the agent is denied access to a ticket, it should request project access and wait for approval. After reconnecting, it should check unread notifications to recover work missed while offline.

### Reissue or reduce AI access

1. Reissue an oath token when a replacement credential is needed; the old oath token cannot be used for future sign-ins.
2. Remove an approved AI access request when future oath-token sign-in must be prevented.
3. Remove project allocations when the agent no longer needs a project.
4. Use logout to revoke an existing bearer session.

Removing a sensitive-project allocation immediately blocks access to that project's tickets. It does not explicitly revoke an already issued bearer token, but every request is checked again for authorization. There is no general deactivate-or-delete-user workflow; use credential and access changes rather than describing this as account deletion.

## Common questions

### Who can see a sensitive project?

Admins can access it. Other users, including seniors, need an allocation to it.

### Does being a project owner give unlimited access?

No. The owner is the access contact. Ownership does not bypass ticket authorization or allocation rules.

### Can a senior create a sensitive project?

No. Only a human admin can create sensitive projects or change a project's visibility.

### What should happen before removing an owner's access?

Transfer ownership first, then remove the former owner's allocation if appropriate.

### Can an AI agent assign itself a ticket?

No. AI agents cannot create or change assignments. A human senior or admin handles assignment.

### Does removing an agent from a project log it out everywhere?

No. Removing allocation changes authorization for project requests, especially immediate sensitive-project access. Use logout to revoke a bearer session, and reissue or remove oath-token access when credential access must be stopped.
