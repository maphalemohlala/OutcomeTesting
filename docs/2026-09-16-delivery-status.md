# Delivery status — 2026-09-16

Supersedes `docs/2026-09-15-delivery-status.md` as the current status. The register of what is
left remains `docs/2026-09-04-outstanding-work.md`, read with the "Still open" and "Not done
here" sections of the deployment notes since.

Every environment claim below was verified by query after the fact. **`Env_AQ_Dev` took all
three deployments; `Env_AQ_Test` was read but not written.**

**The day in one line: the export was wrong twice over — it refused cases that had passed,
because the initial and final outcome columns are different option sets with nothing
translating between them, and it then reported blank File Quality for every Tax-only case
because it only ever read the AQS question. Both fixed and deployed to DEV, and fail
accountability now derives from the adviser and paraplanner the case already names — which
answers the Tax question that had no answer, and retires OD-024's gate with it. What is left is
mostly not code: a Trail Light contract with no Tax column, and no way for anyone to override a
derived pair.**

Four code changes across three deploys, all to DEV. Three deployment notes:
`docs/deployment/2026-09-16-regrade-pass-export-gate.md`,
`docs/deployment/2026-09-16-tax-file-quality-and-html-letters.md` and
`docs/deployment/2026-09-16-derived-fail-accountability.md`.

Reading the export that the morning's fix let through produced the afternoon's two: the
owner spotted that the File Quality column was blank, and was right that Tax cases carry a
file quality outcome of their own.

---

## 1. What changed

| Where | What |
|---|---|
| `plugins/OutcomeTesting.Plugins` | `OutcomeRules.ToGradeScale`; `Outcomes.EffectiveOutcome` returns the grade on one scale. Commit `a1fe9e7` |
| `plugins/OutcomeTesting.Plugins` | Export column 10 falls back to `Q-FQTAX-01`, the Tax file quality outcome. Commit `5093f69` |
| `plugins/OutcomeTesting.Plugins` | The three adviser letters are HTML, with the case link as an escaped, styled anchor. Commit `80ba8b5` |
| `plugins/OutcomeTesting.Plugins` | Fail accountability derives from the case's own adviser and paraplanner; the OD-024 gate retires. Commit `b38e0e1` |
| `Env_AQ_Dev` | Three assembly pushes, 224768 then 225280 then 226304 bytes, each built `-c Release` immediately before |
| `docs/reference/portal-access-runbook.md` | Step 1 corrected: it does not work for a portal-only person |
| Tests | 855 passed, 0 failed (827 at the start of the day) |

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

## 4. Adam Strumidlo bound in TEST — and nobody has ever signed in

The project owner supplied the Entra object id, which is the one thing no query in this
environment could recover: he has no `systemuser` row, so Dataverse did not hold it.

`bindidentity`, `setsecuritystamp`, `enableportallogin` against
`adam.strumidlo@ascotlloyd.co.uk`, contact `c63a64aa-cab1-f111-aaac-002248c654cd`. Identity row
`daddb587-d5b1-f111-aaac-6045bd0aeb46`, issuer copied from a working binding
(`https://sts.windows.net/4abde4fc-.../`). The `identities` report now lists him among the
bound, with Tax Reviewer, AQS Reviewer, Adviser Remediation, T&C Supervisor and
Administrators, and **its "contacts with no binding" section is now empty**.

Compared against two contacts considered correctly provisioned, he is identical:

| | username | stamp | logon enabled | email confirmed |
|---|---|---|---|---|
| Zoe Ramwell | set | set | Yes | No |
| Clare Hook | set | set | Yes | No |
| Adam Strumidlo | set | set | Yes | No |

**That is not enough to promise him a first-try sign-in, and the evidence says it will not be.**
`Authentication/LoginTrackingEnabled` has been `true` in TEST since 2026-09-15 10:14, and
**no contact in the environment has a recorded successful sign-in** — the query for a non-null
`adx_identity_lastsuccessfullogin` returns nothing at all. Zoe Ramwell is the person who was
found on 2026-09-15 to pass all four requirements and still reach
`/private-mode-access-denied`; Adam is now provisioned exactly as she is.

Gate 0 — site visibility — remains the thing keeping everyone out, and it lives in the
Power Pages management service where no `pac` command, no verb and no FetchXML can reach it.

Note what this column can and cannot show: it records **successes**. A failed Entra sign-in
writes nothing, and a gate 0 refusal happens before Dataverse is consulted at all, so there is
no way from here to tell whether anyone has *attempted* access — only that nobody has
succeeded.

## 4b. Gate 0 was the blocker, and the verification never worked

The project owner granted site access under **Set up -> Site visibility -> Manage access**,
and **Zoe Ramwell then signed in successfully** -- the first confirmed sign-in this project
has had. Gate 0 was what had been keeping everyone out, exactly as the 2026-09-15
investigation concluded, and the grant is the fix. Adam is provisioned identically and was
granted at the same time.

The second finding is the durable one. That sign-in **left no trace in Dataverse**. Zoe's
`adx_identity_lastsuccessfullogin` is still null and her contact `modifiedon` is still
2026-09-14 08:51, older than the `LoginTrackingEnabled` change itself. Checked across both
environments: every contact in TEST and every contact in DEV reads null, with the setting
`true` in both since 2026-09-15, the service account included.

So the runbook's "Verifying without a screenshot" section was wrong, and wrong in the
dangerous direction -- it told the reader a null reading after 2026-09-15 was evidence of a
failed sign-in. It has been rewritten to say the check does not work here and that the only
reliable verification is a person signing in and saying so. Why it does not work is not
established; the untested candidates are recorded as untested.

## 5. Still open

**Needs a decision, not code:**

- **AD-039 has no Tax outcome column.** `Q-TAX-02` is the tax verdict and there is nowhere in
  the export contract to put it. Column 15 is the *Advice Quality* grade on the four-value
  BR-005 scale, and the tax result is the three-value PassFailInsufficient scale (AD-055), so
  it was deliberately not written there. Adding a column is an agreement with Trail Light.

**Settled today:** Tax fail accountability, and whether a File Quality fail is attributable in
its own right. Both are answered by deriving the pair from the case — paraplanner for the file,
adviser for the advice — which needs no `al_outcome` row and so works on a Tax case. The OD-024
gate retired with it. See `docs/deployment/2026-09-16-derived-fail-accountability.md`.

**Needs building:**

- **`al_SetFailAccountability` has no caller in the app.** No longer a prerequisite for the
  export, which now attributes without it, but still the only way to override a derived pair.
  Building it needs a decision on where in the case UI it belongs and who may use it.
- **An override on a Tax-only case has nowhere to be stored.** The flags live on an
  `al_outcome` row and a Tax review creates none, so the command cannot address one. Deriving
  works there; recording an exception does not. Unchanged schema question.

*Closed since: the override now reaches a file-quality-only fail, and the Tax file quality
answer is shared between the export and the command rather than resolved two ways.*

**Operational:**

- The batch generated at 12:47 predates all three deploys and still holds the blanks. A re-run
  is a new batch by design (AD-042).
- `Env_AQ_Test` has none of today's four changes.
- Is IO-300001 meant to be on a Tax-only route while carrying an AQS review and a `Q-FQ-01`
  answer? No other Tax-only case does.
- `RegradeCasePlugin.ParseOutcome` hardcodes `120910710`-`713` rather than referencing the
  `OutcomeRules.FinalOutcome*` constants. Values agree; nothing is broken.
