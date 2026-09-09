# The six al_response steps re-enabled — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as `svc.automate.aq@ascotlloyd.co.uk`.

## What was wrong

Six plug-in steps on `OutcomeTesting.Plugins` were **Disabled**, and had been since
08:52 UTC on 2026-09-02:

| Step | Plug-in | Enforces |
|---|---|---|
| ResponseGuard: Create of al_response | `ResponseGuardPlugin` | PP-11 submission lock, AD-023 answer shape and option subset, AD-020 section ownership, `al_responsecode` stamping |
| ResponseGuard: Update of al_response | `ResponseGuardPlugin` | the same, on edit |
| ResponseGuard: Associate fail reason | `ResponseGuardPlugin` | FR-013 fail reasons only on an open review |
| ResponseGuard: Disassociate fail reason | `ResponseGuardPlugin` | the same, on removal |
| ResponseProgress: Create of al_response | `ResponseProgressPlugin` | FR-010 Assigned → Review In Progress on the first saved answer |
| ResponseProgress: Update of al_response | `ResponseProgressPlugin` | the same |

While they were off none of that ran in DEV. The code was present and its tests passed;
only the step state was wrong. AD-053, AD-073, the 2026-09-08 write-path plan and the
2026-09-08 portal repair record all assume these steps run.

## Why they were off — an import, not a decision

Dataverse's own import history settles it. Import job `9bdfe47f` (an unmanaged import of
`OutcomeTesting`, 08:49–08:53 UTC on 2026-09-02, 234 s) processed **exactly these six
`SdkMessageProcessingStep` components at 08:52:04**, the second the rows were modified. It
was the first package to carry `SdkMessageProcessingSteps/`, copied back from the 08:30
export that morning. No other import on 2 or 3 September carried any step, which is why
the steps registered later that day stayed enabled.

Dataverse imports a step **Disabled** unless activation is requested; `pac solution import`
requests it only with `--activate-plugins`. Step XML carries no state element, so `src/`
could never have shown this, and the 2026-09-02 and 2026-09-06 notes counted steps (10, then
14) without reading their state. There is no audit trail on the rows and no decision-log
entry, consistent with an accident.

## Why re-enabling changed nothing it should not

Checked before the change, against DEV rather than inferred:

- **One write path.** The review page PATCHes `al_reviewinstance.al_answerrequest`;
  `AnswerRequestPlugin` → `AnswerWriter` creates or updates `al_response`, sending all four
  answer columns (the Update step's filter) and relying on the registered pre-image. The Code
  App and `GenerateExportPlugin` only read answers; `SubmitReviewPlugin` writes the review,
  never the answers.
- **The portal identity has the privileges.** `# PowerPages Data Runtime PROD` holds
  `Service Writer`: organisation-level read on `al_reviewinstance`, `al_questionversion`,
  `al_question`, `al_section`, `al_failreason`; create and write on `al_response`; write on
  `al_reviewinstance`. Both plug-ins run as that user and need nothing more.
- **DEV data passes the rules.** Every review and every section is on Checker Checklist V8;
  no question version lacks a response type or a question; no question lacks a section; the
  only answer on a still-open review (Q-AML-01, answer No on a Yes/No/N/A scale) is a permitted
  value.
- **The status move is harmless.** Nothing in the portal or the app filters on Assigned; the
  submit plug-in accepts Assigned and Review In Progress alike; the nested review update
  touches only `al_reviewstatus` and `al_startedon`, so neither trigger-column step fires.
- **Plug-in tests:** 508 passed.

Known and accepted: 49 answers created through the portal on 2026-09-08 carry no
`al_responsecode`, because the guard was not there to stamp it. They work (`AnswerWriter`
matches on the two lookups) but lack the replay key until backfilled. The one-off contacts
migration seeds answers with its own code format, which the guard would now overwrite on
Create; it has run once and is not expected to run again.

## What ran

A new verb, because the registration tool had no way to write step state and `pac` has none:

```
dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll \
  setstepstate https://org0b075da8.crm11.dynamics.com enable \
  "ResponseGuard: Create of al_response" "ResponseGuard: Update of al_response" \
  "ResponseGuard: Associate fail reason" "ResponseGuard: Disassociate fail reason" \
  "ResponseProgress: Create of al_response" "ResponseProgress: Update of al_response"
```

Each step was updated to `statecode` 0 / `statuscode` 1 and its state read back before being
reported; all six reported `enabled`. An independent `pac org fetch` afterwards shows all six
Enabled.

## Rules that follow

1. **Any `pac solution import` whose package carries `SdkMessageProcessingSteps/` passes
   `--activate-plugins`**, and is followed by a state query on every step of the assembly.
2. **A step audit reads `statecode`.** Counting rows is not an audit.
3. `setstepstate` is the sanctioned way to change a step's state in any environment.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| Investigate cause and impact | Delivery (automated) | 2026-09-09 | Import job 9bdfe47f identified; no blocker to re-enabling |
| `setstepstate enable` on six steps | Delivery (automated) | 2026-09-09 | Pass — six enabled, state read back |
| End-to-end walk-through with the guard on | — | — | Outstanding: the final proof |
