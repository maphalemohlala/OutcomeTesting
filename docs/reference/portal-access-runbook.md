# Giving someone access to the portal

Living reference. Written 2026-09-14 after Env_AQ_Test was provisioned from empty, where
each of the four requirements below was found the hard way, one failed sign-in at a time.

Entra is the only sign-in method on this site. `LocalLoginEnabled` is `false`, there is no
OpenIdConnect provider registration, and the built-in Entra provider is enabled through
`Authentication/Registration/AzureADLoginEnabled`.

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
register, they are **not** provisioned — go to the next section.

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
| "Registration has been disabled. Invalid sign-in attempt." | Registration is off and they are unbound, **or** `ExternalLoginEnabled` is off |
| Redirected to `/Register`, or "Register your external account" | Unbound, **or** bound with `logonenabled` false — these look identical |
| "The email ... is already taken" | Open registration is on and their contact already exists. Bind instead |
| Signs in, site is empty | All four fine; they hold no web role |

The second row is the trap. A correct binding with `logonenabled` false presents exactly like
no binding at all, so confirm gate 4 before concluding the binding is wrong.

**The best diagnostic is a working contact.** Read one that signs in against one that does
not, in the same environment, and let the differing column name itself.

---

## Settings that must hold

| Setting | Value | Why |
|---|---|---|
| `Registration/Enabled` | `true` | The gate an unbound sign-in reaches |
| `Registration/ExternalLoginEnabled` | `true` | **`false` refuses every Entra sign-in** |
| `Registration/OpenRegistrationEnabled` | `false` | `true` lets any tenant account mint a contact |
| `Registration/LocalLoginEnabled` | `false` | Entra is the only method |
| `Registration/LoginButtonAuthenticationType` | *(empty)* | A wrong issuer here has bitten this project twice |

`sitesetting.yml` is the source of truth and `setsitesetting` writes the same value the next
upload would. **This site serves settings from a cache**: a change can take effect well after
it reads back correctly, so a setting that appears to do nothing has not been disproved yet.

---

## Verifying without a screenshot

`Authentication/LoginTrackingEnabled` is `true`, so a successful sign-in stamps
`contact.adx_identity_lastsuccessfullogin`. Query that rather than asking what the screen
said. Subject to the caching caveat above — a null reading shortly after a settings change
is not proof of failure.

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
- **Invitations.** `InvitationEnabled` is `true` and the path is untested here.
- **Turning registration off** to stop the `/Register` redirect. It does not stop it, it
  changes the wording. The redirect means the sign-in is landing on the registration path,
  not being refused at it.

---

## Prod

Prod has had none of this done. Contacts there will need all four requirements each, and the
first one needs the open-registration bootstrap because Prod has no binding to copy an issuer
from. The settings table above is carried by `sitesetting.yml`, so an upload covers the
settings — it does not cover a single contact.
