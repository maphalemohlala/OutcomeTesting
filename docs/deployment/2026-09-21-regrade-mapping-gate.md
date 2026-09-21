# Recording the final outcome is the case's own T&C Manager

**2026-09-21. AD-202.** Closes the asymmetry AD-201 left open, on the project owner's
instruction to fix it.

## The hole

AD-198 put the mapping gate on the sign-off and left the regrade on the role alone. So:

> Anyone holding `AL Portal - T&C Supervisor` could record the regraded outcome on **any**
> case — including cases they were the adviser on, and including the cases whose sign-off the
> mapping had just refused them.

A regrade sets the case's final outcome. That is as consequential as the attestation, and it
is the same shape as the defect that started the day — a supervisor acting on a case they
supervise nothing of — one command over.

## It was found by looking, not by reasoning

Worth recording, because the reasoning had already been done and had stopped one step short.
AD-201 split the page's gate in two and argued the split was correct, which it was: the page
must mirror its command, and gating the regrade panel on the mapping would have hidden a
control the server accepted.

What settled it was the portal. Verifying AD-201 signed in as the project owner:

| Case | Mapped manager | Sign-off form | Regrade form |
|---|---|---|---|
| 910000005 | the service account | correctly withheld | **rendered** |
| 900000004 | the service account | correctly withheld | **rendered** |

The reader was the **adviser** on both. The argument for the split was sound and the thing it
was protecting turned out to be the defect.

## The fix

The rule moved into `SupervisorMapping.EnsureManagesCase`, called by **both** commands:

```csharp
SupervisorMapping.EnsureManagesCase(service, contactId, caseRef, "Signing a case off");
SupervisorMapping.EnsureManagesCase(service, contactId, caseRef, "Recording the final outcome");
```

One method rather than two copies, because it is one rule — and the copy that did not exist
is precisely what leaked. The act is a parameter so a reader looking at one of two controls is
told which one is refused, rather than being given a sentence about signing off when they
pressed *Record the final outcome*.

`RegradeRequestPlugin` reaches the case through the outcome the payload names, and an outcome
attached to no case says so rather than failing somewhere less legible.

The page collapsed back to one gate. `has_signoff_role` survives **only** to choose the wording
between "you do not hold the role" and "you hold it but not over this adviser"; a test pins
that it never appears in a panel condition again, and another pins that `can_regrade` is gone.

## Scope, deliberately narrow

**Only the two portal paths.** `RegradeCasePlugin.Regrade` and the `al_RegradeCase` Custom API
behind the Code App are untouched: a back-office user is authorised by their Dataverse security
role, which is a different boundary and not the one that was leaking. `RegradeCasePluginTests`
therefore needed no change, which is the check that the blast radius is what it claims.

## Cost accepted

A case whose adviser has no mapping can now be regraded by nobody — the same consequence the
sign-off already carried since AD-198, and another reason `al_advisermapping` is operational
data rather than notification convenience (F58).

## Verified

Six new plug-in tests, **five failing before the change** with "No exception was thrown" —
the defect stated as a test — and passing after. 1,499 plug-in tests, 938 vitest across 69
files, `tsc -b` clean.

Live on DEV, signed in as the project owner, both directions:

- **910000005** (mapped manager is the service account): the only interactive control left on
  the whole page is the navigation account button. No regrade form, no sign-off form, and the
  explanatory message. The hole is shut.
- **900000005** (mapped manager is the reader): the full sign-off form still renders — decision,
  final outcome, recheck required, changes advice. Nothing was over-hidden.

No Liquid error on either, console clean. Assembly 305,664 bytes; template 138,780 → 139,101.

## Not deployed anywhere else

DEV only.
