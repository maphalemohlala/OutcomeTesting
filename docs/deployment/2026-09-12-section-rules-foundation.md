# Section rules foundation — DEV deployment, 2026-09-12

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12: every environment write targets DEV until the owner names
another environment for that specific promotion.

Plan: `docs/superpowers/plans/2026-09-11-checklist-administration-foundation.md`, tasks 1–7.
Spec: `docs/superpowers/specs/2026-09-11-checklist-administration-design.md`.
Decisions: AD-122, AD-123.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `adddatecolumn … al_EffectiveFrom` | `al_section.al_effectivefrom`, behaviour **DateOnly** |
| 2 | `adddatecolumn … al_EffectiveTo` | `al_section.al_effectiveto`, behaviour **DateOnly** |
| 3 | `addboolcolumn … al_IsOptional false` | `al_section.al_isoptional`, default **False** |
| 4 | `addoptionvalue al_section al_ownerrole 120910105 "Both"` | inserted and published, **6 values** (was 5), exit 0 |
| 5 | `addmetadatatosolution al_section al_ownerrole` | attribute added to `OutcomeTesting` |
| 6 | `pac solution export` → `unpack` → copy back `Entities/al_Section/` | **149 lines added, 0 removed** |
| 7 | `pac solution pack --folder src` | succeeds, only the expected `CanvasApps` warning (AD-012) |
| 8 | `dotnet build -c Release` then `pushassembly` | **191488 bytes**, matching the local build exactly |
| 9 | `pushwebtemplate … a1000000-0000-4000-8000-00000000001b` | OT Review Detail, **80472 → 82389 chars** |

`al_section` now carries effective dates, an optional flag and a Both owner role; both
plug-ins and the portal template read them. The Code App half is code only and needs no
deployment.

## Two verbs were added to do this

The registration CLI could create memo and choice columns and nothing else. `adddatecolumn`
and `addboolcolumn` follow `AddMemoColumn`, read-back included. Dates are created with
`DateTimeBehavior.DateOnly` and the behaviour is read back as well as the column, because a
column that arrived as `UserLocal` would look identical in every listing and be wrong only at
a time-zone boundary — which is exactly where nobody is looking.

## Verified, and what the verification does not cover

Automated: **706 plug-in tests**, **390 app tests**, `npx tsc -b` clean.

Against DEV, read-only:

- the new section FetchXML parses — a nested `<condition operator="in">` inside a
  link-entity filter is the part most likely to be rejected;
- it returns the **same 10 AQS sections** the old equality-only filter did, which is what it
  must do until a section carries dates or the Both role;
- the date operators discriminate on real data: `Q-TAX-02` resolves to **v1 as of
  2026-08-27** and **v3 as of 2026-09-12**.

**Not covered, and it is the owner's step.** No section has been dated out, marked optional
or set to Both, because the CLI has no generic row-update verb and one was not invented to
run a test. So a retired section actually disappearing from a live review, and a Both section
rendering in both disciplines with answers saving in each, are unproven end to end. Setting a
section row in the maker portal and clearing the site cache is what proves them.

## A counting trap, recorded because it nearly passed as evidence

The whole-table count of question versions in force is **46 at three different dates**, which
reads like a filter doing nothing. It is arithmetic: the not-yet-started successors excluded
at an early date exactly offset the then-live versions included. The filter is discriminating
correctly — comparing which versions come back, rather than how many, is what shows it.
Compare identities, not totals.

## Four CLI signatures the plan had wrong

All four were written from the assumption that this CLI gates writes uniformly behind
`--confirm`. It does not: the metadata-creating verbs take it, the rest do not, and each
would have swallowed the flag as a positional argument.

| Verb | Actual signature | What `--confirm` would have done |
|---|---|---|
| `addoptionvalue` | `<orgUrl> <entity> <attribute> <value> <label> [<description>]` | set the option's description to the literal `"--confirm"` |
| `addmetadatatosolution` | `<orgUrl> <entity> [<attribute>]` | asked for an attribute named `--confirm` |
| `pushassembly` | `<orgUrl> [<dllPath>]` | failed with "Plug-in assembly not found" |
| `pushwebtemplate` | `<orgUrl> <powerpagecomponentid> <path>` | needs the component **GUID**, not a name |

Only `pushassembly` would have failed loudly. The first two would have written something
slightly wrong and reported success.

## Two facts about the data worth keeping

**There are 12 sections, not nine.** S-TAX, S-AMLCRA, S-FQTAX, S-FQOUT, S-E1…E5, S-CRP, S-CD,
S-GRADE.

**`al_isoptional` is null on all 12, not false.** Dataverse applies a boolean default to new
rows only and does not backfill; zero rows match `eq 0`. Every reader therefore treats absent
as *required*, which is the safe direction and is pinned by a test. A query written
`al_isoptional eq false` matches nothing — filter `ne true`, or judge it in memory. No
backfill was done, deliberately: the null path is the one the tests cover.

## Resting state

The new code is live in DEV and no data uses it, so nothing behaves differently yet. That is
the intended state — the data is what activates it, and the commands that write that data are
plan two.
