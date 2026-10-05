# Pass with issues closes without T&C sign-off - DEV and PROD (AD-230)

Date: 2026-10-05. Branch `feat/otis-product-name`. Solution 1.0.20.0.

## What was reported

PROD case **256497798** showed under the Remediation page's **Awaiting T&C sign-off** filter.
Both of its checks were Pass with issues: Tax `al_taxoutcome` 120910302 and AQS
`al_initialoutcome` 120910701. All six actions were completed, and the case had no `al_signoff`
rows. BR-008 limits the T&C Manager's verification to Insufficient evidence and Potential
harm. The filter reads only `al_casestatus`, and the case really was at Awaiting Sign-off.

## Cause

`CompleteRemediationPlugin.AdvanceCase` moved every case to Awaiting Sign-off when its last
action was completed, whatever its grade. Nothing on the lifecycle side read the grade.
The Pass-with-issues letter already left out "liaise with your T&C Manager".

## What changed

| Part | Change |
|---|---|
| Rule | `OutcomeRules.SignoffRequired(taxOutcome, aqsOutcomes)` says sign-off is due for an AQS grade of Insufficient evidence or Potential harm, or a Tax Fail. Pass with issues on either check, or a flagged Pass, is not due. A case with no grade goes to sign-off, as before. |
| Completion | `CompleteRemediationPlugin.AdvanceCase`: a case that needs no sign-off moves Remediation In Progress -> Closed and gets no sign-off-due letter. A case whose AQS check is still owed always goes to sign-off. |
| Lifecycle | `CaseLifecycle` and `CASE_STATUS_TRANSITIONS` gain Remediation In Progress -> Closed. |
| Guard | `CaseStatusGuardPlugin` refuses that edge unless every action on the case is complete and nothing on it needs sign-off, so a direct write cannot close a Potential harm case past its sign-off. |
| Portal | `OT Remediation` computes `ot_signoff_due`. When it is false, the page draws neither the sign-off panel nor the hint, and does not say "awaiting supervisor". A case actually at Awaiting Sign-off is always due. |
| Docs | AD-230, the adviser guide generator, and a supersession note on APP-070. |

No email is sent when such a case closes. Its final outcome stays unset, so it reports on its
initial grade.

## Tests

- Plug-ins: 1,847 passed, including new theories in `CompleteRemediationCallerTests`,
  `CaseStatusGuardTests` and `CaseLifecycleTests`.
- App: 1,328 passed, including `domain.test.ts` and `portalSupervisorPanels.test.ts`. `tsc -b`
  is clean.

## DEV (`org0b075da8`)

- Assembly pushed (386,048 bytes, sha256 `35c3284f...`), and OT Remediation pushed
  (149,812 -> 151,976 characters).
- Proved with the portal's own completion path (`al_completerequested`):
  - **941004001** (AQS Pass with issues, one action) went to **Closed**.
  - **900000003** (AQS Insufficient evidence) went to **Awaiting Sign-off**.
- Code App built (`index-CBT6ReVg.js`) and pushed.
- Solution version set to 1.0.20.0, then exported managed.

## Package

`artifacts/2026-10-05-signoff-pwi/`:

- `OutcomeTesting_1_0_20_0_managed.zip`, sha256 `c956aba1...`.
- `OTIS_1_0_20_0_managed.zip`, sha256 `b3cd259e...`, branded with 7 labels.

Diffed against 1.0.19.0, the package changes only:

- the Code App bundle;
- the plug-in assembly, which matches the local Release build;
- the OT Remediation component;
- the version.

The uncommitted App Admin powerpagecomponent privileges are **not** in DEV, so they are not in
this package.

## PROD (`org3461d426`, OTIS)

The owner said "Promote to PROD".

- Before the import, OT Remediation had only the managed `OutcomeTesting` layer, so no direct
  push masked it.
- Import: `ImportSolutionAsync`, job `23683f17-2148-4091-85ca-fbeee71b9ad6`, async operation
  `8196780c-ccc0-f111-aaaf-70a8a57693dc`. It succeeded (statuscode 30) at 14:51:10Z.
- Read back from PROD:
  - solution 1.0.20.0, managed, OTIS;
  - assembly sha256 `35c3284f...`, the same as DEV and the local Release build;
  - all 67 steps of `OutcomeTesting.Plugins` enabled;
  - Code App OTIS, `appversion` 2026-10-05T14:50:48Z;
  - the OT Remediation stored source equals the repo source, `ot_signoff_due` included.
- No behaviour test was run in PROD, because completing an action there would change real
  case data.

## TEST (`org37995f36`)

The owner said "bring test to level".

- Before the import, OT Remediation had only the managed layer.
- Import: `OutcomeTesting_1_0_20_0_managed.zip` (unbranded), job
  `c84f5fa1-057e-4d80-b748-7d147b6b5223`. It succeeded (statuscode 30) at 15:03:46Z.
- Read back from TEST:
  - solution 1.0.20.0, managed;
  - assembly sha256 `35c3284f...`;
  - all 67 steps enabled;
  - Code App `appversion` 2026-10-05T15:03:29Z;
  - the OT Remediation source equals the repo source.

## Left as they are

On the owner's direction, the two PROD cases already at Awaiting Sign-off with only Pass with
issues grades, **256497798** and **254399947**, stay where they are. This release changes only
what happens at the next completion, so it does not move them.
