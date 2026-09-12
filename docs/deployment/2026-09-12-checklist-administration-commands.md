# Checklist administration commands — DEV deployment, 2026-09-12

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD untouched, by project owner
direction of 2026-09-12.

Plan: `docs/superpowers/plans/2026-09-12-checklist-administration-commands.md`, tasks 1–10.
Decisions: AD-122, AD-123. Follows `2026-09-12-section-rules-foundation.md`.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `addcommandvalue` ×6 | `al_command` 120910793–798, **24 → 30 values** |
| 2 | `dotnet build -c Release` then `pushassembly` | **211968 bytes**, matching the local build |
| 3 | `registerall` | Six Custom APIs created — **failed twice first, see below** |
| 4 | `pac solution add-solution-component` ×6 | All six added with required components |
| 5 | `callapi` ×9 | Every command exercised; see the table below |
| 6 | `pac solution export` → unpack → copy back | Six `customapis/` folders, six values on `al_auditevent` |
| 7 | `pac solution pack --folder src` | Succeeds, only the expected `CanvasApps` warning |
| 8 | `pa app add dataverse-api` ×6 | Data sources registered; `operations.test.ts` green |

Automated: **753 plug-in tests**, **396 app tests**, `npx tsc -b` clean.

## Two undocumented field caps, which is why `registerall` failed twice

The 2026-09-07 note recorded `registerall` being refused "on field lengths that appear in
neither the contract schema nor the error". The errors do name them, and they are:

| Field | Cap | What breached it |
|---|---|---|
| `customapi.description` | **300** | `al_MoveQuestion` at 323 |
| `customapiresponseproperty.description` | **100** | `al_UpdateSection.Changes` at 103 |

**Request parameter descriptions are not capped at 100.** An existing registered one runs to
285, and `al_UpdateSection`'s 141-character `OwnerRole` registered cleanly in the same run
that rejected a 103-character response property. Only response properties need the short
form.

`registerall` is safe to re-run after a failure: it had already created some plug-in types
and Custom APIs before throwing, and the re-run updated rather than duplicated them.

## Every command exercised against DEV

A generic `callapi` verb was added to the registration CLI for this. The tool could invoke
Custom APIs only through purpose-built verbs, so without it the first real call to any of the
six would have come from the UI — where a plug-in fault and a UI fault look alike.

| Command | Exercised | Result |
|---|---|---|
| `al_AddSection` | With two questions, owned by **Both** | Section + 2 questions in one transaction |
| `al_AddSection` | With no questions | Section created, `QuestionIds` empty |
| `al_AddQuestion` | Into that section | Question + v1 |
| `al_AddQuestion` | **Replayed on the same key** | **Identical ids returned; nothing written twice** |
| `al_RetireQuestion` | Against `Q-GR-01` | **Refused**, naming what would break |
| `al_MoveQuestion` | To the second scratch section | Old version dated out, new question + v1 created |
| `al_UpdateSection` | Rename + team + optional | `Name: … -> …; Owner role: '120910105' -> '120910101'; Optional: 'No' -> 'Yes'` |
| `al_UpdateSection` | **No actual change** | **Empty `Changes`, empty `AuditEventId` — nothing written** |
| `al_RetireQuestion` | The moved question | Version dated out |
| `al_RetireSection` | Both scratch sections | Both dated out |

The protected-code refusal came back in full:

> `PRECONDITION: Q-GR-01 carries the advice quality grade, which OutcomeRules maps to the case outcome. Without it no case can be graded. It cannot be retired without a code change.`

**DEV was left clean.** `S-SCRATCH` and `S-SCRATCH2` both carry `al_effectiveto` of
2026-09-12, and the count of sections in force for AQS is back to **10**, exactly as before.

## What this did NOT prove

**The `question.retire` permission rule is still untested.** Every call above ran as
`svc.automate.aq`, a System Administrator, and `PermissionHelpers.EnsureAppPermission` has a
deliberate break-glass for that role. So the commands are proven to work; whether a
*non-administrator* holding the Administrator app role can use them depends on a stored
`al_pagepermission` row granting `question.retire` Edit, and nothing here exercised that
path. Confirm it before anyone relies on these from the UI.

## Unrelated drift found and fixed

The round trip found `src/Entities/al_CaseAssignment/Entity.xml` carrying
`ChangeTrackingEnabled=0` while DEV has `1` — almost certainly from the 2026-09-09 portal
cache work, never committed. `src/` is what promotes, so a managed install built from it
would have switched change tracking back off on that table. Corrected from the export and
committed separately (`fccfb6e`) so it stays visible rather than buried.

## Resting state

All six commands are live in DEV, registered in the solution, and callable from the Code App
through typed wrappers. Nothing in the UI calls them yet — that is
`docs/superpowers/plans/2026-09-12-checklist-administration-ui.md`.
