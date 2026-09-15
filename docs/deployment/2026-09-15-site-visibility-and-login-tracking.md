# Gate 0 found, and the verification that could never have worked — DEV and TEST, 2026-09-15

Targets: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`) and `Env_AQ_Test`
(`https://org37995f36.crm11.dynamics.com/`), as `svc.automate.aq@ascotlloyd.co.uk`.

Reported by the project owner, twice. First: *"Check the site setting and confirm if any of
the people that have never signed in before will have any issues signing in."* Then, after the
contact work was done: *"I'm getting this error when I try to sign in as Zoe Ramwell"* —
`/private-mode-access-denied`.

The second report is the one that mattered. It was not a contact fault, and nothing in the
first investigation could have found it.

---

## 1. The question as asked, answered

`Authentication/Registration/OpenRegistrationEnabled` is `false` in both environments and in
`sitesetting.yml`. So an unknown person — no contact — is refused at the registration gate.

But that was not the population. All six people who had never signed in **already had
contacts**, seeded rather than created by Power Pages, so they were failing the four identity
requirements instead. Open registration would not have helped them; it only ever *creates*
contacts, and for an email that already has one it refuses with "The email ... is already
taken" and loops forever.

The requested behaviour — *"when a user accesses the site for the first time and they have not
been added to contact, it should add them automatically with no role assigned"* — was
therefore **not implemented**, by owner decision, for three reasons recorded at the time:

1. It fixes nobody who currently cannot sign in.
2. It reverses the 2026-08-31 security closure, re-reverted on 2026-09-08. Any tenant account
   would mint a contact.
3. "No role assigned" is not "no access" on this site. `Authenticated Users` is the implicit
   role (`adx_authenticatedusersrole: true`) and carries **10 of 16** table permissions,
   including Global read on `al_outcomecase`, `al_response` and `al_reviewinstance`.

## 2. Six contacts bound in DEV

`bindidentity`, `setsecuritystamp`, `enableportallogin` for suresh.gautam, zoe.ramwell,
ruthmaxwell, Gill.Philpott, clare.hook, angela.houghton. Entra object ids were read from TEST,
which holds `systemuser` rows for all six; DEV holds none, and the ids are tenant-wide.

All four requirements verified by reading the environment back, not by trusting the tool's own
output. Issuer is `sts.windows.net/4abde4fc-…` on every row — not the `login.windows.net`
variant this project has been bitten by. `identities` now reports **zero** unbound contacts in
DEV. TEST was already complete and needed nothing.

One binding hung for 9.5 minutes and succeeded in seconds on retry. Transient; worth knowing
the tool can stall mid-run.

## 3. Gate 0 — site visibility

**Both sites are Private, and that check runs before Power Pages authentication.** Zoe's TEST
contact passes all four requirements and holds seven web roles; she still cannot reach the
site, because she is not on the Manage access list.

It is not in Dataverse. No `pac` command, no verb of the registration tool and no FetchXML in
the runbook can see it. It is granted in the design studio under **Set up → Site visibility →
Manage access**, by hand.

**Administrators bypass it entirely.** Simunye Radingwana reaches the site without appearing
in Manage access because he holds Dataverse **System Administrator**; Zoe holds
`Outcome Testing App Admin` and `Basic User`, which sound administrative and grant no bypass.
That is why the fault looked like a contact problem — the working comparison subject was not
running the same gauntlet.

This is recorded as **gate 0** in `docs/reference/portal-access-runbook.md`, whose first
version documented four requirements and would have sent the next person at the bindings too.

**Outstanding, and not closable from here:** the six people still need Manage access in TEST.
Recommended as one Entra security group rather than six individual grants.

## 4. `LoginTrackingEnabled` was `false` in both environments

The runbook's entire verification step is *"query `adx_identity_lastsuccessfullogin` rather
than asking what the screen said"*. That column only populates when
`Authentication/LoginTrackingEnabled` is `true`, and it was **`false` in DEV and TEST**.

So the column read null for every contact in both environments — including the service
accounts and Simunye, who sign in constantly. A working sign-in and a broken one read
identically, and the natural conclusion from a null is the wrong one.

Set `true` in both, and read back `true` from both.

**Any null reading of that column dated before 2026-09-15 is not evidence of anything.**

## 5. Settings deployed

| Setting | DEV | TEST | Note |
|---|---|---|---|
| `Authentication/LoginTrackingEnabled` | `False` → `true` | `False` → `true` | Section 4 |
| `Authentication/OpenIdConnect/AzureAD/AllowContactMappingWithEmail` | *(absent)* → `false` | already `false` | Row did not exist in DEV |

Two further drifts were found and **left at their environment values** by owner decision, with
`sitesetting.yml` reconciled down to match so a future upload cannot silently revert them:

| Setting | yml was | Both envs | Resolution |
|---|---|---|---|
| `Registration/ProfileRedirectEnabled` | `true` | `false` since 2026-09-09 | yml → `false` |
| `Registration/InvitationEnabled` | `true` | `false` since 2026-09-15 07:23Z | yml → `false` |

`InvitationEnabled` read `true` early in the session and `false` an hour later, from two
independent tools, with `mspp_modifiedon` moving to 2026-09-15T07:23:52Z under the service
account. The two readings were never reconciled. The live value was taken as authoritative;
the change is recorded here because it was not made by this session.

After the deploy, `sitesetting.yml`, DEV and TEST agree on **all 69 settings, zero drift**.

## 6. Two id hazards

**Created by this deploy.** `AllowContactMappingWithEmail` did not exist in DEV, and
`setsitesetting --create` mints a fresh id. The yml had been carrying TEST's id
(`a4ca0596-…-002248c654cd`) since the setting was tried there on 2026-09-14, even though this
repo is a DEV round-trip. The yml now carries the DEV id
(`51468c4f-…-e4fade069307`) and both ids are recorded in its description. **A
`pac pages upload` aimed at TEST would create a second row of this name there.**

**Pre-existing, and still open.** `Webapi/error/innererror` already has **two rows in TEST** —
`ecc746ed-…-002248c654cd` and `8dea7fe4-…-e4fade069307`, both `true`, both created 2026-09-14.
The yml carries the second. Behaviour is consistent today because the values agree; they will
not always. Deleting the duplicate needs the Portal Management app — no verb of the
registration tool deletes a site setting, which is why it is still there.

All 68 settings common to both environments otherwise share identical ids.

## 7. What was not done

- **Manage access grants.** Maker portal only. This is what currently blocks all six.
- **The duplicate `Webapi/error/innererror` row in TEST.** Needs a manual delete.
- **`adx_identity_emailaddress1confirmed`** is `No` for all six and `Yes` for the three
  accounts known to sign in. With `EmailConfirmationEnabled` `true` that looks like a fifth
  gate, but the likelier reading is common cause — those three were created by Power Pages
  registration, which sets the gates and confirms the email in one act. Left alone. If the six
  fail after gate 0 is opened, `enableportallogin --confirmemail` is the first thing to try.
- **Prod.** Untouched, and has had none of this done.
