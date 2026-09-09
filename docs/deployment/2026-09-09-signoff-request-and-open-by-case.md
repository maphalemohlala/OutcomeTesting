# Sign-off had never worked, and "Opening the checklist" never opened — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Read and pushed through
`plugins/OutcomeTesting.Registration` (Release) as `svc.automate.aq`.

## The reports

1. Signing off a remediation as the service account: "Your account is not allowed to sign
   off remediation … Signing off needs the T&C Supervisor role" — with the role held.
2. After Run checks on the AQS queue: "Assigned to you. Opening the checklist…" and nothing
   opens.
3. "The AQS form still doesn't show the File Quality fail reason and Suitability test point."

## What the environment said

| Question | Answer |
|---|---|
| Service Account's web roles | Administrators, **AL Portal - T&C Supervisor** (read live through `powerpagecomponent_mspp_webrole_contact`). |
| `Signoff - T&C attestation` permission, live | Parent scope under `Remediation Action - read all` via `al_remediationaction_signoff`, create + read + append, bound to role 93. **Identical to source.** Parent bound to Administrators + Authenticated Users, identical to source. |
| `Webapi/al_signoff/enabled` / `fields` | true / `al_signoffdecision,al_notes,al_remediationactionid` |
| `al_signoff` rows in the environment | **Zero.** No sign-off has ever been recorded, by anyone. |
| Live `OT Review Detail` template at 16:24 | Contains "Suitability core checks", "Suitability test point", the fail points block, the subsection rows, the case header and the remediation block. |

## Root causes

**1. The sign-off create was refused by the platform, not by a role.** Every binding was
correct and the role was held, and still no row had ever been created. The page's POST on
`al_signoff` carried an `@odata.bind` to the remediation action. That is the third
browser-side association on this site: the answer create (AD-053) and the claim (AD-076)
were both refused by Power Pages with 90040106 whatever the table permissions said, through
repeated verified repairs, and both were moved onto a trigger column the server acts on. The
sign-off page mapped every 403 to "needs the T&C Supervisor role", which is why it read as a
role fault. Recorded as AD-099.

**2. The post-claim lookup could never succeed.** `openChecklist` read the new review
instance over the Web API with `$select` and `$filter` on `al_reviewinstanceid`,
`_al_outcomecaseid_value`, `_al_assignedcontactid_value`, `al_reviewtype`, `al_submittedon`
and `createdon`. The `Webapi/al_reviewinstance/fields` allowlist holds only the two trigger
columns, and the allowlist governs reads as well as writes, so the lookup failed on every
claim and the page fell back to reloading the queue.

**3. The form is right on the server.** The template with every document block was pushed
at 16:24 and is what Dataverse holds. Power Pages serves web templates from its server-side
cache, which a template push does not clear.

## What changed

| Where | Change |
|---|---|
| `contact.al_signoffrequest` (new memo column, in the OutcomeTesting solution) | The T&C attestation as JSON `{actionId, decision, notes}`, written by the supervisor onto their own contact row. |
| `SignoffRequestPlugin` (new; sync post-operation Update of `contact`, filter `al_signoffrequest`) | Parses the request, **checks the contact's web roles server-side for AL Portal - T&C Supervisor**, clears the column, creates the `al_signoff` with only decision, notes and action. `SignoffGuardPlugin` / `SignoffProgressPlugin` run unchanged. 7 tests. |
| `WebRoleRegistry.TcSupervisorRole` | The attesting role's name, beside the two reviewer roles. |
| `Webapi/contact/fields` | `al_claimrequest,al_signoffrequest`. |
| `Contact - claim request (self)` | Role 93 added to 90 and 91, so a supervisor can write their own contact. The plug-in's role check is what keeps a reviewer out of the sign-off column. |
| `OT Remediation` template | Sign-off PATCHes the contact; the error mapping now surfaces a 400 and any 9004010x code instead of naming a role. |
| `OT Review List` / `OT Review Detail` templates | After a claim the queue page navigates to `/review?case=<id>&type=<discipline>`; the review page resolves the open instance assigned to the signed-in user in Liquid, where no allowlist applies. |

## Verification

| Check | Result |
|---|---|
| `dotnet test` plug-ins | 554 passed, 0 failed |
| Liquid tag balance, three templates | all balanced |
| Live `Contact - claim request (self)` after restore | roles 90, 91, 93 |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `addmemocolumn $U contact al_SignoffRequest "Sign-off request" 4000 "…" --confirm $U` | created contact.al_signoffrequest (memo, 4000) in solution OutcomeTesting |
| 2 | `pushassembly $U` | 176640 bytes, modified 2026-09-09 16:51:40Z |
| 3 | `registertype $U OutcomeTesting.Plugins.SignoffRequestPlugin` | plugintype bc572ffa-6eac-f111-aaac-e4fade069307 |
| 3 | `registerstep $U OutcomeTesting.Plugins.SignoffRequestPlugin Update contact 40 al_signoffrequest sync` | step ed1f3106-6fac-f111-aaac-e4fade069307, added to the solution |
| 4 | `setsitesetting $U Webapi/contact/fields al_claimrequest,al_signoffrequest --confirm $U` | updated |
| 5 | `restoretablepermissions $U powerpages/outcome-testing---outcometesting` | 14 written; permission 077 now binds 90, 91, 93 |
| 6 | `pushwebtemplate` OT Review Detail / OT Review List / OT Remediation | 77071 / 24211 / 32932 chars, 16:52Z |

The site cache clear (`/_services/about` → Clear cache) is the project owner's step, and it
is what report 3 needs. `registerstep` was first refused with "Plug-in type … is not
registered. Run 'registertype' first", and `restoretablepermissions` takes the site folder,
not the permissions folder.

## Retest

1. Clear the site cache. Open an AQS review: the blocks of the document appear in order,
   including "File Quality – Fail points" and the "Suitability test point" table.
2. As a T&C Supervisor, sign off a completed remediation: "Sign-off recorded. Reloading…";
   the action shows the decision; `al_signoff` gains its first row.
3. As an AQS Reviewer, Run checks on a queued AQS case: the checklist opens directly.
4. As a reviewer without the supervisor role, the sign-off panel (if reached) answers
   "Signing off needs the AL Portal - T&C Supervisor role on your portal account."
