# Outcome Testing
### Ascot Lloyd · slide deck outline · 14 September 2026

---

## 1 · Who Ascot Lloyd are

One of the UK's largest independent financial advice firms.

- Founded **2003**, headquartered in **Reading**
- Around **39,000 clients** and **£14.8bn** of assets under advice
- Retirement and pension advice, investment planning, protection, tax planning
- Individual and corporate clients; independent, not tied to a product provider
- Grown substantially by acquisition — backed by Nordic Capital
- FCA regulated

> **Why it matters for this project:** every one of those client files is advice
> the firm must be able to prove was suitable. Scale is exactly what makes a
> manual review process untenable.

<sub>Firm figures are from public secondary sources (Citywire, Nordic Capital, CB Insights) — confirm against internal numbers before presenting.</sub>

---

## 2 · The business problem

**Ascot Lloyd must evidence that the advice its advisers give is suitable.**
That review runs today on Word documents, Excel trackers and email.

What that costs:

| | |
|---|---|
| **Intake is unguarded** | Intelligent Office extracts arrive with no validation gate |
| **Allocation lives in inboxes** | Who is checking what is not a fact anyone can query |
| **Checklists are not versioned** | We cannot prove which question set a file was graded against |
| **Outcomes get overwritten** | A regrade erases the original — the exact record a regulator asks for |
| **Remediation is chased by email** | No sign-off lock, no closure trail |
| **MI and Trail Light export** | Hand-assembled, every time |

### The risk underneath

We cannot evidence a consistent, auditable review process, and we cannot trace
a failed file to the remediation that fixed it.

**That is a regulatory exposure, not an efficiency problem.**

---

## 3 · The workflow being digitised

```
Import → Validate → Allocate → Check (Tax / AQS) → Outcome
                                                      │
                                        pass ─────────┼───── non-pass
                                          │           │
                                        Close    Remediation → Sign-off → Close
                                                                              │
                                                                       MI + Export
```

| Stage | What happens |
|---|---|
| **1 · Import** | Intelligent Office extract uploaded as a batch; every row validated, failures held as exceptions with a reason |
| **2 · Allocate** | Valid cases join a team queue; a lead assigns to a named checker, or a checker claims one themselves |
| **3 · Route** | Tax only, AQS only, or Tax then AQS — sequential when both apply |
| **4 · Check** | Checker answers the versioned checklist; fail reasons attach to individual answers |
| **5 · Outcome** | Pass · Pass with issues · Insufficient evidence · Potential harm |
| **6 · Remediate** | Non-pass opens remediation; the adviser responds in the portal |
| **7 · Sign off** | T&C Manager approves or rejects; approval locks the submitted sections |
| **8 · Close & export** | Case closes; MI datasets and a Trail Light-compatible export are produced on demand |

Every step is a **state the record is in**, not a step someone remembers to take.

---

## 4 · How we're solving it

### Dataverse as the system of record

The documents are replaced with a relational, versioned, audited model.

- **Outcome Case → Review Instance → Response** — one review table carries
  `reviewType` (Tax or AQS) rather than two parallel tables
- **Checklist → Version → Section → Question → Question Version**, delete
  restricted at every level; nothing is ever deleted, content is dated out
- Every read path is **date-scoped**: a submitted review always reads as it did
  on the day it was submitted
- **Initial and final outcomes are both preserved.** A regrade never erases what
  was first recorded
- Reopening a closed outcome is a privileged T&C Manager correction requiring a
  **mandatory reason on an immutable Audit Event**
- Documents stay in Intelligent Office and are **referenced, not copied**

### A lifecycle that is enforced, not documented

`Imported → Ready for Allocation → Queued → Assigned → Review In Progress → Submitted → Awaiting Remediation | Closed → …`

`CaseLifecycle` in the plug-in assembly gates **every** status write, server-side.
The app only ever offers a manager the transitions the server would accept.

### Two front ends, split by persona — not by feature

| Audience | Surface |
|---|---|
| Managers and administrators | **Power Apps Code App** — allocation, imports, question admin, security, audit, MI and export |
| Checkers, advisers, T&C Managers | **Power Pages portal** — where checks and the whole remediation loop are performed |

One dataset. Business logic lives in shared server-side commands, never
duplicated per front end.

---

## 5 · Why this approach

| Choice | Why |
|---|---|
| **Dataverse over SharePoint/Excel** | Relational integrity, row-level security, native auditing and a real state model. The evidence requirement is a data requirement |
| **Server-side plug-ins over app logic** | Two front ends, one rulebook. A rule enforced in the UI is a rule a direct data write ignores |
| **Versioned checklists, dated not deleted** | The regulator's question is "what was this graded against?" — that answer has to survive content changes |
| **Two front ends, one dataset** | Checkers and advisers need a light, licence-cheap web surface; managers need a dense admin tool. Same data, right tool per persona |
| **Power Platform** | Already licensed, already governed, native to the Microsoft 365 estate — no new vendor, no new identity model |

### Deliberate scope choices — not gaps

- **Allocation is manual.** A lead assigns, or a checker claims. No skills routing
- **Export is manual and on-demand**, Trail Light-compatible
- **SFTP automation and Power BI** are out of MVP scope — backlog, not reversed
- **Notifications are email only**

### Where it stands

Of the ten must-haves in the 8 September requirements pack:
**six built, two partial, two not started.**

The pack is largely a description of what already exists. The distance left is
**a second environment and a few workflow decisions — not features.**

**Next:** close four open decisions → sign off DEV → install the managed
solution into TEST.
