# Delivery status — 2026-09-19

Supersedes `docs/2026-09-16-delivery-status.md` as the current status. The register of what is
left remains `docs/2026-09-04-outstanding-work.md`, read with the "Still open" sections of the
deployment notes since.

Every environment claim below was verified by query after the fact. **`Env_AQ_Dev` only** —
nothing was promoted to TEST today.

**The day in one line: a bug reported as "wrong options after changing a question type" was
neither a cached value nor a wrong option set — the saved values were always correct, and a
tax-only rewording was being drawn over an AQS question because the inline rendering path,
unlike the grid one, never asked which discipline it was on. Fixing it exposed a second
defect in the same area: a seeded block goes on heading itself with a scale its questions
have left. Both fixed on both surfaces, then the Advice date became "Date of meeting - Client
contact" and gained a rule that it can never be in the future.**

Three code changes and one corrective, all deployed to DEV. One deployment note:
`docs/deployment/2026-09-19-checklist-scale-and-date-of-meeting.md`.

---

## 1. What changed

| Where | What |
|---|---|
| `app` + `powerpages` | The tax check's reorder and rename of `120910302` apply on a Tax review and nowhere else. Commit `a9a757a` |
| `app` + `powerpages` | A declared grid whose questions have left its scale heads itself from the questions. Commit `6a8111f` |
| `app` + `powerpages` | …reading the tick-scale questions alone, so S-E2's lens tick cannot take Suitability off its grid. Commit `309ce88` |
| `powerpages` | `al_accountabilityrequest` restored to the Web API allowlist. Commit `58330b8` |
| `plugins` + `app` + `powerpages` + Dataverse | `al_advicedate` reads "Date of meeting - Client contact" and cannot be in the future. Commit `18b390e` |
| `app` | The date is validated only when it was changed, as the command does. Commit `8f679da` |
| `Env_AQ_Dev` | Assembly 241,152 bytes, built `-c Release` immediately before the push |
| `Env_AQ_Dev` | Code App bundle `index-CWTHwghs.js`; portal uploaded; `src/` round-tripped |

Decisions recorded: **AD-145**, **AD-146**, **AD-147**.

---

## 2. What the fix actually was

The report named a symptom in the question editor, which is not where the fault was.

`ResponseRules.PermittedChoices` — the authority on which values may be saved — was correct
throughout, and so was every value ever written. What was wrong was that **each surface has
two option-rendering paths, and only the grid ones tested the review's discipline**. AD-055
amended reworded `120910302` to "Pass with issues" for the Tax check alone and put the rename
on the inline path, on the reading that only Q-TAX-02 was ever drawn there. AD-123 checklist
administration had made that false a while ago; retyping a question is simply what made it
visible, because it breaks a section's uniform grid and drops it onto that path.

A checker on an AQS review who ticked what read "PASS WITH ISSUES" saved Insufficient
evidence — a non-pass that raises remediation. **Those answers are to be reported, not
rewritten**, and that audit is still owed.

---

## 3. Verification

| Check | Result |
|---|---|
| App tests | 548 pass (11 new today) |
| App typecheck | `tsc -b` clean — and it, not a test, caught a third call site |
| Plug-in tests | 981 pass (9 new today) |
| Portal in DEV | 7 × "Date of meeting", 0 × "Advice date", queried from `powerpagecomponent` |
| Dataverse label | read back after publish |
| Assembly | byte count matched between build and arrival |

The C# and TypeScript date suites cover the same cases deliberately, including the British
Summer Time boundary, so a change made to one and not the other shows as two suites
disagreeing rather than as a rule holding on one surface only.

---

## 4. The batch: 8 of 10 done

| # | Item | State |
|---|---|---|
| 1 | Wrong options after a type change | **Done** — `a9a757a`, `6a8111f`, `309ce88` |
| 9 | Rename Advice date, block future dates | **Done** — `18b390e`, `8f679da` |
| 8 | Advice date ≤ Due date | **Done** — `a03eb69` |
| 3 | Conditional Primary root cause | **Done** — `f1f9755`, corrected in `7c1633a`. AD-149; `GradingRules`, the submit gate, and post-operation clearing on `al_response` |
| 10 | Insufficient Evidence on Suitability core checks | **Done** — `21f545f`. AD-150; keyed on `al_sectioncode` (`S-E` + digit), guard + clearing + submit gate, portal restricts live. **Audit: no review in DEV violates it** |
| 2 | Split Checker into Tax and AQS | **Done** — `9536b9e`. AD-151; two stamped columns on the case, not editable, three empty states. Schema change after all — both columns are in the solution. Backfilled 12 cases |
| 4 | Rename Owner to Uploaded by | **Done** — `d9141cb`. AD-152; four app surfaces plus the Dataverse display name. Four other "Owner" columns left alone — they are the checker and the adviser |
| 6 | Due date: 3 days, manager-editable | **Done** - `ccf71be`. AD-153, AD-154; calendar time confirmed, new `case.duedate` key seeded to the two manager roles. **Three defects found on the way**: the panel's Due date box failed the whole save, the legacy `DueDate` scalar let anyone move it, and item 8's refusal said "Sept" in the app and "Sep" on the server |
| 7 | Paraplanner email and recipient | Next. `AssignedTo` stays mapped; the remap is off |
| 5 | T&C Manager mapping | Not started. Per-adviser on Contact, defaulted onto the case by adviser email |

---

## 5. Still open — and one thing that is worse than it was

**A regression this deployment caused, fixed the same hour.** The first portal upload pushed a
**stale local `sitesetting.yml` over newer DEV state** and dropped
`al_accountabilityrequest` from the Web API allowlist — the column `OT Review Detail` writes
from the accountability card. Restored and verified.

The cause matters more than the incident: **the accountability work was configured directly in
DEV and never written back to `powerpages/`**. Anything else configured that way is still only
in DEV, and the next upload will overwrite it the same way. It was found by diffing the
round-trip; the upload itself reported success.

### Fixed after the first write-up

**`outcome-testing.css` — this status first said it had not been deploying. That was wrong.**
The manifest carried two entries for it: the web file record under `adx_webfile`, which
uploads cleanly, and an orphan under `annotation` pointing at a note that no longer exists.
Web file bytes lived in annotations under the Standard Data Model; under the Enhanced Data
Model they live in `fileattachment`, where DEV holds **53,724 bytes — byte-for-byte the local
file**. No CSS was ever lost. The orphan is removed and an upload now processes all 300
records with no FAILED line.

**`OT Tax Notes` is back.** Its seeded id `…000022` collided with a web page's under the
Enhanced Data Model, where both live in one table, which is why `--forceUploadAll` would not
create it. Given a free id in the template block — `…00001f`, verified unused in repo and
environment — it exists, is Active, and the two includes that name it resolve again.

**All five AML/CRA questions now carry a current version on `120910006`**, so that section is
uniform and heads itself Pass / Fail / Insufficient evidence through the AD-146 derive path.

**Two components were in DEV but not in the solution**, so neither would have promoted to TEST
or PROD. The recreated `OT Tax Notes`, and — found by the same run — the **`Contact -
directory read (global)`** table permission, which the portal's people pickers depend on.
`addsitetosolution` carried both. This is the same drift as the allowlist: configured in the
environment, never captured where a managed deployment would find it.

**The test project did not compile at HEAD.** `96d37e3` renamed
`Remediation.AssignUnassignedActions` to `AssignOpenActions` and left five call sites behind;
it built only for whoever carried the fix in their working tree, which is how the branch came
to be pushed that way. Fixed, along with the one lint warning the project raised, and the
`repointremediation` verb written in a previous session is committed rather than left loose.

### Still open

**The item 1 data audit has not been run.**

**Anything configured directly in DEV and never written back to `powerpages/` is still only in
DEV**, and the next upload will overwrite it the way this one overwrote the allowlist.
