# Checklist administration UI — 2026-09-12

Target: `Env_AQ_Dev` only. Nothing was deployed by this work — the Code App is built and run,
not pushed — but the commands behind it were exercised against DEV.

Plan: `docs/superpowers/plans/2026-09-12-checklist-administration-ui.md`, tasks 1–7.
Decisions: AD-122, AD-123. Follows `2026-09-12-checklist-administration-commands.md`.

---

## What the library can now do

An administrator holding Edit on `question.retire` can, from the Question library:

- **add a question** to a section, and **edit** its wording, response type, mandatory flag and
  display order — which creates a new version and retires the current one;
- **move a question** to another section, which retires it where it is and creates it in the
  target under a new code;
- **retire a question**, with a required reason and a stops-from date;
- **add a section** with its questions in one transaction, owned by **Tax, AQS or Both**;
- **edit a section** — name, help text, team, display order, optional or required;
- **retire a section**.

Verified: `npx tsc -b` clean, **419 app tests**, `eslint src/features/admin/` clean,
`npm run build` succeeds.

## The Both-team proof, which two earlier plans could not make

The foundation plan (2026-09-12, section rules) could not prove that a Both section reaches
both disciplines, because nothing could set a section to Both — there is no generic row-update
verb and one was not invented to run a test. `al_AddSection` closes that.

A section `S-BOTHPROOF` was created owned by Both, with one mandatory question, and queried
with the exact section filter each front end uses:

| Filter | Tax review | AQS review |
|---|---|---|
| **New** — owner role as membership, plus the effective-date window | **sees it** | **sees it** |
| **Old** — owner role by strict equality | does not see it | does not see it |

The contrast is the point, and it is worse than predicted. The prediction was that a Both
section would render and then refuse answers; in fact, under the old equality filter a Both
section renders to **nobody**. Every one of the five readers the foundation plan changed had
to change, or the value would have been unusable rather than half-usable.

The section was retired after the check. The count of sections in force for AQS is back to
**10**, its baseline.

## What is still not proven, and is the owner's step

**The browser walkthrough.** Driving the running app needs an authenticated Power Apps
session, which cannot be established from this environment. So the controls are proven to
compile, lint, typecheck and call commands that work against DEV — but nobody has yet clicked
them. Run `npm run dev` in `app/` and exercise each control once.

**An answer saved into a Both section from a live review.** The data layer is proven above and
`ResponseGuardPlugin.SectionRefusal` is unit-tested to accept Both from either discipline, but
no answer has been written through a real review instance. That needs a review open in each
discipline against a checklist version holding a Both section.

**The `question.retire` permission rule.** Unchanged from the commands deployment: every call
so far has run as `svc.automate.aq`, a System Administrator, which `PermissionHelpers` has a
deliberate break-glass for. Whether a non-administrator holding the Administrator app role can
use any of this depends on a stored `al_pagepermission` row that nothing has exercised. This
is the most likely thing to surface on a real administrator's first attempt.

## One piece of debt

`app/src/generated/models/Al_sectionsModel.ts` predates AD-123 and carries neither the three
new columns nor the Both owner-role value. The three columns are declared locally in
`useQuestionLibrary.ts` and `OWNER_ROLE_LABEL` is consulted before the generated choice map,
which would otherwise render Both as "Unassigned". Generated files are refreshed by
regenerating from Dataverse, never hand-edited (AD-032 records the same lag on
`al_auditevent`), and `pa app add data-source --table al_section` needs an interactive prompt.
Running it will let the local `SectionSchedule` type be deleted.
