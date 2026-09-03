# Deployment — PP-15 drain and PP-17 portal drill-down, 2026-09-04

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. Source commit `52fe14c`.

Every line below was verified by FetchXML query against the environment after the fact, not
inferred from a command exiting zero. The portal upload in particular does not run to
completion on this site, so its result **has** to be checked rather than assumed.

---

## 1. What went in

| Step | Command | Result |
|---|---|---|
| Assembly | `pac plugin push --type Assembly --pluginId 7b51d0d1-…` | "updated successfully" |
| Plug-in type | `registertype … NotificationDrainPlugin` | created `a5b05784-e7a7-f111-aaac-002248c8d3e2` |
| Commands | `registerall` | 22 registered (was 21) — `al_DrainNotifications` is the new one |
| Solution | `pac solution add-solution-component … CustomAPI` | `al_DrainNotifications` added to `OutcomeTesting` |
| Portal | `pac pages upload -mv Enhanced` | **partially failed, as expected**; all four intended changes landed |

Verified afterwards by query:

- `al_DrainNotifications` exists (`3aefb8ab-e7a7-f111-aaac-002248c8d3e2`) and is a member of
  **both** `Default` and `OutcomeTesting`.
- Both new plug-in types exist: `NotificationDrainPlugin` and `DrainNotificationsPlugin`.
- `OT Home`, `OT Case List` and the new `OT Outcome Label` are all present, and a content
  `like` query confirms each carries its marker (`ot-outcomes-heading`, `f-outcome`,
  `OT Outcome Label`) — so the templates hold the new content, not just a modified timestamp.
- `outcome-testing.css` is 31,828 bytes in the environment, byte-for-byte the local file size.

## 2. What was deliberately NOT deployed: the drain step

**The `NotificationDrainPlugin` step is not registered, so nothing sends.** This is the
AD-081 gate working as designed, not an omission.

OD-030 said the server-side mailbox had never been checked on any environment. It has now
been checked on DEV, and the answer is that it is **not usable**:

```
name         : svc automate aq
emailaddress : svc.automate.aq@ascotlloyd.co.uk
outgoingemailstatus              : Not Run
isemailaddressapprovedbyo365admin: No
testmailboxaccesscompletedon     : 1/1/1900 12:00 AM   (never tested)
outgoingemaildeliverymethod      : Server-Side Synchronization
```

The mailbox is configured for server-side synchronisation but has never been approved by an
O365 admin and has never had its access tested. Registering the drain step against it would
not have failed quietly — it would have worked exactly as designed and made things worse:
every notification would attempt a send, fail, and be stamped `Failed`. The outbox would go
from an honest `Pending` backlog to a wall of failures, and the `Pending` state that
currently means "correctly waiting for a mailbox" would be destroyed.

**To switch notifications on, in order:**

1. An O365 admin approves the mailbox and runs *Test & Enable* on it, until
   `isemailaddressapprovedbyo365admin` is `Yes` and `outgoingemailstatus` is `Success`.
   Re-run the mailbox query above to confirm — do not take the UI's word for it.
2. Register the step:
   ```
   dotnet run --project plugins/OutcomeTesting.Registration -- registerstep \
     https://org0b075da8.crm11.dynamics.com/ \
     OutcomeTesting.Plugins.NotificationDrainPlugin Create al_notification 40 "" \
     async svc.automate.aq@ascotlloyd.co.uk
   ```
   From that moment new notifications drain on creation.
3. The backlog already queued at `Pending` does **not** drain on its own — the step fires on
   Create. Work through it deliberately with `al_DrainNotifications`, smallest first
   (`MaxRows: 1`), and read `al_failurereason` on anything that lands at `Failed` before
   running a larger batch.

## 3. Tooling corrections

Two more for the pile, both of which contradict the existing runbooks.

1. **The registration tool needs a roll-forward.** `OutcomeTesting.Registration` targets
   `net8.0` and this machine now carries only the .NET 10 runtime, so `dotnet run` fails with
   "You must install or update .NET to run this application" — which reads like a missing SDK
   rather than a missing *runtime major*. Prefix every invocation with
   `DOTNET_ROLL_FORWARD=LatestMajor`. Retargeting the project would be the real fix.
2. **`pac solution add-solution-component` wants the component type *name*, not its code.**
   `--componentType 371` resolves to `msdyn_Connector` and fails on a metadata lookup;
   `10088` is rejected outright as unknown. `--componentType CustomAPI` works and pac
   resolves the id itself. This is a much lighter way to get a custom API into the solution
   than the custom-API-only solution import the 2026-09-03 run had to use, and it needs no
   `src/customapis/` folder — which is why `al_DrainNotifications` does not have one.

## 4. The portal upload still does not complete

Reproduced exactly, unchanged from 2026-09-03:

```
Error: Upload failed for file 'adx_entitypermission'
(record a1000000-0000-4000-8000-000000000072): the target entity was not found.
```

Same record — `Response - on a review assigned to me`, a parent-scoped table permission this
change does not touch — and the same stale manifest id
`5140384b-f6a3-f111-aaac-e4fade069307` reported as a failed update. It reached 70 % (14/20
events) before dying. Nothing was lost: section 1 above is the verification.

Carry forward unchanged: the upload cannot be relied on to run to completion, so anything
uploaded needs verifying by query afterwards.

## 5. State after this deployment

- **PP-17 is live in the portal.** Outcome cards on Home, the outcome filter and column on
  the case list, and the shared grade-label template.
- **PP-15 is deployed but switched off.** Outbox, all five emitters, the drain code, the
  retry command and the para-planner recipient are all in the environment. No step is
  registered, so rows continue to rest at `Pending` and nothing claims an email was sent.
- **BR-009 rides on that switch.** The routing is built and deployed; para-planners will not
  actually be notified until the mailbox is approved and step 2 above is run.
- `src/` is still behind DEV on `al_Notification` (AD-013 export round trip, still owed).
