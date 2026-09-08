# Deployment — a repoint moves the identity username, and the dev account has its own sign-in

Date: 2026-09-08
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Second record of the day. Follows
`docs/deployment/2026-09-08-deactivation-fix-and-key-removal.md`, whose section 3 repointed
`svc.automate.aq`'s binding onto the `Service Account` contact. **That repoint was half a
repoint**, and both portal sign-in faults reported afterwards came out of the missing half.

---

## 1. The fault the repoint left behind

A Power Pages sign-in has two halves. `adx_externalidentity` says which contact an Entra
object id becomes; `contact.adx_identity_username` is the ASP.NET Identity username that
same object id signs in as. `bindidentity --repoint` wrote the first and left the second
where it was.

State found this morning, by query:

| Contact | `adx_identity_username` | External identity |
|---|---|---|
| Dev Account | `e044a8e9-…` — **the Service Account's object id** | none |
| Service Account | **(null)** | `e044a8e9-…` |
| Sims Rad | `Sims` | `27696f2a-…` |

So `svc.automate.aq` authenticated, resolved to `Service Account`, and then met a contact
whose identity username belonged to somebody else. **It fails after authentication, not at
it**, which is why it presented as "We're sorry, but something went wrong" — a generic page
carrying nothing that points back at a binding.

`svc.automate.aq-dev` failed differently for a different reason, and the difference is the
diagnosis. Its own object id, `c93725e2-f028-42de-a267-dafe731e87bc`, **had never been bound
to anything**; the binding it looked like it owned was always the other account's. An unbound
sign-in reaches the registration gate, and `Authentication/Registration/Enabled` is `false`,
so it was refused by name: "Registration has been disabled." That message was correct and was
the more useful of the two.

## 2. The command was fixed, not the rows

Patching the two contacts by hand would have left `--repoint` ready to do this again, and the
state it produces is invisible from `adx_externalidentity` alone.

`MoveIdentityUsername` now runs on both the repoint and the create path.
`adx_identity_username` is unique across contacts, so it clears the old holder before setting
the new one — that ordering is why it is one function and not two calls at the call site.

**The "already bound" path no longer reports `Nothing to do`.** A binding can point at the
right contact while the username still sits on the one it was moved away from, which is
exactly the state a pre-fix repoint leaves. Re-running the command has to be able to repair
that rather than report success and change nothing, and that is what repaired DEV — no
one-off script was written.

The create path sets the username too, because Power Pages sets it to the object id itself
when it creates a contact from an external sign-in. That is where the one on this site came
from, so a hand-made binding now matches what the platform would have written.

## 3. What the environment holds now

```
e044a8e9-…  svc.automate.aq@ascotlloyd.co.uk      -> Service Account   Administrators
c93725e2-…  svc.automate.aq-dev@ascotlloyd.co.uk  -> Dev Account       AL Portal - Tax Reviewer
27696f2a-…  Simunye.Radingwana@ascotlloyd.co.uk   -> Sims Rad          4 roles
```

`identities` reports no contact left unreachable. Every provider is
`https://sts.windows.net/4abde4fc-…/`, copied from a binding that already worked rather than
typed. Each contact's username is now its own object id, except `Sims Rad`, which keeps the
local `Sims` it has always had and has always signed in with.

`Dev Account` is reachable again, which the previous record noted it was not.

## 4. Four site settings put back

Between 09:42 and 09:45 the site's authentication settings were edited by hand while the
sign-in fault was being chased. None of the four matched `sitesetting.yml`, and they were not
the cause — the faults predate them — so they are reverted rather than kept.

| Setting | Was | Now |
|---|---|---|
| `Registration/TermsAgreementEnabled` | `true` | `false` |
| `Registration/OpenRegistrationEnabled` | `true` | `false` |
| `Registration/ProfileRedirectEnabled` | `false` | `true` |
| `Registration/LoginButtonAuthenticationType` | `https://login.windows.net/4abde4fc-…/` | *(empty)* |

Two are worth naming. **`OpenRegistrationEnabled = true` means any tenant sign-in
auto-creates a contact**, masked only by `Registration/Enabled` being `false` — one switch
away from an open door. And `LoginButtonAuthenticationType` named `login.windows.net` while
every binding and the actual issuer is `sts.windows.net`; a plausible-looking wrong issuer is
the failure this project has already been bitten by once.

**Entra remains the only sign-in method, which is the standing requirement.**
`LocalLoginEnabled` is `false`, `AzureADLoginEnabled` and `ExternalLoginEnabled` are `true`,
and one provider is configured — so the sign-in page shows the Entra button alone without
`LoginButtonAuthenticationType` being set to anything.

## 5. `setsitesetting`

Added for this, `--confirm`-gated because an authentication setting decides who gets into the
portal at all.

`sitesetting.yml` stays the source of truth and `pac pages upload` stays what normally applies
it; this verb writes the same value the next upload would, so the two converge rather than
fight. It exists for the narrow job of putting one field back without redeploying pages,
templates and table permissions that are already correct — the blast radius of a full upload
on this site is documented at the top of `Deploy-Portal.ps1` and is not worth taking for one
field.

It **refuses a name the site does not already carry** rather than creating a row: an unknown
name is far more likely to be a typo than a new setting, and a typo that silently creates a
row reads as applied while changing nothing about the site's behaviour. It reads the value
back, because on this site a successful-looking write is not evidence.

## 6. What is not proved

Every write above was verified by reading it back. **The sign-ins themselves were not
retested from a browser** — that needs the two accounts, and it is the only check that turns
this from a corrected state into a fixed fault.

The dev account's diagnosis is complete on its own evidence: no binding, registration off,
refused by name. The link between the orphaned username and the *generic* error page is
inference — a confirmed abnormal state, and the only abnormal state found on that path, but
the error page named nothing. If `svc.automate.aq` still fails, the investigation restarts
rather than a second fix being stacked on this one.

---

# Addendum, same day — the username was not the cause

Section 6 said the investigation would restart if `svc.automate.aq` still failed. It did, and
this is that restart. **Sections 1 and 2 above describe a real defect that is correctly fixed,
and a cause that was wrong.** Both statements hold; keeping them together is the point.

## 7. What the retest showed

`svc.automate.aq-dev` signed in. `svc.automate.aq` did not. The binding, the username and the
site settings were by then identical in shape across both, so the difference was not on the
path this record had been examining at all.

`Sims Rad` was then tested as a discriminator and **failed the same way**. That killed the
remaining role-shaped theory — `Administrators` versus `AL Portal - *` — because `Sims Rad`
holds four `AL Portal` roles and no `Administrators`.

| Contact | `adx_identity_securitystamp` | Created | Signs in |
|---|---|---|---|
| Dev Account | set | 08-29, **by Power Pages** | yes |
| Service Account | null | 09-03, by hand | no |
| Sims Rad | null | 09-03, by hand | no |

**Power Pages writes the ASP.NET Identity security stamp only for a contact it creates
itself.** It is the value the authentication cookie is validated against. A contact created by
hand gets an external identity binding and no stamp, and nothing about that is visible from
`adx_externalidentity` — which is why two rounds of investigation went past it.

Confirmed by single-variable test rather than by reasoning: the stamp was set on `Sims Rad`
alone, with `Service Account` deliberately left as an untouched control. `Sims Rad` signed in;
`Service Account` still did not. `Service Account` was then stamped too.

## 8. The command was fixed, again, not the rows

`CreateUserPlugin` created a contact carrying an email and a name and nothing else, so **every
person created through the app was born unable to sign in to the portal.** DEV had two such
contacts and both were the ones failing.

`ContactRegistry.SetSecurityStamp` is called on the **create branch only**. The idempotent
re-run must not touch it: rotating a stamp signs the person out everywhere, so a command that
reads as "create or refresh" must not quietly invalidate a session. Two tests pin it,
including that two contacts never receive the same stamp — a shared constant would be worse
than no stamp at all, since the stamp's only job is to invalidate one person's session.

`setsecuritystamp` fills the gap for contacts that already exist. It writes one column,
refuses a contact that already has a stamp, and deliberately leaves `adx_identity_lockoutenabled`
alone — that column also differs between a hand-made contact and one Power Pages built, and
changing both at once would have made the DEV test unreadable.

## 9. Answers could never be saved from the portal

Reported separately and found to be unrelated: a reviewer with the right role, on a review
assigned to them, in a fresh session, was refused with the portal's 401/403 message.

Creating an `al_response` that references an `al_reviewinstance` needs **Append on the child
and AppendTo on the parent**. `Response - on a review assigned to me` carries both. Neither
`al_reviewinstance` permission carried `AppendTo`:

| Permission | Table | Append | AppendTo |
|---|---|---|---|
| Response - on a review assigned to me | `al_response` | true | true |
| Review Instance - assigned to me (write scope) | `al_reviewinstance` | false | **false** |
| Review Instance - read all | `al_reviewinstance` | false | false |

`adx_appendto` is now `true` on `…071`, contact-scoped, so it reaches only reviews assigned to
the person holding a reviewer role. The repo already applied this pattern to `al_failreason`
and `al_remediationaction`; `al_reviewinstance` was the one that was missed.

**This had never worked from the portal for anyone.** The AD-089 write-path proof of
2026-09-07 exercised role adoption through the SDK, not the response write through the Web
API, so it passed while this path stayed broken — the same "seeding a result is how a broken
path stops being walked" failure that record's own section 2 names. A proof that runs as an
administrator proves nothing about a table permission.

## 10. Standing correction

This record's own first diagnosis was wrong, and it was wrong in the way this project keeps
finding: **a confirmed abnormal state was treated as the cause because it was the only
abnormal state that had been looked for.** The orphaned username was real, is fixed, and was
never why anyone saw an error page. The discriminator that eventually worked — testing a third
account whose configuration differed in a different dimension — cost one sign-in and would
have saved a deployment had it come first.
