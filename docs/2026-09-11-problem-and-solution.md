# Outcome Testing

Problem and solution, in short.
Ascot Lloyd · 11 September 2026

---

## The problem

Ascot Lloyd must evidence that the advice its advisers give is suitable.
That review runs today on **Word documents, Excel trackers and email**.

---

## What that costs us

- **Intake is unguarded.** Intelligent Office extracts arrive with no validation gate.
- **Allocation lives in inboxes.** Who is checking what is not a fact anyone can query.
- **Checklists are not versioned.** We cannot prove which question set a file was graded against.
- **Outcomes get overwritten.** A regrade erases the original — the exact record a regulator asks for.
- **Remediation is chased by email.** No sign-off lock, no closure trail.
- **MI and the Trail Light export are hand-assembled**, every time.

---

## The risk underneath

We cannot evidence a consistent, auditable review process,
and we cannot trace a failed file to the remediation that fixed it.

That is a regulatory exposure, not an efficiency problem.

---

## The solution

**Dataverse as the system of record**, replacing the documents with a relational,
versioned, audited model — and two purpose-built front ends over it.

---

## One data model

Outcome Case → Review Instance → Response

- A single Review Instance carries `reviewType` — Tax or AQS — rather than two parallel tables.
- Checklist → Version → Section → Question → Question Version, delete restricted at every level.
- Fail Reasons attach many-to-many, because one answer can fail for several reasons.
- Documents stay in Intelligent Office and are referenced, not copied.

---

## A lifecycle that is enforced, not documented

`Imported → Ready for Allocation → Queued → Assigned → Review In Progress → Submitted → Awaiting Remediation | Closed → …`

- `CaseLifecycle` in the plug-in assembly gates **every** status write, server-side.
- The Code App offers a manager only the transitions the command would accept.
- `Closed` and `No Check Required` are terminal.

---

## The record we could not keep before

- **Initial and final outcomes are both preserved** on `al_Outcome`. A regrade never erases what was first recorded.
- Reopening a closed outcome is a privileged T&C Manager correction, with a **mandatory reason on an immutable Audit Event**.
- Historical question versions and responses are retained, so every grade stays explainable.

---

## Two front ends, split by persona

| Audience | Surface |
|---|---|
| Managers and administrators | **Power Apps Code App** — allocation, imports, question admin, security, audit, MI and export |
| Checkers, advisers, T&C Managers | **Power Pages portal** — where checks and the whole remediation loop are performed |

One dataset. Business logic lives in shared server-side commands, never duplicated per front end.

---

## Deliberate choices, not gaps

- **Allocation is manual.** A lead assigns, or a checker claims a queued case. No skills routing, no auto-allocation.
- **Export is manual and on-demand**, Trail Light-compatible.
- **SFTP automation and Power BI are out of MVP scope** — re-raised as backlog, not reversed.
- Notifications are **email only**.

---

## Where it stands

Of the ten must-haves in the 8 September requirements pack:
**six built, two partial, two not started.**

The pack is largely a description of what already exists.
The distance left is **a second environment and a few workflow decisions — not features.**

---

## What we need decided

| | |
|---|---|
| **OD-041** | Where does the supervisor record the final regraded outcome? |
| **OD-042** | The landing-page routing rule |
| **OD-046** | Notification wording and sender — the last thing between PP-15 and done |
| **OD-045** | Name the defect tracker; today the acceptance criterion has no mechanism behind it |

---

## Next

1. Close the four decisions above.
2. Sign off DEV.
3. Install the corrected managed solution into **TEST**.
