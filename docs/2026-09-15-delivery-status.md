# Delivery status — 2026-09-15

Supersedes `docs/2026-09-14-delivery-status.md` as the current status. The register of what is
left remains `docs/2026-09-04-outstanding-work.md`, read with the "Still open" and "Not done
here" sections of the deployment notes since.

Every environment claim below was verified by query after the fact, against **`Env_AQ_Dev` and
`Env_AQ_Test`**. Where something did **not** land, it says so.

**The day in one line: nobody who had never signed in could sign in, and the reason was not
the one anybody was looking at — the six unbound contacts were real and are now fixed, but the
thing actually keeping people out is site visibility, which is invisible to every query in the
runbook and which every administrator bypasses without noticing.**

No code changed today. One deployment note:
`docs/deployment/2026-09-15-site-visibility-and-login-tracking.md`.

---

## 1. What changed

| Where | What |
|---|---|
| `Env_AQ_Dev` | Six contacts bound, stamped and enabled; `LoginTrackingEnabled` → `true`; `AllowContactMappingWithEmail` created as `false` |
| `Env_AQ_Test` | `LoginTrackingEnabled` → `true`. Contacts were already complete |
| `sitesetting.yml` | `ProfileRedirectEnabled` and `InvitationEnabled` reconciled down to the environments; `AllowContactMappingWithEmail` id corrected to DEV's; three descriptions written |
| `docs/reference/portal-access-runbook.md` | **Gate 0** added, the administrator-bypass trap named, and the verification section corrected |

## 2. Gate 0, and why it hid

A private Power Pages site refuses everyone not on its Manage access list, before any
Power Pages authentication happens. Zoe Ramwell passes all four identity requirements in TEST
and holds seven web roles, and still gets `/private-mode-access-denied`.

It hid because **System Administrator bypasses it**. The account she was compared against —
Simunye Radingwana — holds that role and so was never subject to the gate at all. The runbook
advised comparing a working contact against a broken one and letting the differing column name
itself; here the difference was not a contact column, and that advice pointed away from the
answer. Both the gate and the trap are now written down.

Nothing in Dataverse records site visibility. No `pac` command and no verb of the registration
tool can read or write it. It is maker-portal only, which is why it is still open.

## 3. The verification step had never worked

`Authentication/LoginTrackingEnabled` was `false` in both environments, so
`adx_identity_lastsuccessfullogin` — the column the runbook tells you to query instead of
asking what the screen said — was null for **every contact in both environments**, including
the service accounts and Simunye, who sign in constantly.

A working sign-in and a broken one read identically. Set `true` in both. **Any null reading of
that column dated before today is not evidence of anything.**

## 4. The requested auto-create was not built

Asked for: a first-time user with no contact is added automatically with no role. Declined on
the evidence, and confirmed by the project owner:

- It helps **nobody** currently locked out — all six already had contacts, and open
  registration refuses an email that already has one with "The email ... is already taken".
- It reverses the 2026-08-31 security closure, already re-reverted once on 2026-09-08.
- "No role" is not "no access": `Authenticated Users` is implicit and carries 10 of 16 table
  permissions, including Global read on `al_outcomecase`, `al_response`, `al_reviewinstance`.

`OpenRegistrationEnabled` remains `false` in both environments.

## 5. Still open

| What | Why it is still open |
|---|---|
| ~~Manage access for the six, in TEST~~ | **Closed** by the project owner, 2026-09-15 |
| ~~Duplicate `Webapi/error/innererror` row in TEST~~ | **Closed.** A `deletesitesetting` verb was written — deletes by id, guards the last row of a name, verifies by re-query |
| `emailaddress1confirmed` `No` for all six | Probably common cause, not a fifth gate. If they still fail once gate 0 opens, `enableportallogin --confirmemail` first |
| `pac pages upload` to TEST | Would duplicate `AllowContactMappingWithEmail` there; TEST has been served by `setsitesetting` throughout |
| Prod | Has had none of this. Gate 0 applies there too and no deployment carries it |

## 6. Settings, after

`sitesetting.yml`, DEV and TEST agree on all **69** settings with **zero** drift. All 68
settings common to both environments share identical ids; the exception and its hazard are in
§6 of the deployment note.
