# Para-planner from AssignedBy, and unmatched names reported

**Date:** 2026-09-20
**Decisions:** AD-160, AD-161
**Audit items:** Fixes 7, finding 7
**Environments changed:** none yet — this is a code change; see *Before the next import* below.

---

## What changed

`AssignedBy` now fills `al_paraplanner`. `AssignedTo` is no longer imported anywhere.

Both front ends carry the map and both had to move together — the Code App transcribes the
workbook and the plug-in parses the CSV, so a mapping changed in one and not the other would
import different people depending on which route the upload took:

| File | Line |
|---|---|
| `plugins/OutcomeTesting.Plugins/ImportRules.cs` | `new ColumnDef("AssignedBy", "al_paraplanner", …)` |
| `app/src/features/imports/caseUpload.ts` | `{ header: 'AssignedBy', field: 'al_paraplanner', … }` |

`ImportRules.ParaplannerAttribute` now names the column once, because the match check and the
column map were one edit away from drifting apart.

The import also resolves the name against Contact as it writes, and reports any row whose
para-planner cannot be addressed as **`Imported (para-planner unmatched)`**, with the reason
and the value. The row still imports.

---

## Before the next import — read this

**Re-importing does not fix existing cases.** BR-001 keys on `TaskID`, and a second upload of
the same task is reported `Duplicate (skipped)` and writes nothing. So any environment that
imported under the old mapping still holds **the checker's name in `al_paraplanner`**, and
will keep holding it until someone changes it deliberately.

That matters because `al_paraplanner` is who gets the Pass and Remediation letters about a
client's advice outcome. A wrong name there is a misaddressed letter, not a cosmetic defect.

| Environment | State | Action |
|---|---|---|
| **DEV** | Holds IO-imported test records under the old mapping | None required — the project owner confirmed these are test records they will sort manually |
| **TEST** | Not yet installed | Import after this change lands, and nothing needs correcting |
| **PROD** | Not built | As TEST |

If an environment does need correcting, the options are to edit the affected cases, or to
delete and re-import the batch. There is no backfill verb, deliberately: writing one to fix
records that nobody has yet relied on would be tooling built ahead of a need.

---

## The residual risk, stated plainly

**No real IO extract was available to check this against.** The only extract in the repository
is `data/io-task-extract-sample.csv`, which is synthetic — its names are literally
"Paraplanner 1" and "Checker 4".

That sample supports this mapping: `AssignedBy` is "Paraplanner N" in all seven rows. So does
the written specification, which has said `AssignedBy` throughout. Against that, the
2026-09-14 direction said the real extract put the two the other way round.

The project owner was shown this conflict and reaffirmed `AssignedBy`. **If a real extract
later shows otherwise, the fix is one line in each of the two files above, plus the tests.**

The test fixtures were renamed with the mapping, deliberately. `AssignedTo` used to be called
"Pat Paraplanner" in both fixtures — which is precisely the assumption that got written into
the column map twice. A fixture that embeds the guess cannot catch the guess.

---

## The tightening in the matcher

`MatchParaplanner` replaces a single `null` with five outcomes: `NoName`, `NoContact`,
`Ambiguous`, `NoEmail`, `Matched`.

One case now behaves differently. The old query filtered on `emailaddress1` being present, so
**two contacts of one name where only one had an email** resolved to that one and sent. It now
reads `Ambiguous` and sends nothing. A missing email address is not evidence about which Sam
Jones the case means, and the whole reason this matching fails loudly is that sending a
client's advice outcome to the wrong para-planner is a data-protection incident where an
unrouted row is an operational one.

A theory test pins that none of the four non-matches returns an address, so the richer
reporting did not quietly become a looser send.

---

## Verification

- **1139** plug-in tests, **608** app tests, `tsc -b` clean.
- The two mapping tests were run against the old mapping and **both fail**; they pass against
  the new one.
- No schema change. No environment change. Nothing to deploy beyond the assembly and the app.
