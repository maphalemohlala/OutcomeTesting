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

## 10. The AppendTo fix was incomplete, and the audit that should have come first

Section 9's fix did not make answers savable. `al_response` binds **two** lookups, and only
one had been granted:

```
body['al_reviewinstanceid@odata.bind']  = '/al_reviewinstances(' + REVIEW_ID + ')';
body['al_questionversionid@odata.bind'] = '/al_questionversions(' + questionVersion + ')';
```

Rather than grant the second and retest, every `@odata.bind` in every web template was
listed and checked against the permission matrix. That is the whole set of writes the portal
performs, and it should have been the first move rather than the third:

| Create | Binds | Was | Now |
|---|---|---|---|
| `al_response` | `al_reviewinstance` | `appendto` false | true |
| `al_response` | `al_questionversion` | `appendto` false | true |
| `al_caseassignment` | *(the row itself)* | `append` false | true |
| `al_caseassignment` | `al_outcomecase` | `appendto` false | true |
| `al_caseassignment` | `contact` | no permission at all | **left alone** |

The claim from the queue was failing for the same reason and had also never worked:
`al_caseassignment` could be created but not related to anything.

**The contact binding is deliberately not granted.** Power Pages gives a signed-in user
implicit access to their own contact, and the claim binds `{{ user.id }}` — their own. A
contact table permission is a security decision about who can see people, not a debugging
step, and adding one speculatively to a table full of real individuals is the wrong way to
find out whether it is needed. If a claim still fails naming `prvAppendToContact`, that is
the evidence to decide on.

## 11. Standing correction

This record's own first diagnosis was wrong, and it was wrong in the way this project keeps
finding: **a confirmed abnormal state was treated as the cause because it was the only
abnormal state that had been looked for.** The orphaned username was real, is fixed, and was
never why anyone saw an error page. The discriminator that eventually worked — testing a third
account whose configuration differed in a different dimension — cost one sign-in and would
have saved a deployment had it come first.

The same shape repeated on the write path in section 10: one missing grant was found and
fixed, and the fix shipped without asking what else the same question would have turned up.
Enumerating the write payloads took one command and found three more gaps.

**None of these portal write paths has ever been exercised by a real portal user.** Claiming
a case and saving an answer are the two things the site exists to do, and both were refused
by the platform before any of this project's code ran. Every "proof" recorded to date drove
the plug-ins through the SDK as an administrator, which bypasses table permissions entirely.
Until a signed-in checker claims a case and saves an answer in a browser, the write paths are
unproven whatever this or any other record says — and the fixes here are, at the time of
writing, verified only by reading the deployed permissions back.

---

# Second addendum, same day — the refusal was never the permission chain

Section 11 said the next move was to enumerate before fixing. This is that enumeration, run
across every read and every write the portal performs, for the Tax and AQS reviewer roles.
It found that **the permission matrix was already correct**, and that the thing refusing the
write was not a permission at all.

## 12. What the audit settled first

The live site was downloaded (`pac pages download` into a scratch folder, not the repo) and
compared with source. **All thirteen table permissions match byte-for-byte**, so nothing
below is deployment drift. The environment was then queried directly:

| Fact | Value |
|---|---|
| Review `115e3dc1-…` | `al_reviewstatus` Assigned, `al_submitrequested` false |
| Its `al_assignedcontactid` | `Sims Rad` — the account that reported the failure |
| Sims Rad's roles | AQS Reviewer, Tax Reviewer, OT Manager, Portal Administrator |

`Authenticated Users` carries `mspp_authenticatedusersrole: true`, so every Global read grant
bound to *Administrators + Authenticated Users* does reach a reviewer. That had been read as
a gap more than once; it is not one.

So the message the page was showing — *"Answering needs a Tax or AQS reviewer role, and the
review has to be assigned to you"* — **was accusing the reader of the one thing that was
demonstrably already true.**

## 13. The read and write matrix

Reads. Every table the review path touches has a Global read grant that reaches both reviewer
roles, with two exceptions:

| Table | Read grant | Reaches Tax/AQS |
|---|---|---|
| `al_reviewinstance`, `al_outcomecase`, `al_section`, `al_question`, `al_questionversion`, `al_response`, `al_failreason`, `al_remediationaction` | yes | yes |
| **`al_reviewroute`** | **none** | — |
| **`al_outcome`** | **none** | — |

Writes, with what each one actually requires:

| Write | Needs | Held |
|---|---|---|
| `POST al_responses` | Create+Append `al_response`; AppendTo `al_reviewinstance`; AppendTo `al_questionversion` | all three yes |
| `PATCH al_responses(id)` | Write `al_response` | yes |
| `…/al_failreason_response/$ref` | Append `al_response`; AppendTo `al_failreason` | yes |
| `PATCH al_reviewinstances(id)` | Write `al_reviewinstance`, `If-Match: *` | yes |
| `POST al_caseassignments` | Create+Append `al_caseassignment`; AppendTo `al_outcomecase`; **AppendTo `contact`** | last one absent |

**Every permission the answer path needs was already granted**, including the two `AppendTo`
grants added earlier today in sections 9 and 10. Those fixes were correct and are not undone
by anything here.

## 14. 401 is not 403, and reading them as one cost three rounds

The console showed `POST /_api/al_responses` returning **401**. The portal answers **403**
when a table permission turns a write away; **401** means the request never authenticated.
`friendlyError` branched on `401 || 403` together and printed the permission message for
both, so a session failure was reported as a role failure — and three rounds of investigation
went looking at web roles that were correct the whole time.

The cause of the 401 is in this repo. `OT-Review-Detail` took its anti-forgery token from a
hidden input:

    var token = document.querySelector('[name="__RequestVerificationToken"]');
    if (token) { xhr.setRequestHeader('__RequestVerificationToken', token.value); }

Power Pages renders that input on a **server-rendered form**. This page is a hand-written web
template and carries no form, so the element has never existed — and `if (token)` **silently
dropped the header**, sending every write unauthenticated. Power Pages requires the token on
every Web API call, so it refused all of them.

The other three write paths on this site — the claim, and both remediation writes — use
`shell.getTokenDeferred()`, which is the documented mechanism, and always did.

**The submit path used to use it too.** It was changed to the hidden field, in a commit whose
own comment explains the reasoning: the two writes were assembled differently, so reconcile
them on *"the one that is known to work"*. Answering was not known to work. Section 11 of this
record establishes that no portal write path had ever been exercised by a signed-in user, so
there was no working reference — and reconciling on an unproven one moved the working half
onto the broken half. **This is the same failure this record keeps naming: a belief treated
as evidence because nobody had tested the thing it was about.**

## 15. The allowlist did govern the lookups

`Webapi/al_response/fields` listed the four answer columns and deliberately excluded both
lookups, on the reasoning that an `@odata.bind` travels as a navigation property and so
escapes the allowlist. That reasoning was wrong, and **the repo already contained its own
refutation**:

| Setting | Lists its lookups? |
|---|---|
| `Webapi/al_caseassignment/fields` | `al_outcomecaseid`, `al_assignedcontactid` — yes |
| `Webapi/al_signoff/fields` | `al_remediationactionid` — yes |
| `Webapi/al_response/fields` | **no** |

`al_response` was the one write path that omitted its own lookups and the one whose create was
refused. Listing them widens nothing the plug-in does not already police: `ResponseGuardPlugin`
re-reads both, and the Parent-scoped permission still confines the write to a review assigned
to the caller.

## 16. Deployed, and verified by query

`Check-PortalSecurity.ps1` and `Check-ComponentIds.ps1` both pass. `Deploy-Portal.ps1` ran
against `Env_AQ_Dev`; 13 of 13 table permissions verified present and nothing extra.

| Change | Verified in DEV |
|---|---|
| `OT-Review-Detail` autosave + submit take the token from `shell.getTokenDeferred()` | 8 occurrences deployed, **0** hidden-field reads left |
| 401 and 403 now carry different messages | 2 `401` branches deployed |
| `Webapi/al_response/fields` gains both lookups | value read back, modified 12:17:34Z |

One upload event failed: `powerpagecomponent f065878a-f8a9-f111-aaac-e4fade069307 Does Not
Exist`. That is the `annotationid` of the `outcome-testing.css` web file — **pre-existing
manifest drift, not part of this change**, and it should be cleared separately rather than
folded in here.

## 17. What was deliberately not done

- **`AppendTo` on `contact`.** Section 10 held this back as a security decision and named the
  evidence that would settle it: a claim failing on `prvAppendToContact`. The claim does still
  fail, but its refusal has not been read yet, and the claim page's token handling was never
  broken — so its cause is genuinely separate from section 14's and is still unidentified.
  Granting portal users access to the contact table on a guess is exactly what section 10
  refused to do, and one more round of guessing is not what this record needs.
- **Read grants on `al_reviewroute` and `al_outcome`.** Both are real gaps. `al_reviewroute` is
  joined `link-type="inner"` by the queue, so the Tax and AQS queues render empty for a
  reviewer whatever happens to the claim; `al_outcome` is the primary entity of the four home
  page counters, which therefore read zero. Both add new data access for authenticated users
  and neither is on the answer path, so they are not being bundled into a fix for it.
- **`Webapi/error/innererror`.** It would have made every refusal in this investigation
  readable, and it returns inner error detail — stack traces included — to the browser, which
  is what PP-16 and NFR-SEC-01 forbid. Worth turning on in DEV for a single diagnostic run and
  off again; not worth leaving on, and not a repair.

## 18. What is still not proved

The same sentence as section 6, for the same reason. **Both changes were verified by reading
the deployed values back, and neither has been exercised by a signed-in reviewer in a
browser.** Reading a deployed value back proves it deployed; it proves nothing about whether
an answer now saves. Site cache can hold a template for around fifteen minutes.

The check that settles it is one reviewer, on a review assigned to them, changing one answer
and seeing "Saved". If it still refuses, **the response body and the status code are the
evidence** — a 403 now means the permission chain and is worth investigating as one, and a
401 now means the token still is not arriving, which would mean `shell` itself is not loading
on this page.

## 19. Sign-in, unchanged and still unexplained

Nothing here touches sign-in, and the two failing accounts were re-checked rather than
assumed. Every column implicated across sections 1, 2, 7 and 8 is now correct and uniform:

| Contact | Binding | Provider | Stamp | Logon |
|---|---|---|---|---|
| Service Account | `e044a8e9-…` | `sts.windows.net/4abde4fc-…` | set | enabled |
| Dev Account | `c93725e2-…` | same | set | enabled |
| Sims Rad | `27696f2a-…` | same | set | enabled |

All three are active, each bound to its own contact, each with a distinct security stamp. The
only remaining difference is `adx_identity_username` — `Sims` against a raw object id — and
section 7 records `Dev Account` signing in *with* an object-id username, so that does not
explain it either. **No fourth theory is being recorded here on no evidence.**

The next step is not another query. It is `Site Actions → Disable custom errors` in the
admin centre, or `Enable diagnostic logs` plus the Activity ID printed on the generic page,
which turns "We're sorry" into the actual exception. Custom errors cannot be turned off while
the site is private, which is worth checking first.

---

# Third addendum, same day — 401 became 400, and the payload was the last thing wrong

Section 18 said the status code would discriminate on the retest. It did. **The refusal moved
from 401 to 400**, which is the first time in this investigation that a fix has been confirmed
by the failure changing shape rather than by a value read back.

## 20. What the 400 proved about the 401

A 401 meant the request never authenticated. A 400 means it authenticated, reached the Web
API, and was rejected as malformed. **So section 14's diagnosis was right and its fix works**:
`shell.getTokenDeferred()` delivers a token the site accepts, and the anti-forgery header is
now arriving on every write from the review page.

It also means the permission chain is still untouched by any of this. A malformed payload
never reaches a table permission.

## 21. Every `@odata.bind` on the site named the wrong property

`@odata.bind` does not take a lookup's logical name. It takes the relationship's
**referencing navigation property name**, which carries the relationship's own casing and is
case-sensitive. All five binds on this site used the logical name:

| Template | Was | Is |
|---|---|---|
| `OT-Review-Detail` | `al_reviewinstanceid` | `al_reviewinstance_response` |
| `OT-Review-Detail` | `al_questionversionid` | `al_questionversion_response` |
| `OT-Review-List` | `al_outcomecaseid` | `al_outcomecase_caseassignment` |
| `OT-Review-List` | `al_assignedcontactid` | `contact_al_caseassignment` |
| `OT-Remediation` | `al_remediationactionid` | `al_remediationaction_signoff` |

Read out of metadata, not inferred. An unrecognised property makes the whole payload
malformed, so the portal answers 400 and **names nothing** — which is why this survived three
rounds of investigation into permissions that were correct throughout.

**This is the same defect in all five places, so all five were corrected together.** Section
11's lesson, applied rather than restated: the question that found one instance was asked of
every write on the site before anything was deployed.

It also finally explains the claim. Section 17 left `AppendTo` on `contact` as an open
security decision, on the reasoning that a still-failing claim might be evidence for it.
**It was not.** The claim was failing because `al_assignedcontactid@odata.bind` is not a
property Dataverse recognises, and it never reached the point where a contact permission
would have been consulted. Had that grant been added on the strength of the claim failing,
it would have widened access to a table of real individuals for a fault that had nothing to
do with permissions — and it would have appeared to work, because the bind fix landed in the
same deployment. **Section 10 was right to refuse to guess, and section 17 was right to keep
refusing.**

## 22. The command was extended, not the knowledge written down

`relationships <orgUrl> <table>` printed many-to-many relationships only. It now also prints
every lookup with the navigation property name each is addressed by:

    al_reviewinstanceid  -> al_reviewinstance  @odata.bind name=al_reviewinstance_response

Recording the five correct names in a document would have been the smaller change and the
wrong one: the next lookup added to this solution would not be in it. The command answers the
question for any table, including tables that do not exist yet, and every corrected bind now
carries a comment naming the command rather than the reasoning.

## 23. Deployed, and verified by query

Both gates pass. `Deploy-Portal.ps1` ran against `Env_AQ_Dev`, 13 of 13 table permissions
verified and nothing extra. The three templates were then read back out of the environment:

| Bind | Deployed | Stale logical-name form remaining |
|---|---|---|
| `al_reviewinstance_response` | 1 | 0 |
| `al_questionversion_response` | 1 | 0 |
| `al_outcomecase_caseassignment` | 1 | 0 |
| `contact_al_caseassignment` | 1 | 0 |
| `al_remediationaction_signoff` | 1 | 0 |

## 24. What is still not proved, and what can no longer be isolated

**No answer has been saved yet.** The 400 is evidence that the token fix worked; it is not
evidence that anything now succeeds. Site cache can hold a template for around fifteen
minutes.

Two things can no longer be tested apart, and the record should say so rather than imply a
cleanliness it does not have:

- **The `Webapi/al_response/fields` change from section 15 has never been exercised on its
  own.** Every request since it was made has been refused for a different reason first. It
  matches the documented contract and the pattern the other two write paths already use, and
  that is the whole of the evidence for it.
- **The claim's bind fix and the contact-permission question landed in the same deployment.**
  If the claim now works, that is the bind fix; there is no contact grant to credit. If it
  fails on `prvAppendToContact`, *that* is finally the evidence section 10 asked for — and it
  will be a permission refusal, 403, not a 400.

The check is unchanged: one reviewer, one answer, "Saved". **A fourth distinct status code
would mean a fourth distinct fault**, and on this site that has been the pattern rather than
the exception.

---

# Fourth addendum, same day — the error text, and the twenty minutes it would have saved

Section 24 said a fourth status would mean a fourth fault. It was 403, and it was. This time
the refusal was read rather than inferred, and it named its own cause in one line:

    {"error":{"code":"90040106",
              "message":"You don't have permission to associate or disassociate
                         table al_reviewinstance to al_response"}}

**This body was available on every one of the previous three rounds and was never read.**
Three diagnoses were reasoned out of status codes alone while the server was saying exactly
what was wrong. That is the single most expensive habit in this record.

## 25. The request contained its own control

The payload binds two tables in one create:

    {"al_answerchoice":120910305,
     "al_reviewinstance_response@odata.bind":"/al_reviewinstances(b63c53c7-…)",
     "al_questionversion_response@odata.bind":"/al_questionversions(3ce90bf2-…)"}

The error names `al_reviewinstance` and **not** `al_questionversion`. Same request, same
contact, same roles, same moment — so whatever differs between those two tables is the cause,
and nothing else can be. A controlled comparison inside a single HTTP request.

What differs is which permission carries `AppendTo`:

| Bind target | Global permission | Contact permission |
|---|---|---|
| `al_questionversion` | `AppendTo` **true** | — |
| `al_outcomecase` | `AppendTo` **true** | — |
| `al_failreason` | `AppendTo` **true** | — |
| `al_remediationaction` | `AppendTo` **true** | AppendTo false |
| **`al_reviewinstance`** | `AppendTo` **false** | AppendTo **true** |

**`al_reviewinstance` was the only bind target on the site whose Global permission lacked
`AppendTo`**, and the only one the platform refused.

## 26. Why section 9's fix did not work

Section 9 found the missing `AppendTo` on `al_reviewinstance` and set it on `…071`,
`Review Instance - assigned to me`, reasoning that a contact-scoped grant "reaches only
reviews assigned to the person holding a reviewer role". That reasoning is sound as security
design and it is not what the platform evaluates: the association check did not accept a
Contact-scoped `AppendTo` here, and refused.

Section 10, one section later, hit the same class of gap on `al_questionversion` and fixed it
on the **Global read** permission — and that one has worked ever since. **Both fixes are in
this record, three paragraphs apart, and they contradict each other.** Nobody noticed, because
neither was ever exercised: the write was failing further upstream on the anti-forgery token
the whole time, so both fixes returned the same 401 and neither could be told from the other.

`adx_appendto` is now `true` on `…074`, `Review Instance - read all`, Global scope. The
contact-scoped grant on `…071` is left in place — it is not doing harm, and removing it in the
same change would make the next test unreadable for exactly the reason this section describes.

## 27. What this widens, stated plainly

`AppendTo` on a Global permission means an authenticated user may relate a record to any
review instance, not only ones assigned to them. That is a real widening and it should be
recorded as one rather than buried as a bug fix.

It is bounded by what has not changed. Creating an `al_response` still requires the
Parent-scoped `…072`, which resolves through `al_reviewinstance_response` to a review the
caller is assigned to; `ResponseGuardPlugin` still re-reads both lookups and still owns the
submission lock, the AD-023 column match and AD-020 section ownership. `AppendTo` grants no
read, no write, no create and no delete, so `Check-PortalSecurity.ps1` assertion 9 is
untouched — and this is the same grant the site already makes on four other tables.

## 28. Deployed, and verified by query

Both gates pass. 13 of 13 table permissions deployed and nothing extra. Read back:

| Permission | Scope | AppendTo |
|---|---|---|
| `Review Instance - read all` (`…074`) | Global | **true** |
| `Review Instance - assigned to me` (`…071`) | Contact | true |
| `Question Version - read` (`…067`) | Global | true |
| `Response - on a review assigned to me` (`…072`) | Parent | true, and Append true |

## 29. One thing to confirm, and one thing still unproved

**The page and the payload appeared to disagree about which review they were on, and did
not.** The console line reads `review/?id=235e3dc1-…` (IO-100004) while the payload binds
`b63c53c7-…` (IO-100007). Confirmed with the tester: the review under test was the one on
IO-100007, and the console line came from an earlier capture on a different page load. The
questionnaire bound the review it was actually rendering. **No defect here**, and the
apparent one is recorded rather than deleted because a page binding the wrong review would
have been serious, and the next reader comparing these two ids deserves to find the answer
instead of repeating the check.

And the standing caveat, unchanged: **no answer has been saved yet.** Four faults have been
found and fixed on this path today — the anti-forgery token, the Web API field allowlist, five
wrong `@odata.bind` property names, and this `AppendTo` — and each was only visible once the
one in front of it was gone. There is no evidence that this is the last one. The check is
still one reviewer, one answer, "Saved".

## 30. The error code, decoded — and a correction to section 25

The refusal was looked up rather than interpreted. From Microsoft's portal Web API error table:

| Code | Name | Requires |
|---|---|---|
| 90040105 | `TablePermissionAppendIsMissngDuringAssociationChange` | `Append` on `{0}` |
| 90040106 | `TablePermissionAppendToIsMissingDuringAssociationChange` | **`AppendTo` on `{1}`** |

Ours is 90040106, `"…table al_reviewinstance to al_response"`, so `{1}` is `al_reviewinstance`
and the missing grant is `AppendTo` on it. Unambiguous, and it is the grant section 28
deployed.

**Section 25 overstated its case.** It called the two binds in one payload a controlled
comparison proving `al_questionversion` passed. The error names only the first table to fail,
so a table not named is not thereby proved to have passed — it may simply be checked later.
The payload order happens to put `al_questionversion` first, which does support it passing,
but that is weaker evidence than section 25 claimed and the record should say so.

The conclusion of section 26 is unaffected and is now better supported by timing: the
contact-scoped `AppendTo` on `…071` had been deployed since 11:19, hours before the first 403,
so no cache could have been hiding it. **A Contact-scoped `AppendTo` does not satisfy this
check.**

## 31. Configuration verified, propagation not

After section 28's deploy the site was downloaded again and both permissions read back:

| Permission | Scope | AppendTo | Web roles |
|---|---|---|---|
| `Review Instance - read all` (`…074`) | Global | true | Administrators, Authenticated Users |
| `Review Instance - assigned to me` (`…071`) | Contact | true | Tax `…090`, AQS `…091` |

`Sims Rad` holds `…091` directly and `Authenticated Users` implicitly, so the grant the
platform reports as missing is present at both scopes, with intact role links.

**`al_questionversion` is now configuration-identical to `al_reviewinstance`** — Global read,
`AppendTo` true, Administrators plus Authenticated Users — and passes this same check in this
same request. That is a falsifiable prediction rather than a hope: once the site's server-side
cache is cleared, `al_reviewinstance` should behave exactly as `al_questionversion` does.

The Global grant landed at 14:47, minutes before the retest. The portals Web API is documented
as server-side cached. **Nothing further is being deployed**, because the configuration is
correct and a fifth change stacked on an unpropagated fourth would be unreadable — the failure
this record has now made four times.

Next action is `Site Actions -> Restart site` in the admin centre, then one retest. **If the
identical message survives a restart, the prediction is falsified**: Global `AppendTo` is not
what this check wants either, that is two failed fixes on this one 403, and the approach —
not the value — is what should be questioned next.

## 32. The prediction was falsified, and the falsification was the useful part

Section 31 predicted that a site restart would make `al_reviewinstance` behave as
`al_questionversion` does. **It did not.** Identical 403, identical message, after a restart.

That is worth more than another fix would have been, because it rules out the value rather
than adding to it. Both `al_reviewinstance` permissions carry `AppendTo: true` — Global
(`…074`) and Contact (`…071`). Under *any* selection strategy the platform could be using —
union, most-specific-wins, Global-only — `AppendTo` on `al_reviewinstance` is true. The
platform says it is missing.

**So the permission being consulted is neither of them.** The question stopped being "is the
grant right" and became "does the grant reach this user at all".

## 33. Four roles claimed to be the Authenticated Users role

Asked directly — does the authenticated-users role affect this — and the answer is that the
site had four of them:

| Web role | DEV | `webrole.yml` |
|---|---|---|
| AL Portal - Tax Reviewer | **true** | false |
| AL Portal - AQS Reviewer | **true** | false |
| AL Portal - Portal Administrator | **true** | false |
| Authenticated Users | true | true |

Power Pages expects exactly one web role per website to carry `authenticatedusersrole`. With
four, which role a signed-in user actually receives is not defined — and every Global grant on
this site, including `Review Instance - read all` and its `AppendTo`, hangs off
`Authenticated Users`. If that role is not the one attached, the only `AppendTo` left on
`al_reviewinstance` is the contact-scoped one, which section 26 established does not satisfy
this check. **That is consistent with every observation, including the ones that killed the
previous two fixes.**

Three facts about how this survived so long:

1. **It is drift.** Source declares one such role. DEV had four.
2. **It changed during this session.** The first query of `mspp_webrole`, at about 11:50,
   returned `false` for `AL Portal - Tax Reviewer`. It was `true` by 15:00. Something in the
   intervening deploys set it, and this record cannot yet say what.
3. **`pac pages download` reports all three as `false`.** The downloaded `webrole.yml`
   disagrees with a direct query of the same environment, taken minutes apart.

Point 3 is the one with consequences beyond today. **"Verified by reading the deployed state
back" has been this record's standard of proof since section 5, and for this field the
download is simply wrong.** Every check today that compared source with a download — including
the ones in sections 12, 28 and 31 — was blind to it. A direct query is not a slower way of
doing the same thing; for this field it is the only way.

Corrected with `setwebroleauth`, which exists for exactly this, and read back by query: one
role now carries the flag, and it is `Authenticated Users`.

## 34. What this is and is not

**It is a real misconfiguration, corrected, and it would have had to be corrected whatever
else is true.** A site with four authenticated-users roles has undefined role resolution, and
no permission conclusion drawn on it is trustworthy — including the ones in sections 26 and
31, which were reasoned against a site in this state.

**It is not yet a diagnosis.** Two fixes have already failed on this 403 while looking
plausible. The site needs a restart to pick up role configuration, and then one answer needs
to save. Until it does, this is the third candidate, not the cause.

**If it still fails after a restart, the method changes rather than the value.** Three failed
fixes on one refusal means the approach is wrong, not the number: at that point the move is to
stop reasoning about which grant is missing and bisect it — grant the reviewer roles a
deliberately over-wide permission on `al_reviewinstance` in DEV, confirm the write succeeds at
all, then narrow until it breaks. That answers the question in two tests instead of n guesses,
and it is what should have happened after the first failed fix.

## 35. The role-flag fix did not fix it either, and the method changed

Checked by query rather than by asking: **no `al_response` row has ever been created from the
portal.**

| Check | Result |
|---|---|
| `al_response` rows created 2026-09-08 | 0 |
| Rows on review `b63c53c7` (under test) | 0 |
| Distinct `createdby` across all 108 rows | `svc automate aq` only, all seeded 09-06 |

That independently confirms section 11's claim from the data instead of from reasoning, and it
makes three failed fixes on this one 403: Global `AppendTo` (section 28), the site restart
(section 31), and the authenticated-users role flags (section 33). **Three failures means the
method is wrong, not the value**, so no fourth value is being guessed.

### The bisect, and its predictions, written down first

`Review Instance - read all` (`…074`) carries the Global `AppendTo` but was bound only to
`Administrators` and `Authenticated Users`. It is now also bound directly to
`AL Portal - Tax Reviewer` and `AL Portal - AQS Reviewer`. Read and `AppendTo` only, so
assertion 9 still passes, and it is a defensible permanent grant if it turns out to be the fix.

This splits the two surviving hypotheses, and both outcomes are recorded now so that neither
can be rationalised afterwards:

- **If an answer saves:** the grant was never reaching the user. `Authenticated Users` delivers
  reads on this site but does not deliver `AppendTo` for the association check — which would
  also mean sections 26 and 31 drew the right conclusion from the wrong cause, since every
  Global grant they reasoned about hangs off that role.
- **If it still refuses identically:** `AppendTo` on `al_reviewinstance` is **not** the real
  blocker, whatever error 90040106 names. The next probe then targets permission *selection*
  rather than permission *value*: `al_reviewinstance` is the only bind target on this site
  carrying **two** permissions, and the hypothesis becomes that Power Pages evaluates only the
  most specific one — the Contact-scoped `…071` — which section 26 established does not satisfy
  this check. `al_questionversion`, which passes, has exactly one permission.

That second hypothesis is the one this record should have reached hours ago. It is the only
structural difference between the bind target that works and the bind target that does not, and
it was visible in the permission matrix in section 13.
