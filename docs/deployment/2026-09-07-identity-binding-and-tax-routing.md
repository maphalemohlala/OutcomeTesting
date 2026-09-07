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

## 4. The stale `annotationid`

Removed from `outcome-testing.css.webfile.yml`. A query first confirmed the reasoning behind
it: **no `annotation` row exists for any web file on this site** — not for any of the nine ids
in source, and none by filename either. On the enhanced data model the content lives in a file
column on `powerpagecomponent`, so every one of those ids is dead.

Only this one is removed. It is the only file whose content changes, so it is the only one
that produces the `FAILED … Does Not Exist` line today; the other eight carry an equally dead
id and will produce it the moment they are edited. Left in place deliberately — the next
upload is the evidence for whether removing it is the right treatment, and that evidence is
worth having before applying it to eight more files.

The other stale id, `5140384b-…` in `.portalconfig/…-manifest.yml`, is a different record and
is untouched.

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

## Still owed

- **Writing Tax answers as `Sims Rad` needs `AL Portal - Tax Reviewer` on that contact.** The
  manager roles carry page *read* only; the write-scope permission
  (`Review Instance - assigned to me`) names just the two Reviewer roles. Reading the Tax
  page, the `File Quality: Tax` section and recorded fail reasons all work as things stand.
  One command closes it, and it is a real access grant, so it is left as a decision:
  `grantrole <orgUrl> Simunye.Radingwana@ascotlloyd.co.uk "AL Portal - Tax Reviewer" --confirm <orgUrl>`
- **The crossed binding is still crossed.** `svc.automate.aq` still signs in as the
  `Dev Account` contact, and the `Service Account` contact — the one holding `Administrators`
  — is still reachable by no sign-in. Left alone because the Tax Reviewer path is currently
  the only way to write a Tax check, and repointing it would remove that.
- **The eight remaining stale `annotationid`s**, pending the next upload's evidence.
- **The stale `5140384b-…` in the portal manifest**, unchanged from the 2026-09-03 record.
