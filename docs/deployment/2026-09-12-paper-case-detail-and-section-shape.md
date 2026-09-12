# Paper case detail and derived section shape — DEV deployment, 2026-09-12

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only**, per the project owner direction of
2026-09-12 that every environment write targets DEV until another environment is named.

Commits: `9f4e968` (import rebuild, **not deployed** — see below), `8dc458d` (portal).
Decisions: AD-124, OD-049 to OD-052.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `pushwebtemplate … a1000000-0000-4000-8000-000000000015` | OT Case Detail, **30712 → 31813 chars** |
| 2 | `pushwebtemplate … a1000000-0000-4000-8000-00000000001b` | OT Review Detail, **82389 → 84951 chars** |

OT Review Detail's 82389 starting size matches exactly what
`2026-09-12-section-rules-foundation.md` left it at, so this built on the deployed version
rather than over someone else's.

Both are Liquid-only and reference no column that does not already exist, which is what
made them safe to deploy while the import half is not. Checked before pushing: Liquid tags
balanced, no Liquid tag delimiters inside comment blocks — the failure that takes a page
down — and HTML element balance identical to the committed version.

## What was deliberately not deployed, and why

**The whole import half of commit `9f4e968`.** The plug-in assembly and the Code App are
both held back, together, because the schema they need is not in DEV. Probed directly
rather than assumed:

```
fetch al_outcomecase / attribute al_checklistitems
  -> 0x80041103 'al_OutcomeCase' entity doesn't contain attribute
     with Name = 'al_checklistitems'
```

Pushing the assembly first would not degrade gracefully: `ImportRules` now writes
`al_servicecaseref`, `al_checklistitems`, `al_iooutcome` and thirteen others, and a create
naming an attribute the table does not have fails the row outright. Every imported row
would fail. The Code App is held with it: it posts CSV under IO's own headers, which the
currently deployed `ImportRules` reads as a file with no "IO reference" column and refuses
as fatal — a clear error rather than corruption, but the upload path would be broken for
anyone using it in the meantime.

Order when it does go: **schema, then assembly, then Code App.**

## The schema cannot be deployed by importing the solution

`src/Entities/al_OutcomeCase/Entity.xml` was hand-written in commit `9f4e968`, and that is
the wrong direction of travel for this repo. The established flow —
`2026-09-12-section-rules-foundation.md`, steps 1 to 7 — creates each column through the
registration CLI, adds it to the solution with `addmetadatatosolution`, and only then
regenerates the repo copy with `pac solution export` → `unpack` → copy back. The repo file
is the *output* of a deployment, not its input; a solution import is also where AD-?/the
2026-09-02 incident switched six plug-in steps off when step XML arrived without
`--activate-plugins`.

So the committed `Entity.xml` should be treated as a specification of the seventeen columns
to create, and expected to be **overwritten** by the export once they exist.

## What the registration CLI is missing

Of the seventeen columns, the CLI can create seven:

| Need | Verb | Status |
|---|---|---|
| `al_checklistitems` (memo) | `addmemocolumn` | available |
| `al_iotaskstatus`, `al_iooutcome` (choice) | `addchoicecolumn` | available |
| `al_iocompleteddate`, `al_taskstartdate`, `al_iocreateddate` (DateOnly) | `adddatecolumn` | available |
| **10 text columns** | — | **no verb exists** |
| `al_checklistcompleteddate` (**with a time**) | `adddatecolumn` | creates **DateOnly** only |

The ten are `al_servicecaseref`, `al_clientref`, `al_adviseremail`, `al_iocompletedby`,
`al_iocreatedby`, `al_tasktype`, `al_workflowname`, `al_servicestatus`,
`al_checklistcompletedby`, and the extract's own paraplanner value now lands on the
existing `al_paraplanner` so it needs nothing.

Two additions are needed before the schema can be created, following `AddMemoColumn` the
way `adddatecolumn` and `addboolcolumn` did on 2026-09-12:

1. **`addtextcolumn <org> <table> <SchemaName> <maxLength> "<display>" "<description>"`**,
   with the created column read back.
2. **A behaviour argument on `adddatecolumn`**, because `al_checklistcompleteddate` carries
   a time and the extract carries no offset, so it wants `TimeZoneIndependent`. A column
   that arrived as `DateOnly` would drop the time silently, and one that arrived as
   `UserLocal` would look identical in every listing and be wrong only at a time-zone
   boundary — the reason the date verb reads its behaviour back in the first place.

Creating columns is also close to one-way: Dataverse can delete a custom column, but doing
so on a table carrying data is destructive and is not something to undo a mistake with. So
the seventeen names, types, lengths and option values are worth a read against
`Entity.xml` before any of them is created.

## Verified

Automated, before the push: **754 plug-in tests**, **468 app tests**, `npx tsc -b` clean,
`eslint src` 0 errors.

**Not covered.** Neither template has a test and neither was rendered — nothing here
proves what the pages look like. Two things want a human look now that they are in DEV:

- `/case-details/?id=…` drawn as the document, and
- a section added through checklist administration whose questions are Pass/Fail, which is
  the case the derived shape exists for. The portal caches web templates, so a hard reload
  or a site cache clear may be needed before either change appears.
