# Deployment — the sign-in reaches the right contact, and Tax exists in DEV

Date: 2026-09-07
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Closes the three items left in "Still owed" on
`docs/deployment/2026-09-07-portal-fixes-deployment.md`. That record's addendum diagnosed the
sign-in fault and deliberately left it; this one resolves it, and **corrects two facts in the
diagnosis that changed which resolution was correct.**

---

## 1. The addendum had the right conclusion and the wrong account

The addendum said an Entra sign-in lands on `Dev Account`, and it does. It also said the bound
identity was `Dev Account`'s own — `svc.automate.aq-dev@ascotlloyd.co.uk`. **It is not.**

`adx_externalidentity` stores an Entra object id, not an email, so the record *looks* settled:
it names a contact. Joining that object id back to `systemuser.azureactivedirectoryobjectid`
is what shows it belongs to a different account:

| Bound object id | Whose Entra account it is | Contact it resolves to |
|---|---|---|
| `e044a8e9-34ac-4503-8da4-e9573ccd234b` | `svc.automate.aq` (Service Account) | **Dev Account** (`svc.automate.aq-dev`) |

So the one binding is itself crossed — the *Service Account* signs in and becomes the *Dev
Account* contact, and therefore never picks up the `Administrators` role that the Service
Account **contact** holds. Two accounts, three identities, none of them lining up.

**The second correction matters more.** The addendum offered as option 3: "Sign in with the
local `Sims` username instead of Entra — changes nothing, and proves the diagnosis in one
attempt." That cannot be done:

```
Authentication/Registration/LocalLoginEnabled       false
Authentication/Registration/OpenRegistrationEnabled false
Authentication/Registration/Enabled                 false
```

Local login is off, so the `Sims` forms login is unreachable. Open registration is off too, so
an Entra sign-in whose object id is bound to nothing creates no contact and reaches nobody.

And `Simunye.Radingwana@ascotlloyd.co.uk`'s own object id
(`27696f2a-6045-4cce-98c4-faf4684ce1b2`) **had no `adx_externalidentity` record at all.** That
is the actual root cause: not that the grant was on the wrong contact, but that the person's
own sign-in had no route to any contact.

### What was done instead

A fourth option, and it is strictly better than the three the addendum listed: **bind the
person's own Entra object id to their own contact.** Additive — the existing binding is
untouched, so the Tax Reviewer sign-in still works — and no service account impersonates a
named person in the audit trail.

```
bindidentity <orgUrl> 27696f2a-6045-4cce-98c4-faf4684ce1b2 \
             Simunye.Radingwana@ascotlloyd.co.uk --confirm <orgUrl>
```

The identity provider is **copied from the binding that already works**, never typed. The
issuer has to match the site's provider exactly, and a plausible-looking wrong value produces
a sign-in that silently reaches nobody — which is indistinguishable from this very fault.

## 2. Manager roles now belong to somebody

`AL Portal - Portal Administrator` and `AL Portal - Outcome Testing Manager` were named in the
page rules and held by no contact. Both are now on the `Sims Rad` contact, granted through
`al_AssignUserRole` — the command the app itself calls — so the association and the
`al_userrolemapping` mirror are written together and the AD-089 conflict rule has nothing to
classify.

Verified by reading the association back, not by trusting the command's return:

```
roles before: AL Portal - AQS Reviewer
roles after : AL Portal - AQS Reviewer, AL Portal - Outcome Testing Manager,
              AL Portal - Portal Administrator
```

Both roles appear in the `Restrict read` rules for the Tax reviews page (`…00b1`), the AQS
reviews page (`…00b2`) and the Review page (`…00b3`), so this is what makes those rules
reachable by anyone at all.

## 3. There is Tax data now, and the reason there was none is not what was recorded

The previous record said "nothing has been routed to Tax". The stronger fact, found by query:
**all 13 cases carried the `AQS only` route.** No case could ever have produced a Tax review,
so this was never going to resolve itself by working through the queue.

`DEV verification - IO-DEV-VERIFY-003` — a fixture, chosen so no case that looks like real
work was disturbed — now carries `Tax only` and has been allocated:

```
Route 'Tax only': Tax=True, AQS=False
DEV verification - IO-DEV-VERIFY-003 -> route 'Tax only'.
Allocated to svc.automate.aq-dev@ascotlloyd.co.uk.
  review: Tax check type=Tax assigned=Dev Account
```

**The review instance was not written directly.** `routecase` creates an `al_caseassignment`
carrying only the case and the contact, exactly as the portal claim does, and leaves
`ClaimCasePlugin` to decide the discipline from the route. A hand-written review instance
would have proved the pages render; this proves routing produces the right check.

**It is assigned to `Dev Account` on purpose.** That contact holds `AL Portal - Tax Reviewer`,
and the `Review Instance - assigned to me (write scope)` permission names only the two
Reviewer roles — so this is the one arrangement in which the Tax path can be *written*, not
just read, without granting anyone a new role. Both Tax-owned sections are present and will
render on it:

| Section | Code | Owner |
|---|---|---|
| Tax check | `S-TAX` | Tax team |
| File Quality: Tax | `S-FQTAX` | Tax team |

## 4. The stale `annotationid` — removed, then put back

Removed from `outcome-testing.css.webfile.yml`, on reasoning a query supported: **no
`annotation` row exists for any web file on this site** — not for any of the nine ids in
source, and none by filename either. On the enhanced data model the content lives in a file
column on `powerpagecomponent`, so every one of those ids is dead.

The other eight were left in place deliberately, because the next upload was the evidence for
whether removing it was the right treatment. **That evidence arrived later the same day and
says it is not** — see the addendum at the end of this record.

The other stale id, `5140384b-…` in `.portalconfig/…-manifest.yml`, is a different record.
Recorded here as still outstanding; **it was not** — see "Still owed" below.

## 5. New tooling

| Command | Writes? | Purpose |
|---|---|---|
| `identities <orgUrl>` | No | Who a sign-in actually becomes. Joins `adx_externalidentity`, `systemuser.azureactivedirectoryobjectid` and the web role associations, and lists contacts nothing is bound to. |
| `bindidentity <orgUrl> <entraObjectId> <contactEmail> --confirm <orgUrl>` | Yes | Binds one Entra object id to one contact. Copies the provider from a working binding; refuses to repoint an object id already bound. |
| `grantrole <orgUrl> <contactEmail> <roleName> --confirm <orgUrl>` | Yes | Grants a web role through `al_AssignUserRole` and verifies the association landed. Unlike `proveroles`, does not withdraw afterwards. |
| `routecase <orgUrl> <caseName> <routeName> [<assigneeEmail>] --confirm <orgUrl>` | Yes | Points a case at a route and allocates it the way the portal claim does. |

`identities` exists because reading any one of those three tables alone is what produced the
wrong diagnosis, and it now reports the crossing in the environment as a `NOTE` line rather
than leaving it to be re-derived.

**One correction while writing `grantrole`.** Its role lookup first used the shared `FindId`
helper, which came back empty for every role that plainly exists — `mspp_webrole` does not
answer a plain `QueryExpression`, a quirk the 2026-09-06 record had already paid for once. The
pre-flight check is what caught it: it refused with "no web role is named …" instead of
writing a mapping row pointing at a role it could not confirm, which would have read as
granted and would not have been. It uses FetchXML now, with the name escaped, because one of
the shipped roles is `AL Portal - T&C Supervisor`.

## 6. State after, read back by query

```
External identities — who a portal sign-in becomes:
  e044a8e9-…  Entra svc.automate.aq@ascotlloyd.co.uk  -> Dev Account
              roles: AL Portal - Tax Reviewer
              NOTE: the bound Entra account is not the contact it resolves to
  27696f2a-…  Entra Simunye.Radingwana@ascotlloyd.co.uk -> Sims Rad
              roles: AL Portal - AQS Reviewer, AL Portal - Outcome Testing Manager,
                     AL Portal - Portal Administrator

Contacts with no binding:
  Service Account <svc.automate.aq@ascotlloyd.co.uk> — Administrators
```

Review instances: **10** — 9 AQS, **1 Tax** (`Tax check`, Assigned, `Dev Account`).

Nothing was deployed: no assembly, no portal upload, no app push. The only code change is the
registration tool, which is a local console and ships in nothing.

## Addendum — the `annotationid` removal is wrong, and the upload said so

Added after the remaining eight were removed and a portal deploy was run to test it. The
deploy **terminated**:

```
Loading Power Pages website manifest...
Record skipped: missing primary key 'annotationid' for entity 'annotation'
Record skipped: missing primary key 'annotationid' for entity 'annotation'
Sorry, the app encountered a non-recoverable error and will need to terminate.
Exception Type: System.InvalidCastException
```

`pac` reads a `.webfile.yml` as declaring an **`annotation` record as well as** an
`adx_webfile` — `filename`, `mimetype`, `isdocument`, `objectid` and `objecttypecode` are all
annotation columns — and it requires a primary key for that record **whether or not the row
exists**. The id being dead is exactly what the earlier query established, and it turns out not
to be the question: `pac` never looks.

So the two failure modes are not equivalent, and the familiar one is the cheaper:

| | With the id | Without it |
|---|---|---|
| What happens | `FAILED … Does Not Exist`, one line, upload continues | `InvalidCastException`, upload terminates |
| What deploys | everything | nothing |

**All nine ids are restored.** The `FAILED … Does Not Exist` line is noise that will keep
appearing, and the original concern behind removing it — that a real failure would be lost in a
familiar one — stands. It needs a different answer than deleting the key; the two candidates
are a `pac pages download` against this site to see what the tool itself writes, and reading the
upload log for the *set* of failure lines rather than their absence.

**Nothing was damaged.** The crash happens while loading the manifest, before any component is
uploaded, and `Deploy-Portal.ps1` restored `table-permissions/` on its way out. Verified after:

| Check | Result |
|---|---|
| Table permissions | 13 in source, 13 in DEV, nothing else |
| `Check-ComponentIds` | 237 identities, no duplicates, no web file faults |
| `Check-PortalSecurity` | all assertions pass |

### The confirming run

Re-run with the ids restored, and it settles the comparison rather than assuming it:

```
Found 72 records to process across 48 entities
Manifest loaded successfully.
Updating table powerpagecomponent with record ID:f065878a-… FAILED
  due to Entity 'powerpagecomponent' With Id = f065878a-… Does Not Exist
Uploading - [####################] 100,0% (Events: 19/19)
Power Pages website upload succeeded in 14,97 secs.
```

**The same dead id, the same FAILED line, and 19 of 19 events.** The line is emitted and the
upload carries on to completion — which is the whole point: with the key present the failure is
one line of noise, and without it the manifest never finishes loading.

Then 13 table permissions written by `restoretablepermissions`, and verified by query: 13 in
source, 13 in DEV, nothing else. `Portal deployed and verified.`

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Code App | `npm run build` then `npx pa app push` | Built clean, pushed successfully |
| 2 | Portal | `Deploy-Portal.ps1 -OrgUrl <orgUrl>` | Upload succeeded 19/19, 13 of 13 permissions written and verified |

The plug-in assembly was **not** redeployed and did not need to be: the only C# change in this
batch is `plugins/OutcomeTesting.Registration`, a local console that ships in nothing. The
schema change — deleting `al_contact_al_outcomecase` — was applied directly to DEV by
`deleterelationship`, so no solution import was required either.

Suites at the deployed commit: **plug-ins 376/376**, **app 192/192**, `tsc` clean.

## Still owed

- ~~Writing Tax answers as `Sims Rad` needs `AL Portal - Tax Reviewer`~~ **Settled later the
  same day.** The role is granted and verified by association readback. The grant alone would
  not have finished it: the write scope is Contact-anchored, so holding the role admits nobody
  to a review that is not assigned to them, and the only Tax review was on `Dev Account`.
  `IO-DEV-VERIFY-002` was routed `Tax only` and allocated, so there are now two Tax reviews,
  one per contact, and both sign-ins exercise the Tax path end to end.

  The cost of the decision, recorded rather than left to be discovered: one contact now holds
  reviewer **and** manager authority. That is what makes a one-person walk-through possible and
  equally what would hide a role-separation defect — anything that ought to be refused to a
  reviewer is permitted to this account by its manager roles, and nothing says which grant
  allowed it. Testing the separation itself needs a contact holding one role.
- **The crossed binding is still crossed**, but it is no longer pinned. `svc.automate.aq` still
  signs in as the `Dev Account` contact, and the `Service Account` contact — the one holding
  `Administrators` — is still reachable by no sign-in. It was left alone because the Tax
  Reviewer path ran through that binding; **that constraint is gone**, so the only open question
  is what the service accounts should be able to do.
- **A way to stop the `FAILED … Does Not Exist` line hiding a real failure** that is not
  deleting the `annotationid` — see the addendum for why that route is closed.
- ~~The stale `5140384b-…` in the portal manifest~~ **Already gone.** `6e2e6ec` removed it as
  a side effect of stripping the manifest for `Deploy-Portal.ps1`; it had been carried as
  outstanding in two records after it had been fixed.
