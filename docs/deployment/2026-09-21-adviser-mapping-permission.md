# The sign-off panel now asks whose adviser the case is

**2026-09-21. AD-201.** The project owner, after finding that a service account holding the
T&C Supervisor role could sign off a case whose adviser it supervises nothing of:

> Supervisors should only see the sign off controls on cases advisers linked to the handled

The **server** already refused this — `SignoffRequestPlugin.EnsureMappedToCase`, AD-198, shipped
earlier the same day. The **page** did not, so a supervisor was still offered a form that could
only end in a refusal. This closes that gap.

## What was blocking it

The gate needs to know whether the reader is the T&C Manager `al_advisermapping` names for the
case's adviser. The site had **no table permission for `al_advisermapping` at all**, so a Liquid
fetch would have come back empty for everybody and hidden the panel from the right person as
well as the wrong one.

Granting one was left to the owner because it is a privacy decision, not a technical one: who
supervises whom is organisational structure, and a Global-scope permission would have put the
whole table in front of every role bound to it.

## What was granted, and why it is narrower than it first looked

**Contact scope, not Global.** `al_advisermapping.al_tcmanagerid` is a lookup to `contact`, so
Power Pages can narrow every read to the rows where the reader *is* the manager:

```
Adviser Mapping - advisers I supervise   powerpagecomponent a1000000-…-00000000007a
  entitylogicalname     al_advisermapping
  scope                 756150001            (Contact)
  contactrelationship   al_contact_al_advisermapping_tcmanager
  read                  true     (create / write / delete / append / appendto all false)
  webrole               AL Portal - T&C Supervisor    — one role
```

That answers the question the gate asks **without exposing the structure at all**. A supervisor
reads the advisers mapped to them and nothing else; no supervisor learns anyone else's. The
privacy objection that held this up does not survive contact scope, which is why it was worth
checking the relationship metadata before proposing Global:

```
EntityDefinitions(LogicalName='al_advisermapping')/ManyToOneRelationships
→ al_contact_al_advisermapping_tcmanager   referencing al_tcmanagerid → contact
```

Three further narrowings, each deliberate:

- **One web role**, not Authenticated Users. Nothing but the sign-off gate reads this table.
- **Read only.** The portal never writes a mapping; the Code App's admin page does.
- **No `Webapi/al_advisermapping/*` site settings.** The gate is server-rendered Liquid, so the
  table stays off the browser-reachable API entirely. Adding the settings would have made it
  queryable from the console for no gain.

The web roles were written **inside the `powerpagecomponent.content` JSON**, which is where the
enhanced data model reads them — AD-195's lesson, where binding them only in
`mspp_entitypermission_webrole` left List Option returning 403 while looking correctly configured.

## The page

`OT Remediation`, one fetch and two gates.

```liquid
{% assign ot_is_case_tcmanager = false %}
{% assign ot_case_adviser_email = c.al_adviseremail | default: '' %}
{% if c and ot_case_adviser_email != '' and user %}
  ... al_advisermapping where al_adviseremail = the case's AND al_tcmanagerid = user.id ...
{% endif %}
```

**Filtered on both conditions, though contact scope already narrows the rows.** The scope is the
privacy control; the filter is the answer. Written the other way — relying on the scope to do the
matching — the gate would be correct only for as long as an assumption about the runtime holds,
and the assumption that this data model stores permissions where they appear to be stored is
exactly the one AD-195 caught being wrong. As written, the gate is right whether or not a Liquid
fetch enforces table permissions, and nothing leaks under either behaviour.

The case fetch gained `al_adviseremail`, read for the gate and shown nowhere.

## Two gates, because there are two rules

This is the part worth reading twice.

| Panel | Command | Server checks | Page gate |
|---|---|---|---|
| Sign-off | `al_RequestSignoff` | `EnsureSupervisorRole` **and** `EnsureMappedToCase` | `can_signoff` = role **and** mapping |
| Regraded outcome | `al_RegradeCase` | `EnsureSupervisorRole` **only** | `can_regrade` = role only |

`RegradeRequestPlugin` carries **no mapping gate**. Folding the regrade panel into `can_signoff`
would therefore have hidden a control the server would have accepted — the same defect as the
one being fixed, pointing the other way. So the two panels now carry two variables.

**This asymmetry is worth a decision.** A regrade sets the case's final outcome, which is as
consequential as the attestation, and the argument for restricting a sign-off to the case's own
supervisor applies to it unchanged. Extending `EnsureMappedToCase` to `RegradeRequestPlugin` is a
small change and a deliberate tightening; it is **not** made here because it restricts a command
the owner did not name. Until it is made, the page tells the truth about the system as built.

A supervisor on somebody else's adviser's case is told so, and **not** told who the manager is —
contact scope means the page could not read the name, and naming them would leak the structure
the scope exists to keep.

## Verified

Tests: **11 failing before the template change, 13 passing after** (`portalSupervisorPanels.test.ts`),
937 vitest across 69 files, `tsc -b` clean.

One of those tests earns its place beyond the feature: the multi-line expectations were matching
against `\n` while the working tree carries CRLF under git's `autocrlf`, so they were reporting on
how the file had been checked out rather than on what the template said. Both templates are now
normalised before anything is matched.

The permission was created in DEV and read back — `204` then `200`, `statecode 0`, content JSON
stored exactly as sent. The template was pushed (134,050 → 138,780 chars).

The deployed template was then read back out of Dataverse and compared with the file on disk:
**identical**, 138,780 characters both sides once the working tree's CRLF is normalised, with
all seven markers of this change present in what the server serves and the old single gate gone.

### Verified live on the DEV portal

Signed in as the project owner, who holds `AL Portal - T&C Supervisor`. All three states were
reached against real data, and the second and third only because the owner built them.

| Case | Adviser | Mapped manager | Pending | What rendered |
|---|---|---|---|---|
| 900000005 | service account | **the owner** | 5 | **Sign-off form**, `data-action-ids` carrying all five |
| 910000005 | the owner | service account | 1 | **No form**; the "not for this one" message |
| 900000004 | the owner | service account | 0 | Neither, correctly — nothing was pending |

The message, verbatim from the page:

> Remedial actions on this case are waiting to be signed off. Signing a case off is the
> AL Portal - T&C Supervisor mapped to its adviser, which your account is not for this one.

On 910000005 there is **no sign-off control of any kind** — no combobox, no Approved/Rejected,
nothing. On 900000005 the same template renders the full form. One page, one signed-in user, two
cases, opposite answers: that is the gate working rather than the gate being argued for.

**No Liquid error on any case page**, console clean throughout, including the two where the
mapping fetch runs and returns empty.

The sign-off form appearing at all is what proves the table permission: `can_signoff` requires
`ot_is_case_tcmanager`, which requires the `al_advisermapping` fetch to have returned a row, and
before this change the site had no permission on that table whatsoever.

### And the end-to-end path, run by the owner

A mapping was created making the **service account** the T&C Manager for the owner's own adviser
email, two actions on 900000004 were completed, and the service account signed them off:

```
19:26:19  APPROVED  by Service Account
19:26:21  APPROVED  by Service Account
```

900000004 closed, the sign-off table separating *Supervisor sign-off: Approved, Service Account*
from *Adviser sign-off: Simunye Radingwana*. That is AD-198 and this note working together on the
permitted path: the service account could sign off **because it had been linked**, which is the
whole rule. The defect it came from — a service account signing off a case it supervises nothing
of — is closed by requiring the link rather than by naming the account.

## The regrade asymmetry is real, and was observed

The section above leaves extending `EnsureMappedToCase` to `RegradeRequestPlugin` open. The live
check turned that from a theoretical note into something seen on screen.

On **both** 910000005 and 900000004 — cases whose mapped manager is the service account, not the
reader — the **Regraded outcome form rendered**, with its *Reason (required)* box and its
**Record the final outcome** button, while the sign-off form was correctly refused.

The reader on those pages is the case's own **adviser**. So, stated plainly:

> Anyone holding `AL Portal - T&C Supervisor` can record the regraded outcome on any case,
> including cases they are the adviser on, and including cases whose sign-off they are refused.

This is the same shape as the defect that started the day, one command over. The page is not at
fault — it is mirroring `RegradeRequestPlugin`, which checks the role and nothing else, and hiding
the panel would have hidden a control the server accepts. The fix belongs in the command.

**Recommended**: extend `EnsureMappedToCase` to `RegradeRequestPlugin` and collapse `can_regrade`
back into `can_signoff`. The cost is that a case whose adviser has no mapping could then not be
regraded by anyone — the same consequence sign-off already carries, and another reason
`al_advisermapping` is now operational data.

**Not done here**, because it restricts a command the owner has not asked to restrict.

### What the DEV data makes this look like

Six cases carry remedial actions. The owner's own portal contact is the mapped T&C Manager for
the service-account adviser, which gives a clean A/B with no data changes:

| Case | Adviser | Mapped manager | Sign-off panel for the owner |
|---|---|---|---|
| 900000001 | service account | **the owner** | **shows** |
| 900000005 | service account | **the owner** | **shows** |
| 900000004 | the owner | none | hidden, with the reason |
| 910000005 | the owner | none | hidden, with the reason |
| 910000006 | the owner | none | hidden, with the reason |
| 910000004 | an address on no mapping | none | hidden, with the reason |

**Four of the six have no mapped manager, so nobody may sign them off.** That is not new and this
change did not cause it — the server has refused those four since AD-198 this morning. What
changes is that the refusal is now visible before the form is filled in rather than after.

It does mean the panel will appear to have *vanished* from four of six DEV cases. The fix is to
populate `al_advisermapping`, which AD-198 already moved from notification convenience to
**operational data**: an adviser with no mapping has nobody who may sign their cases off. DEV
holds 2 rows and TEST holds 0, so TEST cannot complete a remediation at all (F58).

## Not deployed anywhere else

DEV only. TEST needs this component created the same way when the solution goes over, alongside
the items already listed in the 1.0.5.0 note.
