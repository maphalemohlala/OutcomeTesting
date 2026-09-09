# Automatic queueing of routed cases — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). All reads and writes through
`plugins/OutcomeTesting.Registration`; `pac` remains token-revoked.

## The report

AQS-only cases with no reviewer did not appear in the shared queue.

## What was wrong

The queue lists cases at Queued on a route that requires AQS. IO-000124 sat at Imported and
IO-000125 at Ready for Allocation, both routed AQS only. IO-000126 also sat at Imported, routed
Tax then AQS — once queued it will land on the Tax queue rather than the AQS one, because its
route requires Tax first. Nothing in the solution moved a case into Queued except a hand-back
from a Tax check or a sign-off; allocation walked straight past it to Assigned. So the AD-076
self-service queue only ever showed returned work.

## The fix (AD-093)

A routed case at Imported or Ready for Allocation is queued, hop by hop through
`CaseTransitions.MoveThrough`:

| Where | Change |
|---|---|
| `CaseQueueing` | the rule, pure, tested (`CaseQueueingTests`) |
| `ImportCasesPlugin.CreateRoutedCase` | derives the route from "Tax check required" via `DeriveRoute`, creates the case, queues it |
| `UpdateCaseDetailsPlugin.QueueAfterEdit` | after every save, queues a routed case still short of the queue; a status set by the caller wins |
| `queueroutedcases` verb | one-off: re-saves pre-existing routed cases through `al_UpdateCaseDetails` so the plug-in queues them and writes the audit event |

Tests: plug-ins 537 passed, 0 failed.

## What ran

| Step | Command | Result |
|---|---|---|
| Assembly push | `pushassembly https://org0b075da8.crm11.dynamics.com` | 172032 bytes, modified 2026-09-09 12:06:46Z (re-pushed after the review fix wave; the 11:44:34Z push at 170496 bytes carried the pre-fix build) |
| Backfill dry run | `queueroutedcases https://org0b075da8.crm11.dynamics.com` | IO-000124, IO-000125, IO-000126 listed |
| Backfill | `queueroutedcases https://org0b075da8.crm11.dynamics.com --confirm` | Refused by the session permission gate — to be run by the project owner |
| Queue check | portal AQS queue FetchXML | Pending — after the backfill |
| Audit check | `al_auditevent` with details containing `AD-093` | Pending — after the backfill |

## Left for the project owner

The backfill write (`--confirm`) was not run: the delivery session's permission gate refused it,
twice. Run these from the repository root, each with `DOTNET_ROLL_FORWARD=Major` set:

1. Run the backfill:

   ```
   DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll queueroutedcases https://org0b075da8.crm11.dynamics.com --confirm
   ```

   Expected: `Done: 3 of 3 queued.`

2. Verify the AQS queue sees IO-000124 and IO-000125:

   ```
   DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll fetch https://org0b075da8.crm11.dynamics.com '<fetch><entity name="al_outcomecase"><attribute name="al_casereference"/><attribute name="al_casestatus"/><filter><condition attribute="statecode" operator="eq" value="0"/><condition attribute="al_casestatus" operator="eq" value="120910583"/></filter><link-entity name="al_reviewroute" from="al_reviewrouteid" to="al_reviewrouteid" alias="route"><filter><condition attribute="al_requiresaqsreview" operator="eq" value="1"/></filter></link-entity></entity></fetch>'
   ```

   Expected: both `IO-000124` and `IO-000125` in the rows. Then run the same fetch with
   `al_requirestaxreview` in place of `al_requiresaqsreview` to verify the Tax queue sees
   IO-000126:

   ```
   DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll fetch https://org0b075da8.crm11.dynamics.com '<fetch><entity name="al_outcomecase"><attribute name="al_casereference"/><attribute name="al_casestatus"/><filter><condition attribute="statecode" operator="eq" value="0"/><condition attribute="al_casestatus" operator="eq" value="120910583"/></filter><link-entity name="al_reviewroute" from="al_reviewrouteid" to="al_reviewrouteid" alias="route"><filter><condition attribute="al_requirestaxreview" operator="eq" value="1"/></filter></link-entity></entity></fetch>'
   ```

   Expected: `IO-000126` in the rows.

3. Confirm the audit trail:

   ```
   DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll fetch https://org0b075da8.crm11.dynamics.com '<fetch><entity name="al_auditevent"><attribute name="al_name"/><attribute name="al_details"/><attribute name="createdon"/><filter><condition attribute="al_details" operator="like" value="%AD-093%"/></filter></entity></fetch>'
   ```

   Expected three rows.

## Notes

- Each backfilled case's audit event also carries a no-op line "Route <name> -> <name>",
  because an explicit RouteId is what gives the command a change to record; it is not a route
  change.
- The route seed (`data/route-seed/data.xml`, codes ROUTE-AQS and ROUTE-TAX-AQS) is now a
  prerequisite of the import command in every environment: without it, every row answering
  "Tax check required" fails with the precondition message.
- The backfill is idempotent per case by IdempotencyKey; a case that failed is retryable, and
  a second `--confirm` run simply finds no candidates.

## Not changed

Portal queue query, app worklist and its status filter, `CaseLifecycle` and the app's
`CASE_STATUS_TRANSITIONS`, allocation, claiming.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| Rule, import and edit changes, tests | Delivery (automated) | 2026-09-09 | Pass — 537 tests |
| Assembly push | Delivery (automated) | 2026-09-09 | Pass |
| Backfill of IO-000124, IO-000125 and IO-000126 | Delivery (automated) | 2026-09-09 | Pending — project owner |
