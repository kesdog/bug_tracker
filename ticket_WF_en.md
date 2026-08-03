# Ticket Workflow

## Purpose and scope

This guide explains how a bug ticket moves from submission to archive in the Bug Tracker. It is for people using the application, not for technical setup or API use.

## Key terms

| Term | Plain-language meaning |
| --- | --- |
| Reporter | The person who created the ticket. This does not change. |
| Assignee | The person or AI agent currently responsible for work on the ticket. |
| Initial Bug Report | The original submitted description and any submitted images. |
| Solution / Fix Report | The record of investigation and resolution work, including its images. |
| Resolution Notes | The notes required when closing a ticket. |
| Active | A ticket that is `todo`, `open`, or `reopened`. |
| Archived | A ticket that is `closed`, including a cancelled ticket. |

## Ticket lifecycle

```text
                         assign or reassign
                              +-----------+
                              |           v
Create unassigned --> [ TODO ] --------> [ OPEN ]
                              ^              |
                              |              | close with Resolution Notes
                              |              v
                              +-------- [ CLOSED / ARCHIVED ]
                                reopen with a reason   |
                                                       |
                         cancel without a solution ---+
```

When a closed ticket is reopened, its status becomes `reopened`. Assigning it again changes it to `open`.

## Statuses at a glance

| Status | Where it appears | What it means |
| --- | --- | --- |
| `todo` | View Tickets | Submitted but not assigned. |
| `open` | View Tickets or Allocated Bugs | Active work is assigned. |
| `reopened` | View Tickets or Allocated Bugs | A previously closed ticket has been returned to active work. |
| `closed` | Archived | Resolved or cancelled. |

## Step-by-step workflow

### 1. Create a ticket

1. Open **Add Bug**.
2. Enter the required information: title, initial description/report, bug type, project, and severity.
3. Add images to the **Initial Bug Report** if they help explain the problem.
4. Submit the ticket.

An unassigned ticket starts as `todo`. The creator becomes its reporter and that reporter identity cannot be changed.

A human senior or admin may assign the ticket while creating it. In that case, it starts as `open` and receives an **Assigned At** time.

### 2. Review active work

1. Open **View Tickets** to review active tickets.
2. Open **Allocated Bugs** to focus on tickets assigned to you.
3. Choose **View Reports** to read the **Initial Bug Report** and, where available, the **Solution / Fix Report**.

### 3. Assign or reassign a ticket

1. A human senior or admin chooses an active ticket.
2. They select an active person or eligible AI agent as assignee.
3. The ticket becomes `open`.

The **Assigned At** time is recorded only on the first assignment; reassignment does not replace it. For sensitive projects, the assignee must be allocated to the project. An AI agent can be assigned only when the project has at least one active allocated human developer or senior.

### 4. Record the work and close the ticket

1. An authorized person adds investigation, fix, or verification details to the **Solution / Fix Report**.
2. When the work is complete, they close the ticket and provide **Resolution Notes**.
3. The ticket becomes `closed` and moves to **Archived**.

Closing records the resolution time and the person who resolved it. The original **Initial Bug Report** remains the original submission.

### 5. Reopen a closed ticket

1. Open the ticket in **Archived**.
2. Reopen it and provide a reason.
3. Continue work while the ticket is `reopened`.

Reopening clears the recorded resolution time and resolver. It keeps the existing **Solution / Fix Report** and its images so earlier work is not lost. A reopened ticket can later be assigned, which changes it to `open`.

> **Cancellation: archive without a solution**  
> The UI may call this **Cancel Ticket Without A Solution** or **Archive As Cancelled**. A reason is required. Cancellation is allowed only when there is no solution/resolution text or image. The ticket is stored as `closed`, appears in **Archived**, and currently can be reopened. It is not a separate permanent status.

## Permissions at a glance

| Person | What they can do |
| --- | --- |
| Admin | Manage all tickets, subject to the normal ticket rules. |
| Senior | Manage normal-project tickets across the organization. For sensitive projects, they need project allocation. Human seniors can assign and reassign active tickets. |
| Developer | Manage a ticket when they are its reporter or assignee, subject to sensitive-project membership. |
| AI agent | Can manage a ticket when it is its reporter or assigned ticket, subject to sensitive-project membership. It cannot assign tickets. |

The same access rules apply when reading comments: you must be allowed to read the ticket first.

For normal projects, a reporter or assignee can retain access to that exact ticket even after their project allocation is removed. For sensitive projects, removing project allocation removes ticket access immediately, including for a reporter or assignee.

## Important editing rules

- Ticket metadata can be changed only while the ticket is active.
- The **Initial Bug Report** cannot be edited while the ticket is closed. Reopen it first.
- The **Solution / Fix Report** may be edited by an authorized user even after closure.
- If someone else saves a change first, you may need to refresh and retry your change.

## Working safely when several people update a ticket

More than one person can view the same ticket at once. To avoid one person's update silently replacing another person's work, each ticket has a hidden **version number**.

### What the version number does

1. When you open a ticket, the app also receives its current version number.
2. When you save a change, the app sends that version number with the change.
3. If nobody changed the ticket first, the change is saved and the ticket receives a newer version number.
4. If someone else saved a change first, the app stops the save rather than overwriting their newer work.

This can happen when two people edit the report, change assignment or metadata, close a ticket, reopen it, or take another action at nearly the same time.

### What to do when you see a conflict

1. Read the message explaining that the ticket changed while you were working.
2. Refresh or reopen the ticket to load the newest version.
3. Review the other person's changes before deciding what should be kept.
4. Apply your change again only if it is still appropriate.

The app does not automatically merge two different edits to the same report text. Reviewing the latest ticket first helps prevent accidental loss of useful information.

### Bulk assignments and other group actions

When assigning several tickets at once, each ticket is checked separately. Tickets that have not changed can be updated. Tickets changed by somebody else are reported as conflicts so they can be refreshed and handled individually. This prevents a bulk action from silently replacing more recent work.

## Common questions

### Can I change the reporter?

No. The person who creates the ticket remains the reporter.

### Why is my new ticket `todo` instead of `open`?

It was created without an assignee. A human senior or admin can assign an active ticket to move it to `open`. A ticket can also be directly assigned atthe bug creation screen if its made by a senior dev or an admin. 

### Can anyone assign a ticket?

No. Only a human senior or admin can assign or reassign active tickets.

### Can I edit a closed ticket?

You can edit the **Solution / Fix Report** if you are authorized. To edit the **Initial Bug Report** or active-only metadata, reopen the ticket first.

### What happens if I lose project allocation?

For a sensitive project, access to its tickets stops immediately. For a normal project, you may still have access to an exact ticket if you are its reporter or assignee.

### Is a cancelled ticket gone forever?

No. It is archived as a closed ticket with a cancellation reason, and it can currently be reopened.
