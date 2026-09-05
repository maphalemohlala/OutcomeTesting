# Deployment — portal repairs and PP-15 switch-on, 2026-09-05

Target: `Env_AQ_Dev`, as `svc.automate.aq@ascotlloyd.co.uk`. Three writes, each verified by
FetchXML afterwards rather than taken from the command's own output.

---

## 1. OD-035 closed — `/case-details` repointed

```
repointwebpage … case-details "OT Case Detail Page"
  a1000000-…-032 (root):    a1000000-…-0022 -> a1000000-…-002b
  a1000000-…-042 (content): a1000000-…-0022 -> a1000000-…-002b
```

Verified after: both rows now report `mspp_pagetemplateid = OT Case Detail Page`.

**One correction to the 2026-09-04 record.** It said the two web pages carried a **null**
page template. They did not — both pointed at `a1000000-…-0022`, the colliding id, which is
now the `OT Outcome Label` **web template**. The lookup was populated but aimed at a row of
the wrong component type, which is why a projection of it rendered blank and read as null.
The distinction matters for diagnosing the next one: a broken page here looks like an empty
lookup, but the underlying fault is a lookup pointing at a component that is no longer a page
template.

## 2. OD-033 half closed — `Administrators` over-grant cleared

```
setwebroleauth … "Administrators" false
  Web role 'Administrators' (c53b2908-…): authenticatedusersrole True -> False
```

Verified after: `Administrators` reads `No`, matching `webrole.yml`. `Authenticated Users`
correctly remains `Yes` — that is the stock role and source declares it so.

**`Checker` is untouched and still `Yes`**, because that half is a decision, not a
correction: it is declared nowhere in source, so there is nothing to reconcile it against.
See the register.

## 3. PP-15 switched on — the drain step is registered

```
registerstep … NotificationDrainPlugin Create al_notification 40 "" async svc.automate.aq@ascotlloyd.co.uk
  runs as systemuser 3c6f441a-…
  created sdkmessageprocessingstep: 76bbe7e0-…
  added to the OutcomeTesting solution
```

Verified after: **Post-operation, Asynchronous, Enabled**, on Create of `al_notification`.
Both preconditions were met first — mailbox `Yes`/`Success`, and the sending identity settled
as the approved account by project owner direction.

**But PP-15 is switched on, not proven.** `al_notification` holds **zero rows** — not a
drained outbox, an empty one. The emitters have never produced a notification in DEV since
the table was created on 2026-09-03, so:

- there is no `Pending` backlog to work through, and the `MaxRows: 1` procedure has nothing
  to run against;
- **no notification has ever travelled the full path** — emitter, outbox row, async drain,
  server-side email. The drain is registered and the mailbox is approved, and neither of
  those is evidence that an email arrives.

The honest next step is to cause one qualifying event in DEV — an allocation is the cheapest,
since `NotificationEmitterPlugin` fires on Create of `al_caseassignment` — then watch the row
appear, go `Sent`, and confirm the email lands. That is a real business write, so it is a
deliberate act for whoever owns DEV data, not a side effect of this deployment.

## 4. A tool fix this forced: portal reads need FetchXML

`repointwebpage` failed on its first run with "No page template named 'OT Case Detail Page'"
against a row that exists. The cause is worth carrying: **`QueryExpression` returns nothing
against the enhanced-data-model `mspp_*` tables on this site**, while a FetchXML query with
the identical equality filter returns the row immediately. The registration tool's shared
`FindId` helper uses `QueryExpression`, so it silently reports "not found" for any portal
component.

Both portal commands now read through a `PortalRows` helper built on `FetchExpression`. The
rest of the tool is unchanged — `FindId` is fine for the `systemuser`, `plugintype` and
`customapi` tables it was written for. Anything new that reads an `mspp_*` table must use
FetchXML, or it will report a row that exists as missing.

## What is now open in DEV

- **OD-034** — root cause found and the export side proven; the **import** side is untested,
  and the feature is preview. See section 5.
- **OD-033 second half** — the undeclared `Checker` role, still auto-granted to every
  authenticated user, still granting nothing.
- **PP-15 end-to-end proof** — see section 3.

---

## 5. OD-034 — root cause, and a fix proven on the export side

Added after the repairs above, once the site had been put into the `OutcomeTesting` solution.

**The defect is narrow.** `pac` routes **parent-scoped** table permissions — `adx_scope:
756150003` with an `adx_parententitypermission` — down the Standard-model path on an
enhanced-data-model site, and tries to write `adx_entitypermission`, which does not exist
there. Exactly two permissions in source are parent-scoped:

| Permission | Id |
|---|---|
| `Response - on a review assigned to me` | `…072` — the run aborts here |
| `Signoff - T&C attestation` | never reached, presumably equally affected |

Every other permission uses scope `756150000`/`756150001` and uploads without complaint.

**Three fixes ruled out, with evidence rather than opinion:**

- **Upgrading `pac`.** `dotnet tool search microsoft.powerapps.cli.tool` reports **2.11.2 as
  the latest published version** — exactly what is installed. There is nothing to wait for.
- **Scoping the upload.** `pac pages upload` accepts only `--path`, `--deploymentProfile`,
  `--forceUploadAll` and `--modelVersion`. Deployment profiles override *values* per
  environment; they do not exclude records.
- **Manifest drift.** The manifest does list four permissions absent from source, which `pac`
  would try to delete. All four (`Feedback`, three `PROVISIONAL DEV ONLY`) are **already gone
  from DEV**, so those attempts are no-ops. Real drift, but not the cause and not a hazard.

### The trap: adding the site alone proves nothing

The first export, with only the `powerpagesite` record in the solution, produced:

```
Assets/powerpagesites.xml      842 bytes
```

The site header — default language, header/footer template ids, primary domain — and **not
one** web page, web template, table permission or web role. Solution membership of the site
is not the same claim as "the site travels".

### The fix

`addsitetosolution` adds the site **and every one of its site components**. `pac` cannot do
this: 2.11.2 rejects the Power Pages component types by name (`PowerPagesSite` silently falls
back to `Entity`) *and* by number ("Component Type Id (10434) is not known"). The SDK's
`AddSolutionComponentRequest` takes the number directly. Type values are read from
`solutioncomponentdefinition` at run time, because Microsoft's own documentation gives two
different numbers for the site in a single example.

```
site 1, languages 1, components 250 -> 252 added, 0 failed
```

Re-export, and the package goes from 211 entries / 685 KB to **471 entries / 994 KB**, with a
`powerpagecomponents/` folder. Spot-checked by id, all present: the `OT Outcome Label` web
template, `OT Case Detail Page`, the `Case detail` web page, the `Administrators` web role,
`outcome-testing.css` as real file content — and the record that breaks the upload:

```xml
<powerpagecomponent powerpagecomponentid="a1000000-0000-4000-8000-000000000072">
  <content>{ "entityname": "Response - on a review assigned to me",
              "parententitypermission": "a1000000-0000-4000-8000-000000000071",
              "parentrelationship": "al_reviewinstance_response",
              "scope": 756150003, … }</content>
```

Intact, with its scope, its parent and its web-role links.

### What this does NOT prove

- **The import side is untested.** Only DEV is authenticated here, so the package has been
  shown to *contain* the site, not to *reconstitute* it. Export into a second environment and
  confirm before trusting the pipeline.
- **Power Pages solution awareness is a preview feature.** Microsoft's own CLI documentation
  states it plainly: "This feature is a preview feature. Preview features aren't meant for
  production use and may have restricted functionality." Adopting it as the PROD promotion
  path is a decision for the platform owner, not a tooling detail — and it should be taken
  alongside item 5 of the register, since `src/` still has to become the source of truth
  either way.
