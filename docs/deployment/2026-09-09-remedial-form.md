# Deployment — AD-095 remedial form: columns, guard, templates and app live in DEV

Date: 2026-09-09
Target: `Env_AQ_Dev` (`org0b075da8`, `https://org0b075da8.crm11.dynamics.com`)

## Report

The remedial form (AD-095) is deployed to DEV. Advisers fill three new choice questions —
client contact required, recheck required, and whether the remedial actions change the
advice — on the remediation action itself, alongside the existing free-text response. All
three columns exist in DEV, `RemediationResponseGuardPlugin` now locks them together with the
response once an action is Completed, the Web API allowlist lets the portal write them, the
worklist and case-detail web templates render the form, and the app's remediation tab shows
the same columns. Code and tests are complete and pushed; one styling file is not yet pushed
(see "Left for the project owner" below) and retesting as an adviser and a supervisor is
still to be done.

## What changed

| AD-095 | **The remediation form is the adviser's, filled on the remediation action.** Three new choice columns on `al_remediationaction` — `al_clientcontactrequired` (Yes 120910793 / No 120910794 / Potentially 120910795), `al_recheckrequired` (Yes 120910796 / No 120910797), `al_changesadvice` (Yes 120910798 / No 120910799) — carry the form's questions; the adviser's free-text response is the "Remedial action"; owner is the assigned contact, target date the due date, adviser sign-off the completion, supervisor sign-off the `al_signoff` decision, "All remedial actions checked and approved" the derived state of every action's latest decision, and "Regraded outcome" the `al_outcome` final grade. `RemediationResponseGuardPlugin` locks the three with the response once the action is Completed, and its step filters on all five. The worklist renders the form with the write panels; the case page renders it per case, read-only; the app's remediation tab shows the same columns. | Project owner's form and direction 2026-09-09 ("filled in by the adviser at the remediation action"). Columns live on the action rather than the case because that is where the adviser writes and where the Contact-scoped write permission already reaches (AD-069); a case-level block would need a new write path. Values from the free tail of the 1209107xx band (770-792 were used). "All checked and approved" is derived rather than stored so it cannot disagree with the sign-offs it summarises. | 2026-09-09 |

## Tests

- Plug-ins: 541 passed, 0 failed.
- App: 217 passed, lint 0 errors (9 pre-existing warnings), build clean.

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet $T addchoicecolumn $U al_remediationaction al_ClientContactRequired "Client contact required?" "120910793:Yes;120910794:No;120910795:Potentially" "..." --confirm $U` | `al_remediationaction.al_clientcontactrequired created with options 120910793=Yes, 120910794=No, 120910795=Potentially.` |
| 1 | `dotnet $T addchoicecolumn $U al_remediationaction al_RecheckRequired "Recheck required?" "120910796:Yes;120910797:No" "..." --confirm $U` | `al_remediationaction.al_recheckrequired created with options 120910796=Yes, 120910797=No.` |
| 1 | `dotnet $T addchoicecolumn $U al_remediationaction al_ChangesAdvice "Remedial actions change the advice?" "120910798:Yes;120910799:No" "..." --confirm $U` | `al_remediationaction.al_changesadvice created with options 120910798=Yes, 120910799=No.` |
| 2 | `dotnet build plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.csproj -c Release` | build clean |
| 2 | `dotnet $T pushassembly $U` | pushed OutcomeTesting.Plugins, 172544 bytes, modified 2026-09-09 13:02:05Z |
| 2 | `dotnet $T setstepfilter $U "RemediationResponseGuardPlugin: Update of al_remediationaction" al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice --confirm $U` | `'RemediationResponseGuardPlugin: Update of al_remediationaction': filteringattributes 'al_adviserresponse,al_evidencereference' -> 'al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice'.` |
| 3 | `dotnet $T setsitesetting $U Webapi/al_remediationaction/fields al_adviserresponse,al_evidencereference,al_completerequested,al_clientcontactrequired,al_recheckrequired,al_changesadvice --confirm $U` | `al_adviserresponse,al_evidencereference,al_completerequested` -> `al_adviserresponse,al_evidencereference,al_completerequested,al_clientcontactrequired,al_recheckrequired,al_changesadvice` |
| 3 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000019 "powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html"` | pushed web template 'OT Remediation' (a1000000-0000-4000-8000-000000000019): 25279 -> 29639 chars, modified 2026-09-09 13:02:29Z |
| 3 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000015 "powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html"` | pushed web template 'OT Case Detail' (a1000000-0000-4000-8000-000000000015): 10636 -> 15844 chars, modified 2026-09-09 13:02:36Z |
| 4 | `npx pa app push` (from `app/`) | App pushed successfully, 2026-09-09 ~13:02:50Z |
| 5 | Final review fix wave (commits 211acb9, 3216c4d): `pushwebtemplate` both templates again | OT Remediation 29639 -> 30199 chars, modified 2026-09-09 13:21:17Z; OT Case Detail 15844 -> 15923 chars, modified 2026-09-09 13:21:26Z |
| 5 | `dotnet $T pushwebfile $U a1000000-0000-4000-8000-000000000050 "powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css"` (new verb, commit 2f5ce12: block upload into the component's `filecontent` File column) | pushed web file 'outcome-testing.css': 33229 bytes, modified 2026-09-09 13:21:34Z |
| 5 | `npx pa app add data-source --connector dataverse --table al_remediationaction --non-interactive` then `npx pa app push` | data source regenerated (model and schema now carry the three answers, commit 6462efa); app pushed 2026-09-09 ~13:21Z |

## Left for the project owner (css now deployed)

The css web file was pushed at 13:21:34Z through a new `pushwebfile` verb, so nothing here is
undeployed. What remains is yours:

- **Retest** as an adviser and a supervisor (section below).
- **Sign-off visibility (parked, your decision):** `al_signoff` is readable on the portal by the
  T&C Supervisor role only, so advisers, reviewers, managers and Administrators see a dash for
  "All remedial actions checked and approved?", "Supervisor sign-off" and the per-row sign-off
  on the case page and worklist, even when a sign-off exists. Granting portal read on
  `al_signoff` to the roles that can open a case (AD-056/AD-069 territory) would fill them in;
  it is a security decision and has not been made here.

## Retest

As an adviser: open My Work, expand an action assigned to you, fill the form (client contact
required, recheck required, changes advice, and the free-text response), save, and sign off.

As a supervisor: sign off the action's `al_signoff` decision; open the case page and read the
form (read-only) to confirm all five fields show correctly.

## Sign-off

| Item | Owner | Status |
|---|---|---|
| Code and tests | Delivery (automated) | Pass |
| DEV columns (`al_clientcontactrequired`, `al_recheckrequired`, `al_changesadvice`) | Delivery (automated) | Pass — created 2026-09-09 |
| DEV assembly (`OutcomeTesting.Plugins`) | Delivery (automated) | Pass — 172544 bytes, modified 2026-09-09 13:02:05Z |
| DEV guard step (`RemediationResponseGuardPlugin: Update of al_remediationaction`) | Delivery (automated) | Pass — filteringattributes widened 2026-09-09 |
| DEV site setting (`Webapi/al_remediationaction/fields`) | Delivery (automated) | Pass — allowlist widened 2026-09-09 |
| DEV web templates (OT Remediation, OT Case Detail) | Delivery (automated) | Pass — re-pushed after the review fix wave, 13:21:17Z and 13:21:26Z |
| App push | Delivery (automated) | Pass — re-pushed with the regenerated data source, ~13:21Z |
| css web file (`outcome-testing.css`) | Delivery (automated) | Pass — `pushwebfile`, 13:21:34Z |
| Retest | Project owner | Pending |
