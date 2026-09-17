# AD-143 deployed to DEV and TEST: regrade and sign-off get a real gate

**2026-09-16.** The plug-in assembly carrying AD-143 was pushed to both environments.

## What it changes

`RegradeCasePlugin` and `SignOffRemediationPlugin` now call
`PermissionHelpers.EnsureAppPermission` for `command.regrade` and `command.signoff` at Edit.

Both classes previously documented their authorization as "enforced by Dataverse: a caller
without the write-`al_outcome` / create-`al_signoff` privilege is refused by the platform (the
T&C Manager team is granted that privilege in the security configuration)". No team was ever
granted either selectively — `GrantSecurity` puts both tables in `writeForEveryone`, so both
application roles hold create and write at Global depth and the privilege refused nobody. Any
app user could regrade an outcome or sign off a remediation by calling the API directly, while
the client offered those buttons only to a T&C Supervisor. AD-031 gives both to the supervisor
precisely so the person who did the work is not the person who overturns or attests to it.

This is a tightening, so the risk it carries is the opposite of a feature's: not that it fails,
but that it refuses someone it should not.

## Lock-out check, before the build

The same class of failure as AD-142 earlier the same day — a new gate reading configuration that
was never put in place. Checked first, not after:

| Rule | DEV | TEST |
|---|---|---|
| `command.regrade` | AL Portal - T&C Supervisor, Edit, Active | T&C Supervisor Edit + Administrators Manage, both Active |
| `command.signoff` | AL Portal - T&C Supervisor, Edit, Active | T&C Supervisor Edit + Administrators Manage, both Active |

Every TEST app-role holder keeps access: Adam and Zoe hold both `AL Portal - T&C Supervisor` and
`Administrators`; Gill holds `Administrators`. DEV is unaffected — no DEV human holds an
application role, so every one of them short-circuits on the break-glass System Administrator
probe.

No plug-in executes `al_RegradeCase` or `al_SignOffRemediation` internally, so the portal path
is not pulled through the new gate. Power Pages reaches these through `RegradeRequestPlugin` and
`SignoffRequestPlugin`, which check the contact's web roles because a portal write arrives as the
site's application user (AD-053).

## What was deployed

Built `-c Release` first, because `pushassembly` uploads `bin/Release` without building it and
will happily ship a stale DLL. The byte count is the check that the right one went up.

| Environment | Result |
|---|---|
| Env_AQ_Dev | 226304 bytes, modified 2026-09-16 17:48:43Z, version 1.0.0.0 |
| Env_AQ_Test | 226304 bytes, modified 2026-09-16 17:49:09Z, version 1.0.0.0 |

226304 bytes matches the local Release build exactly in both.

Verified afterwards in both: **51 steps, all Enabled.** An assembly push does not carry step XML,
so nothing could be switched off the way the 2026-09-02 solution import did — confirmed rather
than assumed.

864 unit tests pass against the Release build that was shipped.

## Not deployed

- `OutcomeTesting.Registration/Program.cs` — the local tool, not an environment artefact.
- `src/Roles/*.xml` — the two role grants were applied directly by `grantsecurity` earlier today;
  the XML carries them for the next solution import.

## Caveat worth recording

The code deployed here is **uncommitted** work on `feat/checklist-administration`. Both
environments are now running an assembly that has no commit to point at. Committing the branch is
what makes this deploy reproducible.
