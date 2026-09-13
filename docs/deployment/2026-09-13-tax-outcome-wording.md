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

**Out of scope and deliberately untouched:** `al_iooutcome`, the Intelligent Office task
outcome the case import carries in (`ImportRules.cs`, `caseUpload.ts`). It is a separate
four-value set, it is not the Tax check's grade, its option text is an inbound contract with
IO, and it already carries its own `Pass with issues` at 120910611 — so the same rename there
would leave two values reading identically and make the text-to-value parse ambiguous. See
"Still open" below.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `setoptionlabel … al_outcomecase al_taxoutcome 120910302 "Pass with issues"` | _pending_ |
| 2 | `pac solution export` → `unpack` → copy back `Entities/al_OutcomeCase/` | _pending_ |
| 3 | `pac solution pack --folder src` | _pending_ |
| 4 | `dotnet build -c Release` then `pushassembly` | _pending_ |
| 5 | `pushwebtemplate … OT Answer Options` | _pending_ |
| 6 | `pushwebtemplate … OT Case List` | _pending_ |
| 7 | Code App build and push | _pending_ |

`setoptionlabel` is new in this change — the registration tool could add an option value
(`addoptionvalue`) but had no way to reword one, and AD-013 makes DEV the source, so a label
hand-edited into `src/Entities/al_OutcomeCase/Entity.xml` would have been overwritten by the
next export. It refuses an absent value and refuses a label already worn by another value on
the same set.

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
- **The case import.** The project owner asked for the import template to be updated too. The
  only "Insufficient evidence" in the import path is `al_iooutcome` 120910612, which is the IO
  task's own outcome rather than the Tax check's, and which cannot take this label while
  120910611 holds it. Raised with the owner; not changed here.
- **`ResponseRules.IsNonPass` does not admit `ChoicePassWithIssues`.** Latent and harmless
  today — Pass with issues exists only on the Grade scale, which is read through
  `OutcomeRules.TryGradeFromAnswer` instead — and untouched by this change, which adds no new
  value anywhere. Worth closing before any future change puts 120910303 on a pass/fail scale.
