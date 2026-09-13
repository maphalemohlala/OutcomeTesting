# Tax check outcome wording — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12: every environment write targets DEV until the owner names
another environment for that specific promotion.

Decision: AD-055, amended 2026-09-13.

---

## What changed, and what deliberately did not

Q-TAX-02 "Tax check outcome" reads its middle option as **PASS WITH ISSUES** where it read
INSUFFICIENT EVIDENCE. Project owner direction.

This is wording and nothing else:

| | |
|---|---|
| Response type | `PassFailInsufficient` (120910006) — **unchanged** |
| Stored value | `120910302` — **unchanged** |
| `ResponseRules.PermittedChoices` | **untouched** |
| `OutcomeRules.TaxResultRequiresRemediation` | **untouched** — that value still enters remediation |
| `data/v8-seed` | **untouched** |
| Existing rows | **no migration** — nothing stored changes meaning |

The rename could not sit on the scale. `PassFailInsufficient` is shared with the suitability
grid — S-E1 to S-E5 and S-CRP, nine seeded questions — which still reads 120910302 as
Insufficient evidence. Renaming the option itself would have relabelled six AQS sections
nobody asked to change. It is applied instead only where a label is read in a Tax context.

**The intake page's download template needed no change: it no longer exists.** The project
owner asked for it to be updated too. It was removed the day before this change, in
`9f4e968 feat(import): rebuild the case import around the IO task extract` (2026-09-12,
design D1) — the 18-column CSV was derived from `checklist-v8.md` and existed nowhere in
Intelligent Office, so the intake page now accepts the Pre-Advice Check task report and
offers no blank template to fill in. Its columns carried no outcome of any kind, only
`Tax check required` (Yes / No) and `Tax team disposition`, so it had no Insufficient
evidence option to reword even before it went. The only download left on the page is the
validation report.

**Out of scope and deliberately untouched:** `al_iooutcome`, the Intelligent Office task
outcome the case import carries in (`ImportRules.cs`, `caseUpload.ts`). It is a separate
four-value set, it is not the Tax check's grade, its option text is an inbound parse
contract with IO, and it already carries its own `Pass with issues` at 120910611 — so the
same rename there would leave two values reading identically and make the text-to-value
lookup ambiguous. `setoptionlabel` refuses it for that reason.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `setoptionlabel … al_outcomecase al_taxoutcome 120910302 "Pass with issues"` | `'Insufficient evidence' -> 'Pass with issues'`, published, **3 values**, exit 0 |
| 2 | `pac solution export --managed false` | **1,139,139 bytes**, succeeded |
| 3 | `pac solution unpack` → diff against `src/` | DEV reads `Pass with issues`. **Copy-back refused** — see below |
| 4 | `dotnet build -c Release` | clean, 0 warnings, 0 errors, **214,528 bytes** |
| 5 | `pushassembly` | **NOT RUN — held back.** See below |
| 6 | `pushwebtemplate … OT Answer Options` | `a1000000-…-021`, **3638 → 4123 chars** |
| 7 | `pushwebtemplate … OT Case List` | `a1000000-…-014`, **28896 → 28891 chars** |
| 8 | Code App build and push | **NOT RUN — held back.** See below |
| 9 | `pac solution pack` over `src/` minus `PluginAssemblies/` | **612,992 bytes**, packed — the hand-edited `Entity.xml` is still valid |

The checker-facing half of this change is live: the option label is renamed in DEV and both
portal templates render PASS WITH ISSUES. The plug-in half is not, and cannot be until the
import schema lands.

`setoptionlabel` is new in this change — the registration tool could add an option value
(`addoptionvalue`) but had no way to reword one, and AD-013 makes DEV the source, so a label
hand-edited into `src/Entities/al_OutcomeCase/Entity.xml` would have been overwritten by the
next export. It refuses an absent value and refuses a label already worn by another value on
the same set.

---

## What was deliberately not deployed, and why

**`pushassembly` and the Code App, both held — the same hold
`2026-09-12-paper-case-detail-and-section-shape.md` placed on the import half of `9f4e968`,
which is still in force.** The assembly built here carries that commit's rebuilt
`ImportRules`, which writes sixteen columns DEV does not have. Probed directly today rather
than assumed:

```
fetch <fetch top="1"><entity name="al_outcomecase">
        <attribute name="al_iooutcome" /></entity></fetch>
  -> 0x80041103 'al_OutcomeCase' entity doesn't contain attribute
     with Name = 'al_iooutcome'
```

A create naming an attribute the table does not have fails the row outright, so pushing this
assembly would fail **every** imported case — and this change's one plug-in line is not worth
that. The Code App is held with it for the reason that note gives: it posts CSV under IO's own
headers, which the deployed `ImportRules` refuses as having no "IO reference" column.

Order when it does go is unchanged: **schema, then assembly, then Code App.**

**The AD-013 copy-back was refused.** Diffing the export against `src/` (line endings
normalised — the export is CRLF, `src/` is LF) shows `src/` carries **58** attributes on
`al_OutcomeCase` and the exported solution **42**. The sixteen missing are exactly the IO
extract's: `al_AdviserEmail`, `al_ChecklistCompletedBy`, `al_ChecklistCompletedDate`,
`al_ChecklistItems`, `al_ClientRef`, `al_IoCompletedBy`, `al_IoCompletedDate`,
`al_IoCreatedBy`, `al_IoCreatedDate`, `al_IoOutcome`, `al_IoTaskStatus`, `al_ServiceCaseRef`,
`al_ServiceStatus`, `al_TaskStartDate`, `al_TaskType`, `al_WorkflowName`. They are authored
in `src/` and exist neither in DEV nor in the solution, so copying the export over `src/`
would have silently deleted 696 lines of column definitions that the import rebuild depends
on. The label this deployment changed was verified present and correct in the export instead,
and `src/` was left alone.

The only other divergence is deliberate: the `al_TaxOutcome` column and option-set
`Description` prose added by this commit is in `src/` and not in DEV, because `setoptionlabel`
sets the label and not the description. It is documentation rather than behaviour; it will go
to DEV with the next `setattributedescription` or be carried by a future round trip.

---

## Evidence before the deploy

| Suite | Result |
|---|---|
| `dotnet test` (plugins) | **754 passed, 0 failed** |
| `npx vitest run` (app) | **456 passed, 0 failed** |
| `npx tsc -b` (app) | clean, exit 0 |
| `xml.dom.minidom` parse of `al_OutcomeCase/Entity.xml` | well-formed |

`checklistDocument.test.ts` checks the rendered form against the project owner's reference
`docs/reference/checker-checklist.html`. That document still reads INSUFFICIENT EVIDENCE, so
the divergence is now recorded there as the **third deliberate difference**, asserted as a
substitution on the document's own option list rather than as a literal — the order, the
casing and the other two options are still read from the reference, and any further drift
still reddens the test.

---

## Still open

- **The reference document.** `docs/reference/checker-checklist.html` is left exactly as the
  project owner supplied it. If the checklist is reissued with the new wording, the deliberate
  difference in `checklistDocument.test.ts` should be removed in the same change.
- **`ResponseRules.IsNonPass` does not admit `ChoicePassWithIssues`.** Latent and harmless
  today — Pass with issues exists only on the Grade scale, which is read through
  `OutcomeRules.TryGradeFromAnswer` instead — and untouched by this change, which adds no new
  value anywhere. Worth closing before any future change puts 120910303 on a pass/fail scale.
