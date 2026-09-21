# Clean refusals, and the end of the System Administrator short-circuit

**2026-09-21.** Three changes, deployed to **DEV only**. Two are about what a refused caller
is told; one is a deliberate narrowing of what a Dataverse System Administrator may do.
Promotion to TEST and PROD is a separate decision.

## 1. A refusal that crossed a service call was reported as a crash

Signing off an already-approved remediation answered:

```
UNEXPECTED: OutcomeTesting.Plugins.SignoffRequestPlugin could not complete.
OrganizationServiceFault: PRECONDITION: This remediation action has already been
approved. Reopening an approved sign-off is a privileged correction (AD-031).
```

The rule was enforced perfectly. The reporting was wrong three ways.

`PluginBase` rethrows an `InvalidPluginExecutionException` untouched, which is what keeps a
deliberate refusal readable. But **one command calling another never receives that
exception**: it crosses the service boundary as `FaultException<OrganizationServiceFault>`,
lands in the next catch down, and gets re-labelled as an unexpected failure. So:

- the message no longer **starts** with its prefix, and every client that branches on
  `PRECONDITION:` / `CONFLICT:` / `UNAUTHORIZED:` is reading the wrong thing;
- a rule working exactly as designed reads to the user as a broken product;
- the plug-in class name is handed to whoever asked, which NFR-OBS-01 exists to prevent.

`CommandHelpers.RefusalWithin` now finds the first known prefix in a fault message and
returns the message from there. Searching rather than matching the start is deliberate: it
collapses nesting of any depth, so two commands deep yields the original sentence whatever
wrapped it. The full text still reaches the trace log and `InnerException`.

## 2. A platform privilege denial leaked internals

APP-003 captured what the platform actually says, delivered straight to a browser:

```
UNEXPECTED: OutcomeTesting.Plugins.ImportCasesPlugin could not complete.
OrganizationServiceFault: Error occoured : SecLib::CheckPrivilege failed.
User: 1fae3cf3-…, PrivilegeName: prvReadEntity, PrivilegeId: a3311f47-…
```

A class name, two GUIDs, a privilege name, a platform stack phrase, and `occoured`
misspelled by the platform. Nobody shown that can act on it.

It now reads:

> **UNAUTHORIZED:** Your Dataverse security role does not allow this action. This is a
> platform privilege rather than an Outcome Testing application role, so the two are worth
> checking separately: ask an administrator to look at the security roles on your user
> account. The platform trace log records which privilege was missing.

The wording separates the two role systems on purpose. APP-003 was an account holding the
Outcome Testing **application** role but not the Dataverse **Basic User** role, and the
`app-roles-need-basic-user` note records three instances in one week. Sending that person to
check their application role wastes the one place they would have looked.

**Nothing is swallowed.** It is still thrown, the transaction still aborts, and the raw fault
is still on the trace and in `InnerException`. AD-109 forbids *absorbing* a fault and
carrying on; this re-words one and rethrows it.

A fault that is genuinely unexpected still says `UNEXPECTED:` and still names the plug-in —
that branch is untouched, and a test holds it there, because trading one silent failure for
another would be no improvement.

## 3. A System Administrator is now held to the application rules

**Project owner's direction, 2026-09-21:** a system administrator should only perform actions
they are allowed to, like every other user in the environment.

`PermissionHelpers.EnsureAppPermission` began with a break-glass: an administrator returned
on the gate's first line with no application configuration at all. That is gone.

Why it was safe to remove:

- **Nobody is stranded.** A System Administrator holds every table privilege in the
  environment, so they can still repair a bad rule by writing `al_userrolemapping` and
  `al_pagepermission` **directly**. What they lose is the ability to do it *through the
  command* — which means the repair is now a platform write on a record, rather than a
  silent pass through an application gate.
- **The bootstrap is what actually protects a fresh environment**, and it is unchanged: it
  fires on an empty mapping table. That is the case the break-glass was justified by and
  never served, because an empty table was already handled two lines further down.
- **It removes an inconsistency.** `al_CompleteRemediation` already refused an administrator
  outright — *"Only the adviser who owns this remediation action can complete it"* — so the
  same account was governed by one rule on one command and a different rule on 27 others.

### Consequence for AD-142

The short-circuit was the **only** reader of the platform `role` and `systemuserroles`
tables. The gate no longer touches either, so `prvReadRole` — granted to both shipped roles
on 2026-09-16 specifically to make that probe answer — is now unnecessary.

It is **left granted**. Withdrawing a privilege changes the managed solution and affects
TEST and PROD, which is a separate decision from this one. It is unnecessary rather than
harmful. `AppPermissionGateTests.TheGateNoLongerReadsThePlatformRoleTable` pins the read as
gone, so re-introducing it has to be deliberate.

### Residual risk, stated plainly

If every mapping exists but nobody holds `permission.manage`, no one can fix it *through
Security configuration in the app*. The repair is a direct Dataverse write, which any System
Administrator can make. Accepted knowingly rather than discovered later.

## Verification

- **1,385 plug-in tests pass** (was 1,376; nine cases added).
- **Proved by reverting.** With the old behaviour restored, all ten new cases fail; the two
  regression guards — a genuinely unexpected fault still naming the plug-in, and the gate
  still reading the tables it is checked against — correctly stay green.
- **799 app tests pass.** Nothing client-side compensated for the old message shape in a way
  that breaks. The portal's `friendlyError` already tested the refusal prefixes *before*
  `UNEXPECTED:`, which is why users saw clean text while the wire did not.
- **Verified live in DEV** after `pushassembly` (296,448 bytes, sha256 `a43f3886…`). The
  PRT-093 replay — the real PATCH the page makes, with a valid verification token — now
  returns exactly:
  `PRECONDITION: This remediation action has already been approved. Reopening an approved
  sign-off is a privileged correction (AD-031).`
- **The tooling identity was checked for lockout**, not assumed: `svc.automate.aq` holds
  active mappings including `Administrators`, and both `al_GetMyRoles` and the gated
  `al_GetRoleHolders` still succeed for it — now through the application role model rather
  than the short-circuit.

**Not yet verified live:** the privilege-denied wording. Reproducing it needs an
under-privileged account signing in to the Code App, as APP-003 did. The unit tests drive
the exact raw fault string captured from that live run, and both platform phrasings.
