# Power Pages portal security closure — design

Status: approved design, pre-implementation
Date: 2026-08-31
Sub-project: B of four (see "Decomposition" below)
Requirements: PP-01, PP-02, NFR-SEC-01, NFR-SEC-02
Decisions applied: OD-019, OD-021, OD-022, AD-047, AD-056, AD-059, AD-067
Supersedes: the provisional permission arrangement recorded in `powerpages/README.md`

## 1. Purpose

The portal's security model is currently a development scaffold. Every table permission
is bound to the stock **Authenticated Users** and **Administrators** roles; the seven
purpose-built `AL Portal - *` roles carry no permissions at all and are inert (AD-067);
there are no page permissions of any kind; and self-registration is switched on, so
anyone who reaches the URL can create an account.

This design closes that gap. It is the gate the portal design spec already states:
*"No feature page is built before this gate passes"*
(`docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`, section 9, step 2).

It does not add a feature. Nothing a user can do changes, except that users who should
never have reached the portal no longer can.

## 2. Decomposition

The outstanding portal work was split into four sub-projects. This spec covers B only.

| | Sub-project | Depends on |
|---|---|---|
| A | Command-by-write: reach `al_SubmitReview`, `al_CompleteRemediation` and `al_SignOffRemediation` from a page without Power Automate | — |
| **B** | **Security closure (this spec)** | — |
| C | Assignment filtering on My Work and the worklists | B |
| D | Remediation loop: adviser response, T&C attestation, working-day ageing | A, B |

A and B are independent. B was chosen first.

## 3. Current state, as measured

Established by reading the site metadata on 2026-08-31, not from the README, which is
stale on every row below.

| Fact | Evidence |
|---|---|
| Seven `AL Portal - *` web roles exist and hold no permissions | `webrole.yml`; no `adx_entitypermission_webrole` in any permission file names them |
| All 14 table permissions bind only to `c53b2908…` (Administrators) and `e24b50c5…` (Authenticated Users) | every `table-permissions/*.yml` |
| Seven `PROVISIONAL DEV ONLY - *` permissions remain, all Global read | ids `…062`, `…064`, `…065`, `…066`, `…067`, `…069`, `…06a` |
| `Feedback` grants **Anonymous Users** create on `feedback` | `Feedback.tablepermission.yml` |
| No **Restrict Read** rule exists, so every page is public | `webpagerule.yml` holds two Grant Change rules and nothing else |
| Self-registration is enabled | `Authentication/Registration/OpenRegistrationEnabled = true` |
| Stock starter content is still present | `contact-us`, `search`, `subpage-1`, `subpage-2` pages; the contact-us basic form; nine sample media web files |

### Tables the portal actually queries

Read from every `ot-*` web template, so the reference-config permission set is
evidence-led rather than assumed:

`al_outcomecase`, `al_reviewinstance`, `al_response`, `al_remediationaction`,
`al_failreason`, `al_section`, `al_questionversion`, `al_question`, and the
`al_al_failreason_al_response` intersect.

**No template queries `al_checklist`, `al_checklistversion` or `al_outcome`.** Their
provisional permissions are therefore deleted rather than replaced.

## 4. Web role matrix

Two resolved decisions are applied.

- **OD-019** — Adviser and Planner are separate portal roles. Add `AL Portal - Planner`
  as `a1000000-0000-4000-8000-000000000097`.
- **OD-021** — Regional Manager is notification-only and is not a portal user. Delete
  `AL Portal - Regional Manager` (`…094`) and any binding to it.

Final matrix: seven purpose-built roles (`…090`, `…091`, `…092`, `…093`, `…095`,
`…096`, `…097`) plus the three stock roles.

`…094` is retired, not reused. Reissuing a deleted component's id is the AD-059 failure
mode with a delay fuse.

## 5. Table permissions

### 5.1 Read scope

OD-022 (resolved 2026-08-31, project owner direction) is taken literally: *all
authenticated users see all cases; they can action only what is assigned to them.*

Read-all therefore stays bound to **Authenticated Users**, and the purpose-built roles
carry **write**. This extends the AD-056 read-everything/write-own boundary from the two
reviewer roles to every authenticated portal user.

Recorded consequence, accepted by the project owner: an adviser or planner can read
every client's case in the portal, including cases they have no involvement in. The
containment is that the write boundary is structural — write is reachable only through
the Contact-anchored parent chain — not that reads are scoped.

### 5.2 Target permission set

Permissions are **renamed and rebound in place wherever possible**, keeping their
existing ids. Given the AD-059 history, not churning ids is the safer edit: a delete
plus recreate is two chances to collide for no gain.

| Id | Name | Table | Scope | Roles | Change |
|---|---|---|---|---|---|
| `…060` | Outcome Case - read all | `al_outcomecase` | Global | Authenticated Users | unchanged |
| `…074` | Review Instance - read all | `al_reviewinstance` | Global | Authenticated Users | unchanged |
| `…075` | Response - read all | `al_response` | Global | Authenticated Users | unchanged |
| `…073` | Fail Reason - read | `al_failreason` | Global | Authenticated Users | unchanged |
| `…062` | **Remediation Action - read all** | `al_remediationaction` | Global | Authenticated Users | renamed from PROVISIONAL |
| `…066` | **Section - read** | `al_section` | Global | Authenticated Users | renamed from PROVISIONAL |
| `…067` | **Question Version - read** | `al_questionversion` | Global | Authenticated Users | renamed from PROVISIONAL |
| `…069` | **Question - read** | `al_question` | Global | Authenticated Users | renamed from PROVISIONAL |
| `…071` | Review Instance - assigned to me | `al_reviewinstance` | Contact, `contact_al_reviewinstance` | **Tax Reviewer, AQS Reviewer** | rebound |
| `…072` | Response - on a review assigned to me | `al_response` | Parent of `…071` | **Tax Reviewer, AQS Reviewer** | rebound |
| `…06b` | **Remediation Action - assigned to me** | `al_remediationaction` | Contact, `contact_al_remediationaction` | **Adviser, Planner** | new |
| `…064` | PROVISIONAL - Outcome | — | — | — | **deleted** |
| `…065` | PROVISIONAL - ChecklistVersion | — | — | — | **deleted** |
| `…06a` | PROVISIONAL - Checklist | — | — | — | **deleted** |
| `73e2df0d…` | Feedback | `feedback` | Global | Anonymous Users | **deleted** |

Scope option values as used in this site: Global `756150000`, Contact `756150001`,
Parent `756150003`.

`…06b` is minted from the free tail of the table-permission band as section 9 narrows it
(`60`–`6f`, of which `61`, `63`, `68` and `6b`–`6f` are unused).

### 5.3 Constraint honoured

Power Pages requires every web role on a child permission to also exist on its parent
permission. `…072` (child) and `…071` (parent) both bind to exactly Tax Reviewer and
AQS Reviewer, so the constraint holds by construction.

## 6. Page permissions

The layer that does not exist today, and the one that actually closes the URL-reachable
hole. Mechanics verified against Microsoft Learn `power-pages/security/page-security`
(updated 2026-01-27) and the `mspp_webpageaccesscontrolrule` table reference.

**Where these live, confirmed empirically on 2026-08-31.** Page access control rules are
*not* a new folder and *not* absent from source control. `pac pages download` serialises
them into the single top-level **`webpagerule.yml`**, alongside `webrole.yml`, and that
file is already in the repository holding the two Grant Change rules. A scratch download
from DEV with PAC CLI 2.11.2 produced a `webpagerule.yml` byte-identical to the committed
one, which settles both the file and its schema:

```yaml
- adx_name: Grant Change to Administrators
  adx_right: 1
  adx_scope: 1
  adx_webpageaccesscontrolrule_webrole:
  - c53b2908-1fc1-4470-89cd-6f5b95c17ffe
  adx_webpageaccesscontrolruleid: 563ee258-2962-4440-a6e8-d25296ac40bb
  adx_webpageid: 52570e2a-4d91-41f8-95c9-d0017a937039
```

Note there is no `adx_websiteid` in the serialised form — the site is implied by the
folder. New rules are appended to this file as further list entries.

- `adx_right`: `1` Grant Change, `2` Restrict Read.
- `adx_scope`: `1` All content, `2` Exclude direct child web files.
- A rule applies to its page **and every descendant page**.
- A page with no applicable rule is **public**.
- Grant Change is permissive and **overrides** Restrict Read.
- Multiple *active* rules on one page raise a conflict error; one per page.
- A child page's own rule must use a **subset** of its parent's roles, or the page
  becomes unreachable for the extra roles.
- The Anonymous Users role must never be bound to a page rule.

### 6.1 Rules

Appended to `webpagerule.yml`, with ids minted from the new `b0`–`bf` band (see
section 9).

| Id | Name | Page | Right | Scope | Roles |
|---|---|---|---|---|---|
| `…0b0` | Restrict read - portal roles | Home `52570e2a…` | Restrict Read (2) | **Exclude direct child web files (2)** | all seven `AL Portal` roles |
| `…0b1` | Restrict read - Tax reviews | Tax reviews `…033` | Restrict Read (2) | All content (1) | Tax Reviewer, Outcome Testing Manager, Portal Administrator |
| `…0b2` | Restrict read - AQS reviews | AQS reviews `…034` | Restrict Read (2) | All content (1) | AQS Reviewer, Outcome Testing Manager, Portal Administrator |
| `…0b3` | Restrict read - Review | Review `…036` | Restrict Read (2) | All content (1) | Tax Reviewer, AQS Reviewer, Outcome Testing Manager, Portal Administrator |
| `…0b4` | Restrict read - Remediation | Remediation `…035` | Restrict Read (2) | All content (1) | Adviser, Planner, T&C Supervisor, Outcome Testing Manager, Portal Administrator |

Every child rule's role set is a subset of the `…0b0` set, as the platform requires.

`My Work` (`…030`), `Cases` (`…031`) and `Case detail` (`…032`) get no rule of their
own and inherit `…0b0` — every portal role may read them, which is what OD-022 asks
for.

`Access Denied`, `Page Not Found` and `Default Offline Page` get no rule of their own.
They are children of Home, so they **inherit `…0b0` and are not public** — an earlier
draft of this section claimed they stayed public, which was simply wrong about how
inheritance works. The consequence is deliberate and is the safer direction: an
anonymous user who guesses a URL is redirected to sign-in rather than being shown a
custom 404, which discloses less, not more.

One consequence of that is not settled and must not be guessed at. An authenticated
contact holding **no** portal web role is denied Home, and the Access Denied page they
would be sent to is itself behind the same rule. Whether Power Pages serves its error
pages outside the web-page permission path or redirects in a loop is platform behaviour,
and only DEV can answer it. The role matrix in 10.3 already exercises exactly this case;
it now names the loop as a possible outcome. **If a loop appears, the remedy is to clear
`adx_parentpageid` on those three pages** so they leave Home's inheritance chain
entirely, rather than weakening the Home rule.

### 6.2 Why the Home rule excludes child web files

`bootstrap.min.css`, `theme.css` and `outcome-testing.css` are web files under Home. A
Restrict Read rule on Home with scope **All content** would restrict them to
authenticated users, and Learn documents the result explicitly: the sign-in page, which
is anonymous by necessity, renders unstyled. Scope `2` keeps the Home page's direct
child web files public while restricting every descendant *page*. This is deliberate,
and any future change of that scope value must account for it.

### 6.3 The pre-existing Grant Change rule

`webpagerule.yml` holds two rules, and reading them changes what the negative tests
should expect.

**`Grant Change to Administrators`** (`563ee258…`) is bound to the Administrators web
role, sits on **Home** (`52570e2a…`), and carries **scope 1, All content**. That is
exactly the configuration Learn flags: Grant Change overrides Restrict Read, and a
Grant Change on Home with All content scope overrides it *site-wide*. Administrators
will therefore reach every page regardless of every rule in 6.1. That is intended for a
platform administrator and is left in place, but it means "Administrators cannot see
page X" is not a test that can pass, and the DEV matrix must not assert it.

**`Grant Change to Content`** (`7f9846c2…`) has **no `adx_webpageid` and no web role**.
It grants nothing, applies to nothing, and is inert stock content. It is deleted as part
of section 8, so the file holds only rules that do something.

## 7. Authentication settings

| Setting | Now | Target |
|---|---|---|
| `Authentication/Registration/OpenRegistrationEnabled` | `true` | `false` |
| `Authentication/Registration/Enabled` | `true` | `false` |
| `Authentication/Registration/ExternalLoginEnabled` | `true` | **`true` — unchanged, see below** |
| `Authentication/Registration/LocalLoginEnabled` | `false` | `false` (unchanged) |

Entra ID OpenIdConnect becomes the only route in, which is what PP-01 requires and what
the AD-047 Entra-group-sync provisioning model already assumes. Contacts are provisioned
by sync; nobody self-registers.

### 7.1 ExternalLoginEnabled must stay ON, and an earlier draft of this section had it wrong

This design originally listed `ExternalLoginEnabled` among the settings to turn off, on
the reading that "external login" meant external self-service accounts. It does not. It
is the site-wide switch for **external identity providers**, and Entra ID OIDC is one of
them. Microsoft Learn's Entra setup page states it directly: if no identity providers
appear in the maker UI, *External login* must be On
(`power-pages/security/authentication/openid-settings`). This site's own
`AzureADLoginEnabled` description agrees, calling Azure AD "an external identity
provider".

With `LocalLoginEnabled` and `Registration/Enabled` both off, turning external login off
leaves **no route into the portal at all**. The value was briefly set to `false` in this
work and would have locked every user out of DEV on the next upload.

`Check-PortalSecurity.ps1` assertion 7 now asserts this setting is **`true`**, alongside
the three it asserts are false. An assertion that a hardening gate requires something to
be *enabled* looks wrong at a glance, which is exactly why it carries its reasoning
inline: the next person to "tidy" it will read why first.

Site setting ids are unchanged — only values change.

## 8. Starter content removal

Deleted, with every reference traced and removed in the same change:

- **Pages** — `contact-us`, `search`, `subpage-1`, `subpage-2`, and `pages` (the parent
  of the two subpages).
- **Basic form** — `simple-contact-us-form`, together with the `Feedback` permission it
  needs (already deleted in section 5.2).
- **Web files** — `Cat-PC.png`, `Circle-1/2/3.png`, `Geometric-2/4.png`, `Graph-1.png`,
  `Site-mockup-1.png`, `Video-1.mp4`.
- **Page rule** — the `Grant Change to Content` entry in `webpagerule.yml`, which names no
  page and no role and therefore grants nothing (see 6.3).

Retained: `Home`, `My Work`, `Cases`, `Case detail`, `Tax reviews`, `AQS reviews`,
`Remediation`, `Review`, `Access Denied`, `Page Not Found`, `Profile`,
`Default Offline Page`, the four CSS files, `robots.txt`, and the PWA trio
(`PWAManifest.json`, `PWALogo.png`, `OfflinePage.png`).

`OfflinePage.png` and `PWALogo.png` are **not** deleted despite looking like starter
media: the retained `Default Offline Page` and `PWAManifest.json` reference them, and
removing them would leave a retained page pointing at a missing file.

`Profile` is retained: Power Pages redirects to it after sign-in, and the profile
navigation weblink set points at it.

Verified before writing this: the primary navigation weblink set targets only Home,
Cases, Tax reviews, AQS reviews and Remediation, and no `ot-*` web template references
the search page, the contact-us page or any deleted media file. The stock
`search-facet-*` content snippets become orphaned but are inert and are left alone;
deleting thirty snippets to tidy a page nobody reaches trades real collision risk for no
security gain.

## 9. Id band extension (AD-059)

The AD-059 documented bands stop short of two component types already in use and one
this design adds. The README table and the decision are corrected to:

| Band | Component type |
|---|---|
| `10`–`1d` | web templates |
| `20`–`2a` | page templates |
| `30`–`36`, `40`–`46` | web pages |
| `50` | web files |
| `60`–`6f` | table permissions |
| `70`, `80`–`88` | web links and weblink sets |
| `90`–`97` | **web roles** (previously undocumented) |
| `a0`–`af` | **site settings** (previously undocumented) |
| `b0`–`bf` | **page access control rules** (new) |

The table-permission band is narrowed from `60`–`75` to `60`–`6f` because `70` is
already a weblink set and `71`–`75` are permissions that predate the correction and stay
where they are. The band table describes where *new* ids are minted; it does not
relocate existing components.

`Check-ComponentIds.ps1` currently matches component files by **suffix**
(`.webtemplate.yml`, `.tablepermission.yml`, …). Web roles and page rules do not fit that
shape: each is a single top-level list file named exactly `webrole.yml` and
`webpagerule.yml`. The checker therefore needs exact-filename handling in addition to
suffix handling, keyed on `adx_webroleid` and `adx_webpageaccesscontrolruleid`. Its
existing line-walking loop already collects every entry in a list file, so once the two
filenames are recognised, multi-entry files are handled correctly with no further change.

## 10. Verification

### 10.1 `Check-PortalSecurity.ps1`

A new script beside `Check-ComponentIds.ps1`, built test-first: every assertion is
written and shown failing against today's metadata before any metadata is changed, so
the script is proven to detect the fault it guards against rather than merely passing.

Assertions:

1. No table permission binds to a web role whose `adx_anonymoususersrole` is `true`.
2. No permission name contains `PROVISIONAL`.
3. Every retained business page is covered by a Restrict Read rule, on itself or on an
   ancestor. `Access Denied`, `Page Not Found` and `Default Offline Page` are the
   allow-listed exceptions.
4. Every Restrict Read rule's role set is a subset of the nearest ancestor rule's role
   set.
5. At most one active Restrict Read rule per page.
6. No page rule binds the Anonymous Users role.
7. `Registration/Enabled`, `OpenRegistrationEnabled` and `LocalLoginEnabled` are all
   `false`, and `ExternalLoginEnabled` is `true` (see 7.1).
8. Every `adx_entitypermission_webrole` references a role that exists in `webrole.yml`
   — the check that would have caught a binding to the deleted Regional Manager role.

Exit non-zero on any failure, so it can gate a release the way the README currently asks
in prose.

### 10.2 Tooling and what remains unverified

PAC CLI **2.11.2** is installed (`dotnet tool install --global Microsoft.PowerApps.CLI.Tool`)
— the same version the README's `--modelVersion Enhanced` syntax is verified against — and
an auth profile for `Env_AQ_Dev` is active under the service account. Downloads and
comparisons against DEV are therefore possible, and were used to settle section 6.

Two things still are not verified and must be done during execution:

1. **The upload round trip.** Download is proven to serialise `webpagerule.yml`; upload
   carrying new entries in it is inferred from that symmetry, not observed. Task 5 proves
   it by uploading and re-downloading to a scratch folder.
2. **Browser behaviour per role.** No automated check can replace signing in as each role
   and attempting the negative cases in 10.3.

**Sync state, measured 2026-08-31.** Ignoring line endings, the repository and DEV differ
only in the three files of the header/footer branding change. `sitesetting.yml` differs in
key ordering and YAML quoting but in no value. The repo is otherwise a faithful copy of
DEV, so an upload deploys the intended change and nothing else.

### 10.3 DEV test matrix (human step)

Per role, in an InPrivate session:

| Test | Expected |
|---|---|
| Anonymous user requests `/`, `/cases`, `/review?id=…` | Redirected to sign-in, no content disclosed |
| Anonymous user requests `outcome-testing.css` | Served — styling must survive on the sign-in page |
| Tax Reviewer opens `/aqs-reviews` | Access denied |
| AQS Reviewer opens `/tax-reviews` | Access denied |
| Adviser opens `/review?id=…` | Access denied |
| Adviser opens `/remediation` | Renders |
| Tax Reviewer opens another checker's review | Renders read-only; save is refused |
| Any authenticated role opens `/cases` | Renders (OD-022) |
| A contact with no portal web role opens `/` | Access denied — fail closed |
| Self-registration URL | Unavailable |

## 11. Risks

| Risk | Handling |
|---|---|
| Table permission **filenames** in the repo do not match what `pac pages download` emits — the repo has `Fail-Reason-Read.tablepermission.yml`, pac writes `Fail-Reason---read.tablepermission.yml` | A future `pac pages download --overwrite` into `powerpages/` would leave **both** files, each claiming the same `adx_entitypermissionid`, which is the AD-059 collision in a new disguise. `Check-ComponentIds.ps1` already detects it as a duplicate id, so the gate holds — but the trap is recorded in the README so the next person understands the duplicate rather than deleting the wrong copy. |
| A Grant Change rule on Home overrides every Restrict Read rule, site-wide, for Administrators | Confirmed in 6.3 against the real record. The DEV matrix deliberately does **not** assert that an Administrator is denied any page, because that assertion could only ever fail. |
| Deleting a web role or page that something still references | The `Check-ComponentIds.ps1` extension plus assertion 8; references traced in section 8 before deletion |
| Locking the Home page's child web files would unstyle the sign-in page | Scope `2` on `…0b0`, with the reasoning recorded in 6.2 so it survives the next edit |
| Contacts not yet in an Entra group have no portal role and lose access | Correct fail-closed behaviour, but it changes who can sign in on the day it deploys. The runbook calls for confirming role membership before upload. |

## 12. Out of scope

- Everything in sub-projects A, C and D.
- Column-level permissions. Not required by any PP requirement; the portal exposure is
  already controlled by which columns the templates render.
- The Entra group-to-web-role sync mechanism. AD-047 defers it to verification against
  current Microsoft Learn; this design consumes role membership, it does not provision it.
- TEST and PROD promotion. DEV only, per the standing rule that DEV is the sole
  authoring environment.

## 13. Decisions to record

| Ref | Content |
|---|---|
| AD-068 | Page permissions introduced: one Restrict Read rule on Home scoped to exclude direct child web files, per-page rules for role-specific branches, subset rule honoured, public pages allow-listed |
| AD-069 | Write-scope permissions bind to the purpose-built `AL Portal` roles; read-all stays on Authenticated Users per OD-022, superseding the AD-067 finding that the roles are inert |
| AD-059 (amended) | Band table extended to web roles, site settings and page access control rules; table-permission band narrowed to `60`–`6f` |
| OD-019, OD-021 | Marked implemented, with the ids added and retired |
