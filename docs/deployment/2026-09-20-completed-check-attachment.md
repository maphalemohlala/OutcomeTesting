# 2026-09-20 — the completed check attachment, and the review page's empty state

Covers Change 2's content clause, Change 7's PDF clause (AD-165, AD-166) and one portal
defect found by the extended template gate (AD-167).

## What went to DEV

| Component | How | Verified |
|---|---|---|
| `OutcomeTesting.Plugins` assembly | `pushassembly` | 282,112 bytes, matching the `-c Release` build made immediately before. The byte count is checked because the verb uploads `bin/Release` without building it |
| `OT Review Detail` web template | `pushwebtemplate` against component `a1000000-…-00000000001b` | Re-downloaded afterwards and the guard confirmed present |

**Nothing else changed in the environment.** No schema, no permissions, no settings — the
document is drawn from columns that already existed, and the two `al_notification`
attachment columns went in with AD-164.

## Why the portal template was pushed one record at a time

`pac powerpages upload` sends the whole tree. Diffing the tree against a fresh DEV download
first — which is the standing rule — showed the two are **content-identical except for a
UTF-8 BOM on the local files**, but that the `table-permissions` folder differs in *file
naming* on eleven records: DEV's download names them from the display name
(`Case-Assignment---claim-from-the-queue.tablepermission.yml`) where the local tree uses
shorter names (`Case-Assignment---claim.tablepermission.yml`).

A whole-tree upload with mismatched file names risks creating duplicate permission records
rather than updating the existing ones, and duplicate page permissions are audit finding 6 —
the thing `MaxLevel` turns into access that cannot be revoked from the Security screen. So
the single changed record was pushed directly instead.

**This is not resolved, only avoided.** The naming mismatch is still there and the next person
to reach for `pac powerpages upload` will meet it. Reconciling the two naming schemes is worth
doing before anyone needs a bulk portal deployment.

## The character count on the template push looks wrong and is not

`pushwebtemplate` reported `158350 -> 156122 chars` — a decrease of 2,228, on a change that
*added* nine lines. The arithmetic:

- DEV held the source with CRLF line endings: 2,800 lines, so 2,800 carriage returns.
- The push normalises to LF, removing all 2,800.
- The edit adds 572 characters.
- −2,800 + 572 = **−2,228.** Exactly the reported difference.

Recorded because a size *drop* after an addition is what a truncated upload looks like, and
the next person to see it should not have to re-derive this.

## What still needs doing in DEV, and fails quietly if skipped

Unchanged from the AD-161 and AD-162 notes — neither is affected by this work, and both
still mean a letter that never arrives rather than an error anybody sees:

1. **No contact matches the extract's para-planners.** The completed check is now worth
   sending, which makes this more visible rather than less: an unmatched para-planner means
   no recipient, so the letter is not sent at all and the attachment goes nowhere.
2. **No adviser is mapped to a T&C Manager** at `/admin/advisers`, so no sign-off notification
   is raised.

## Manual verification

`docs/audit-2026-09.md`, tests **T7b** and **T7c**. T7b step 4 was rewritten: it previously
told the tester to confirm the answers were *absent*, which would have certified the gap this
work closed.

One thing the automated checks genuinely cannot do is confirm the page *renders*. For the PDF
that gap is now closed — a sample is generated through `CompletedCheckPdf`, rasterised and
looked at, which is how the subheading size was found to be wrong (AD-166). For the portal
templates it is still open: the gate is static and nothing renders Liquid.

---

# 2026-09-20 (later) — recipients, and letters of your own (AD-168)

## What went to DEV

| Component | How | Verified |
|---|---|---|
| `al_notificationtemplate.al_event` | `addchoicecolumn` | 8 options, matching `NotificationOutbox.KnownEvents()` |
| `al_notificationtemplate.al_recipientkind` | `addchoicecolumn` | 5 options, matching `NotificationRecipients` |
| `al_notificationtemplate.al_recipientcontactid` | `addlookupcolumn` | lookup to `contact` |
| `OutcomeTesting.Plugins` assembly | `pushassembly` | 289,792 bytes, matching the `-c Release` build made immediately before |
| Code App | `pa app push` | data source regenerated first; the model diff was **exactly** the 28 lines for the three columns |

No plug-in step was registered: the guard already runs pre-operation on Create and Update of
`al_notificationtemplate`, and the new rules are in the same type.

## The registration tool hung twice, and it was not the verb

`addchoicecolumn` sat on `Connecting to <org>…` for 12 minutes backgrounded, then 10 more in
the foreground, printing nothing. It is not a slow metadata publish and it is not sign-in:
running the cheap `fetch` verb immediately afterwards connected and returned in seconds.

It is **two instances of the tool contending for the MSAL token cache**. Killing the stray
process and re-running the identical command succeeded in seconds. This extends the existing
one-process-at-a-time rule, and the symptom is worth recording because it looks exactly like a
slow write — which is what sent me reading `AddChoiceColumn` for a `Console.ReadLine` that was
never there.

**If the tool sits on `Connecting…`**: `Get-Process -Name OutcomeTesting.Registration`, stop
what is there, retry. Never background it.

## What is NOT verified in DEV

The logic has 1251 plug-in tests behind it, but **nothing has been exercised end to end in an
environment**, because there is no verb that creates a template row — the table is
administered from the Code App screen. The fan-out in particular has never queued a real
letter outside a test.

That is what **T7d** in `docs/audit-2026-09.md` is for, and step 4 is the one to actually do:
point a letter at a para-planner who matches no contact and confirm the letter still reaches
its original recipient. An override that resolves to nobody must not empty an address the
system had worked out correctly, and losing a letter silently is worse than ignoring a bad
setting.
