# Delivery status — 2026-09-16

Supersedes `docs/2026-09-15-delivery-status.md` as the current status. The register of what is
left remains `docs/2026-09-04-outstanding-work.md`, read with the "Still open" and "Not done
here" sections of the deployment notes since.

Every environment claim below was verified by query after the fact. **`Env_AQ_Dev` took the
one deployment; `Env_AQ_Test` was read but not written.**

**The day in one line: the export refused a case that had passed, because the initial and
final outcome columns are different option sets and nothing translated between them — fixed
and deployed to DEV, while the two other requests of the day both stopped on something that
cannot be done from here.**

One code change, deployed. One deployment note:
`docs/deployment/2026-09-16-regrade-pass-export-gate.md`.

---

## 1. What changed

| Where | What |
|---|---|
| `plugins/OutcomeTesting.Plugins` | `OutcomeRules.ToGradeScale`; `Outcomes.EffectiveOutcome` now returns the grade on one scale. Commit `a1fe9e7` |
| `Env_AQ_Dev` | Assembly pushed, 224768 bytes, built `-c Release` immediately before |
| `docs/reference/portal-access-runbook.md` | Step 1 corrected: it does not work for a portal-only person |
| Tests | 834 passed, 0 failed (827 before) |

## 2. The export bug

`al_initialoutcome` and `al_finaloutcome` carry the same four grades on **different option
sets** — `1209107_0_x` and `1209107_1_x`. `EffectiveOutcome` returned whichever column won the
BR-007 precedence as a raw value, and every caller compared it against the `OutcomeRules` grade
constants, which are only the first band. A final Pass (`120910710`) therefore differed from
`OutcomePass` (`120910700`) and read as a non-pass.

IO-300001 had been regraded from Insufficient evidence to **Pass** and was refused the AD-039
export for having no fail accountability — accountability for a fail it did not have. Because
`al_GenerateExport` throws inside the case loop, one such case blocks the entire batch.

`SetFailAccountability` made the mirror mistake and would have allowed a fail to be attributed
on a case that passed. Both are fixed by the one change.

The gate tests never set `al_finaloutcome` at all, and the one test that looked like it covered
a regrade put an initial-scale value in the final column — impossible data. That is why this
survived to a real case.

## 3. Not done: notifications from `tc.outcometesting@ascotlloyd.co.uk`

Requested today. **Not a code change and not attempted.**

There is no sender address in the codebase by design — AGENTS.md rule 7 forbids hardcoded
email addresses, and the sending mailbox is whatever account the `NotificationDrainPlugin`
step is registered to run as, set through the run-as argument of `registerstep`.

**No systemuser with that address exists in DEV.** The only match on `%outcometesting%` is the
Power Pages application user. Before the step can point at it the account has to exist and its
mailbox be approved and tested for server-side email (OD-030); pointing the step at a mailbox
that is not will land every notification at `Failed` rather than sending it. The drain step was
left exactly as it was.

## 4. Not done: Adam Strumidlo's TEST sign-in

His contact is in TEST (`c63a64aa-cab1-f111-aaac-002248c654cd`) and **his web roles are already
mapped** — Tax Reviewer, AQS Reviewer, Adviser Remediation, T&C Supervisor, Administrators. The
`identities` report lists him under *"Contacts with no binding — their roles are unreachable by
Entra sign-in"*. He has none of the four identity requirements: no `adx_externalidentity` row,
no identity username, no security stamp, and `adx_identity_logonenabled` is `No`.

Two things block the binding, and neither can be done from the tooling:

1. **His Entra object id is not in Dataverse.** `bindidentity` takes it as an argument, and the
   runbook's way of reading it — `systemuser.azureactivedirectoryobjectid` — returns nothing,
   because he has no `systemuser` row in TEST or in DEV. It has to come from the Entra admin
   centre. The runbook has been corrected; this case was not covered.
2. **Gate 0, site visibility.** Both sites are Private, and that check runs before any
   Power Pages authentication. Only Dataverse System Administrator holders bypass it, and he is
   not a Dataverse user at all — so unless he is inside whichever Entra group holds Manage
   access, binding him produces `/private-mode-access-denied` and nothing else. It lives in the
   Power Pages management service; no `pac` command, no verb and no FetchXML can read or write
   it.

So "access on first try" needs both, and the issuer is available to copy
(`https://sts.windows.net/4abde4fc-.../`) as soon as the object id is.

## 5. Still open

- IO-300001 has not been re-exported. The gate will pass it now; generating a batch writes a
  record for every closed case and marks the batch Generated, so it was left to be run
  deliberately.
- `Env_AQ_Test` does not have this fix. Any regraded-to-Pass case there will still block its
  export batch.
- `RegradeCasePlugin.ParseOutcome` hardcodes `120910710`-`713` rather than referencing the
  `OutcomeRules.FinalOutcome*` constants. Values agree; nothing is broken.
