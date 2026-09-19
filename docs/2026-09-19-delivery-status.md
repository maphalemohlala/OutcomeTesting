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

## 4. The batch: 2 of 10 done

| # | Item | State |
|---|---|---|
| 1 | Wrong options after a type change | **Done** — `a9a757a`, `6a8111f`, `309ce88` |
| 9 | Rename Advice date, block future dates | **Done** — `18b390e`, `8f679da` |
| 8 | Advice date ≤ Due date | Next. Extends `CaseHeaderRules`; the project owner confirmed one rule, no Submission date field |
| 3 | Conditional Primary root cause | Not started. Both are checklist questions in S-GRADE, not form fields |
| 10 | Insufficient Evidence on Suitability core checks | Not started. The stable key is the **set** S-E1…S-E5, not one section |
| 2 | Split Checker into Tax and AQS | Not started. To derive from `al_reviewinstance.al_assignedcontactid`; no schema change |
| 4 | Rename Owner to Uploaded by | Not started. Premise verified: no plug-in ever reassigns the case's `ownerid` |
| 6 | Due date editable by T&C Manager | Not started. The 72-hour derivation already exists; this is editability only |
| 7 | Paraplanner email and recipient | Not started. `AssignedTo` stays mapped; the remap is off |
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

**`outcome-testing.css` has not been deploying.** Every upload fails on one record whose
manifest id the environment no longer has, so any CSS change since that id went stale never
reached DEV. Pre-existing and untouched.

**`OT Tax Notes` is missing from DEV** while two live templates include it by name, so the Tax
notes panel renders as nothing. Its seeded id collides with a web page's under the Enhanced
Data Model, where both live in one table; `--forceUploadAll` will not create it. One line to
fix, but every id in the seeded range is taken, so the choice is the project owner's.

**Two AML/CRA questions are still on the old scale**, so that section currently renders as a
meta table with no headers — correct for a mixed section, not the end state asked for.

**The item 1 data audit has not been run.**
