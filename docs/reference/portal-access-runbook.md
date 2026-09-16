# Giving someone access to the portal

Living reference. Written 2026-09-14 after Env_AQ_Test was provisioned from empty, where
each of the four requirements below was found the hard way, one failed sign-in at a time.
Extended 2026-09-15 with **gate 0**, which sits above all four and was missing from the first
version of this page -- its absence sent a reported failure straight at the bindings, which
were already correct.

Entra is the only sign-in method on this site. `LocalLoginEnabled` is `false`, there is no
OpenIdConnect provider registration, and the built-in Entra provider is enabled through
`Authentication/Registration/AzureADLoginEnabled`.

---

## Gate 0 -- site visibility

**Both sites are Private.** A private Power Pages site refuses everyone except the people
explicitly granted access, and that check runs *before* Power Pages authentication -- before
contacts, bindings, stamps and web roles are consulted at all. Someone can pass every one of
the four requirements below and still never reach the site.

| What they see | URL |
|---|---|
| "You don't have access to this / You do not have permissions to access this site." | `/private-mode-access-denied` |

Note the wording: it refuses them from the **site**, not from a sign-in. A registration or
binding fault never produces this page.

Access is granted in the Power Pages design studio under **Set up -> Site visibility ->
Manage access**. It lives in the Power Pages management service, **not in Dataverse**, so:

- Nothing in `sitesetting.yml` controls it, and no `mspp_` table records it.
- No `pac` command and no verb of the registration tool can read or write it.
- The `identities` report cannot see it, and neither can any FetchXML in this document.

It has to be done in the maker portal by hand. Prefer granting **one Entra security group**
and managing membership in Entra, so provisioning a person becomes group membership plus the
four requirements below, with no per-person portal step.

### The trap: administrators bypass it

Holders of the Dataverse **System Administrator** role, and environment admins, reach a
private site without appearing in Manage access at all. They are not on the list; they do not
need to be.

That makes an admin account a **useless test subject**, and this is how gate 0 stayed hidden.
On 2026-09-15 a reported failure for Zoe Ramwell was investigated against Simunye Radingwana,
who could see the site -- but Simunye holds System Administrator and Zoe does not, so the two
accounts were never running the same gauntlet. Zoe's four requirements were already identical
to his.

Holders of System Administrator in TEST as at 2026-09-15, excluding Microsoft service
principals: `Simunye.Radingwana`, `Jordan.Starling`, `HarrisonS.admin`, and the
`svc.automate.aq` service account the tooling itself authenticates as.

**Test with an account that holds no admin role**, or the test proves nothing.

---

## What a sign-in actually needs

Four things, all on or beside the contact. **Three of four is a failed sign-in**, and the
failures are hard to tell apart from the outside.

| # | What | Where | Set by |
|---|---|---|---|
| 1 | Binding | `adx_externalidentity` row, Entra object id -> contact | `bindidentity` |
| 2 | Identity username | `contact.adx_identity_username` | `bindidentity` (same call) |
| 3 | Security stamp | `contact.adx_identity_securitystamp` | `setsecuritystamp` |
| 4 | Web authentication | `contact.adx_identity_logonenabled` | `enableportallogin` |

Power Pages writes all four itself for a contact **it** creates during registration. A
contact created any other way — the app, a seed, the maker portal, `migratetocontacts` —
gets none of them, and nothing about that is visible from `adx_externalidentity`.

A web role is a fifth thing, but a separate one: without it the sign-in succeeds and the
person sees an empty site rather than being refused.

---

## Someone already provisioned

Nothing to run, and nothing for them to fill in.

They go to the site and sign in with their Ascot Lloyd account. There is no registration
step, no invitation code and no email to confirm. If they land on a page asking them to
register, they are **not** provisioned — go to the next section. If instead they are told
they have no access to the **site**, that is gate 0 and no amount of provisioning fixes it.

Test: `https://outcometestingtest.powerappsportals.com`

---

## Provisioning a new person

Four commands. Repeat the org URL after `--confirm`; that is the guard, not a formality.
`DOTNET_ROLL_FORWARD=Major` is needed on this machine, and **only one of these can run at a
time** — each signs in separately and they will collide otherwise.

```bash
ORG=https://org37995f36.crm11.dynamics.com/     # Env_AQ_Test
DLL=plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll
export DOTNET_ROLL_FORWARD=Major
```

**1. Get their Entra object id from the target environment itself**, not from a note or
another environment. It is tenant-wide so it does carry across, but reading it where you are
about to write it is one fewer assumption:

```
<fetch><entity name="systemuser">
  <attribute name="azureactivedirectoryobjectid"/>
  <attribute name="internalemailaddress"/>
  <filter><condition attribute="azureactivedirectoryobjectid" operator="not-null"/></filter>
</entity></fetch>
```

Run it with `pac env fetch --environment $ORG --xmlFile <file>`. Pass the query as a **file**;
inline `--xml` crashes with an XmlException.

**This only works for someone who is also a Dataverse user.** A portal-only person has a
contact and no `systemuser` row, so Dataverse does not hold their object id anywhere and this
query returns nothing — in either environment. Found on 2026-09-16 with Adam Strumidlo, whose
five web roles were already mapped and whose contact was already in TEST, but who has no
`systemuser` row in TEST or DEV. There is no query in this document that can recover the id
for such a person: it has to come from the Entra admin centre, or they have to be given a
Dataverse licence. `bindidentity` takes the object id as an argument and cannot look it up.

**2. The contact must already exist** with their email in `emailaddress1`. `bindidentity`
refuses an email no contact has, and it will not create one.

**3. Bind, stamp, enable:**

```bash
dotnet $DLL bindidentity      $ORG <objectId> <email> --confirm $ORG
dotnet $DLL setsecuritystamp  $ORG <email>            --confirm $ORG
dotnet $DLL enableportallogin $ORG <email>            --confirm $ORG
```

`bindidentity` copies the issuer from a binding that already works rather than taking it
typed, so **the first binding in an empty environment cannot be made this way** — see below.

**4. Give them a web role** if they do not have one, with `grantrole`.

Then have them sign in. Do not treat the writes as the finish; every one of them reads back
clean whether or not the sign-in works.

---

## The first person in a brand new environment

`bindidentity` has nothing to copy the issuer from and refuses. Breaking that circle:

1. Turn `Authentication/Registration/OpenRegistrationEnabled` on **temporarily**.
2. Have one person **whose email has no contact yet** sign in. Power Pages creates the
   contact and writes all four requirements itself.
3. Turn `OpenRegistrationEnabled` straight back off.
4. Everyone else goes through the four commands above, using that first binding's issuer.

The person in step 2 must have no existing contact. Open registration only ever *creates*
contacts; for an email that already has one it refuses with "The email ... is already taken"
and loops on the registration form forever. That is not a transient error and retrying never
clears it.

---

## Reading a failure

| What they see | What it means |
|---|---|
| "You don't have access to this", at `/private-mode-access-denied` | **Gate 0.** Site visibility is Private and they have not been granted access. Nothing to do with contacts |
| "Registration has been disabled. Invalid sign-in attempt." | Registration is off and they are unbound, **or** `ExternalLoginEnabled` is off |
| Redirected to `/Register`, or "Register your external account" | Unbound, **or** bound with `logonenabled` false — these look identical |
| "The email ... is already taken" | Open registration is on and their contact already exists. Bind instead |
| Signs in, site is empty | All four fine; they hold no web role |

The second row is the trap. A correct binding with `logonenabled` false presents exactly like
no binding at all, so confirm gate 4 before concluding the binding is wrong.

**The best diagnostic is a working contact -- but only a comparable one.** Read one that
signs in against one that does not, in the same environment, and let the differing column
name itself.

The comparison is only valid if both accounts hold the **same Dataverse security role**. An
administrator passes gate 0 for free, so comparing a normal user against one tells you
nothing about site visibility and the differing column will not be a contact column at all.
Check `systemuserroles` before trusting the comparison -- on 2026-09-15 this exact mistake
cost an investigation, because the two contacts really were identical.

---

## Settings that must hold

| Setting | Value | Why |
|---|---|---|
| `Registration/Enabled` | `true` | The gate an unbound sign-in reaches |
| `Registration/ExternalLoginEnabled` | `true` | **`false` refuses every Entra sign-in** |
| `Registration/OpenRegistrationEnabled` | `false` | `true` lets any tenant account mint a contact |
| `Registration/LocalLoginEnabled` | `false` | Entra is the only method |
| `Registration/LoginButtonAuthenticationType` | *(empty)* | A wrong issuer here has bitten this project twice |
| `LoginTrackingEnabled` | `true` | Set true 2026-09-15, but it records nothing even so -- see "Verifying without a screenshot" |

`sitesetting.yml` is the source of truth and `setsitesetting` writes the same value the next
upload would. **This site serves settings from a cache**: a change can take effect well after
it reads back correctly, so a setting that appears to do nothing has not been disproved yet.

**A setting can exist twice.** `setsitesetting` resolves a name to a row and updates it; if two
rows share a name it can neither see nor clear the other, and which one the site reads is not
something to rely on. TEST carried two `Webapi/error/innererror` rows from 2026-09-14 until
2026-09-15 and stayed consistent only because both said `true`. Duplicates arise when a row is
created in one environment and then an upload or a `--create` supplies a different id for the
same name, so **check the count, not just the value**:

```
<fetch><entity name="mspp_sitesetting"><attribute name="mspp_name"/>
  <attribute name="mspp_value"/><order attribute="mspp_name"/></entity></fetch>
```

Compare the row count against the number of distinct names. Remove a duplicate **by id**:

```bash
dotnet $DLL deletesitesetting $ORG <sitesettingid> --confirm $ORG
```

It prints every row of that name and marks which it will delete, and it refuses to delete the
only row of a name unless you also pass `--last` — deleting a setting outright is a different
act from de-duplicating one.

---

## Verifying without a screenshot -- you cannot

**This does not work on this site. Do not use `adx_identity_lastsuccessfullogin` to decide
whether anyone can sign in.** The section below is kept because the reasoning is what matters,
not because the check works.

The claim it used to make was that `Authentication/LoginTrackingEnabled` being `true` makes a
successful sign-in stamp `contact.adx_identity_lastsuccessfullogin`, so querying that column
beats asking what the screen said. That is false here, and it was disproven the only way it
could be -- by someone signing in.

**Evidence, 2026-09-16.** `Authentication/LoginTrackingEnabled` had been `true` in TEST since
2026-09-15 10:14. Zoe Ramwell signed in successfully that afternoon, confirmed by the project
owner. Her contact still read `adx_identity_lastsuccessfullogin` null, and her `modifiedon` was
still 2026-09-14 08:51 -- untouched, two days stale, older than the setting itself. A
demonstrably working sign-in left no trace in Dataverse at all.

It is not one environment and not one account. **Every contact in TEST and every contact in
DEV reads null**, with the setting `true` in both since 2026-09-15 10:14 -- including the
`svc.automate.aq` service account, whose portal contact has been bound since 2026-09-14. The
column has never held a value for anybody, under any configuration this project has run.

Why is not established. The untested candidates are that login tracking covers local (forms)
authentication only and never fires for an external Entra identity -- `LocalLoginEnabled` is
`false` here and nobody signs in any other way -- or that the site needs a restart the setting
change never triggered. Neither has been checked, so neither should be repeated as fact.

**What this means in practice:**

- **A null reading is not evidence of anything, on any date.** Not before 2026-09-15 when the
  setting was off, and not after it, when it is on and still records nothing. A working
  sign-in and a broken one read exactly alike, and always have.
- **The only reliable verification is a person signing in and telling you.** That is not a
  weaker check than a query; on this site it is the only one. The 2026-09-15 investigation and
  this one were both settled by a human report, never by a column.
- The four requirements below are still worth querying. They tell you whether provisioning is
  *complete*, which is a different question from whether a sign-in *works* -- gate 0 sits above
  them and is invisible to all of it.

All four requirements, everyone at once:

```
<fetch><entity name="contact">
  <attribute name="emailaddress1"/>
  <attribute name="adx_identity_username"/>
  <attribute name="adx_identity_securitystamp"/>
  <attribute name="adx_identity_logonenabled"/>
  <attribute name="adx_identity_lastsuccessfullogin"/>
  <link-entity name="adx_externalidentity" from="adx_contactid" to="contactid"
               alias="ei" link-type="outer"><attribute name="adx_username"/></link-entity>
</entity></fetch>
```

`identities` reports the same ground joined against Entra and the web roles, and names the
contacts nothing can reach.

---

## What does not work

- **`AllowContactMappingWithEmail`.** Tried in TEST on 2026-09-14 to bind seeded contacts by
  email claim; it changed nothing and is set `false`. This site uses the built-in Entra
  provider, which the setting is documented not to apply to. Whether it was genuinely
  inapplicable or only ever read from a stale cache was never separated — but the four
  commands above need no setting at all.
- **Invitations.** `InvitationEnabled` is `false` in both environments as at 2026-09-15 and
  the path is untested here. It read `true` in `sitesetting.yml` until that date, so an
  upload would have switched it on unasked; the yml was reconciled down to the environments.
- **Turning registration off** to stop the `/Register` redirect. It does not stop it, it
  changes the wording. The redirect means the sign-in is landing on the registration path,
  not being refused at it.

---

## Prod

Prod has had none of this done. Contacts there will need all four requirements each, and the
first one needs the open-registration bootstrap because Prod has no binding to copy an issuer
from. **Gate 0 applies there too** and is not carried by any deployment -- if the Prod site is
private, its Manage access list has to be populated by hand before a single contact matters.
The settings table above is carried by `sitesetting.yml`, so an upload covers the
settings — it does not cover a single contact.
