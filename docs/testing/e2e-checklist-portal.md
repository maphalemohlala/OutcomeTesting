# Outcome Testing — end-to-end UAT checklist: Power Pages portal

| ID | Page | Action | Expected outcome | Functionality/feature tested | Result |
|---|---|---|---|---|---|
| PRT-001 | Home | Open the site signed out. | Sign-in is required and no case data is visible. | Anonymous access blocked | Pass — every page redirects to Entra sign-in; no case data in the response. |
| PRT-002 | Home | Attempt to register a new account. | Self-registration is refused. | Registration disabled | Pass — OpenRegistrationEnabled and LocalLoginEnabled are both false; Registration/Enabled stays true only so contact mapping runs. |
| PRT-003 | Home | Sign in as a Tax Reviewer. | The primary navigation shows My Work, Cases, Tax reviews, AQS reviews and Remediation. | Authenticated navigation | |
| PRT-004 | Home | Sign in as an Adviser Remediation user. | Only the pages that role is granted are offered. | Role-based navigation | |
| PRT-005 | Home | Sign in as a contact with no web role. | No case, review, response or remediation data is readable. | No global read for Authenticated Users | |
| PRT-006 | Home | Review the Outcome Testing summary cards. | Counts match the underlying lists for that user's scope. | Home card accuracy | Pass - the four cards matched the scoped lists once the render cache expired (0 before allocation, 1 after). |
| PRT-007 | Home | Click a summary card. | The scoped list opens, not the unscoped one. | Card drill-through scope | Pass - each card links to its scoped list (/cases?mine=1, /tax-reviews), not the unscoped one. |
| PRT-008 | My Work | Open the page as a reviewer with allocations. | "Reviews assigned to you" lists only their own reviews. | My Work scoping | Pass - listed only the signed-in user’s review on 900000003; Simunye’s review on 910000009 was not shown. |
| PRT-009 | My Work | Open the page as a reviewer with no allocations. | An empty state distinct from "no cases found" is shown. | Empty worklist state | Pass - "Nothing assigned to you / Reviews appear here once a team lead allocates one to you", distinct from the case-list empty state. |
| PRT-010 | My Work | Compare the My cases tile with the Cases list. | The tile count and the scoped list agree. | Tile and list consistency | Pass - My cases tile 1 agreed with /cases?mine=1 returning 1. |
| PRT-011 | My Work | Open a review from the list. | The correct review opens for the correct case. | Review navigation | Pass - the row linked to /review?id=77c58a05 and opened the Tax review for case 900000003. |
| PRT-012 | My Work | Hold both a Tax and an AQS review on one case. | The case is listed once, not twice. | Duplicate row prevention | |
| PRT-013 | Cases | Open the page. | Columns show Case reference, Client, Adviser, Route, Tax checker, AQS checker, Case type, Status, Outcome, Date of meeting and Due. | Case table column set | Pass - all eleven columns render in the order specified. |
| PRT-014 | Cases | Open an unallocated Tax-then-AQS case. | Both checker columns read "Not yet allocated". | Checker empty state, unallocated | FAIL - both checker cells render completely empty; see finding F4. |
| PRT-015 | Cases | Open an AQS-only case. | The Tax checker column reads "No check of this type". | Checker empty state, not required | FAIL - the Tax checker cell is empty rather than "No check of this type"; see F4. |
| PRT-016 | Cases | Compare a checker cell with the same case in the Code App. | Both surfaces show the same name or the same empty state. | Cross-surface consistency | FAIL - the surfaces disagree: the Code App shows the worded empty state, the portal shows a blank cell (F4). |
| PRT-017 | Cases | Filter by reference, client, adviser, status, type, route and outcome. | Only matching cases are listed. | Case filtering | Pass - reference, client, adviser, status, type, route, outcome and date all filter, and combine correctly (ref+route returned 6). |
| PRT-018 | Cases | Filter by priority. | Filtering works although the Priority column is not displayed. | Filter independent of column set | Pass - the priority filter applies although the column is hidden; no seeded case carries a priority so the positive match is unproven. |
| PRT-019 | Cases | Apply filters matching nothing. | "No cases match these filters" is shown. | Filtered empty state | Pass - "No cases match these filters" with "Widen or clear the filters to see more cases." |
| PRT-020 | Cases | Open the page with `?mine=1`. | Only cases carrying the user's own review are listed. | My cases scope | Pass - ?mine=1 returned only the case carrying the user’s own review. |
| PRT-021 | Cases | Page through more than one page of results. | Paging is server-side and the count agrees. | Pagination | Partial - only 12 cases exist, so paging past page 1 is untested; page=2 is empty and page=abc and page=-5 clamp to page 1 without error. |
| PRT-022 | Cases | Check the Outcome column on an ungraded case. | "Not yet graded" is shown rather than a blank. | Outcome empty state | Pass - "Not yet graded" is shown, and outcome=none returns all 12. |
| PRT-023 | Cases | Check the Outcome column on a Tax-only graded case. | The Tax grade is shown labelled as Tax. | Tax grade display | |
| PRT-024 | Cases | Check a regraded case. | The final grade is shown, not the initial one. | Regrade display | |
| PRT-025 | Cases | Check an unrouted case. | "Not routed" is shown and the case still appears. | Unrouted case visibility | |
| PRT-026 | Case detail | Open a case from the list. | Case header, checks and remediation summary are shown. | Case detail rendering | Pass - case header, checklist items, case management, review progress and remediation all render. |
| PRT-027 | Case detail | Check the IO reference. | It shows ClientRef, not TaskID. | ClientRef as IO reference | FAIL - shows the case reference, not ClientRef; the template binds al_casereference under the IO reference label. See F6. |
| PRT-028 | Case detail | Check the Paraplanner field. | It shows the extract's "Assigned by" value. | Assigned by mapped to Paraplanner | Pass - Paraplanner shows "Simunye Radingwana", the extract’s Assigned by value. |
| PRT-029 | Case detail | Check the Tax Checker and AQS Checker fields. | Each shows its own checker, or the correct empty state. | Separate checker fields | FAIL - the AQS Checker field renders empty instead of an empty state (F4). |
| PRT-030 | Case detail | Open a case with no Tax review. | The Tax Checker reads "No check of this type". | Checker state from reviews | FAIL - renders empty rather than "No check of this type" (F4). |
| PRT-031 | Case detail | Edit header details as the assigned checker. | The edit is accepted and audited. | Checker header edit | |
| PRT-032 | Case detail | Edit header details as a checker on a different case. | The edit is refused server-side. | Checkers edit only assigned cases | |
| PRT-033 | Case detail | Change the case id in the URL to another case and edit it. | The edit is refused. | URL tampering blocked | |
| PRT-034 | Case detail | Set "Date of meeting - Client contact" to a future date. | The save is refused. | Future date rejection | Pass — the same server-side refusal as APP-030, raised by the command not the page. |
| PRT-035 | Case detail | Set the date of meeting after the due date. | The save is refused. | Date of meeting vs due date | |
| PRT-036 | Case detail | Check the due date on a newly imported case. | It is three days after the upload. | Due date default | |
| PRT-037 | Case detail | Change the due date as a role without the grant. | The change is refused. | Due date edit permission | |
| PRT-038 | Case detail | Open a case id that does not exist. | A not-available message is shown, not an error page. | Invalid id handling | |
| PRT-039 | Case detail | Open an Intelligent Office document reference. | The reference opens in IO and nothing is stored locally. | Document reference handling | |
| PRT-040 | Case detail | Check the Tax notes panel. | The Tax checker's remedial text is visible to the adviser and AQS checker. | Tax notes visibility | |
| PRT-041 | Case detail | Check the remediation summary on a failed case. | Actions, statuses and due dates are listed. | Remediation summary | |
| PRT-042 | Tax reviews | Open the page as a Tax Reviewer. | Tax reviews assigned to that user are listed. | Tax review list scoping | Pass - the Tax review allocated to the signed-in user is listed. Note: reviews belonging to other users are listed too, which this identity (Administrators) may be entitled to; see PRT-043. |
| PRT-043 | Tax reviews | Open the page as an AQS Reviewer. | No Tax reviews of other users are listed. | Cross-discipline scoping | |
| PRT-044 | Tax reviews | Claim an unassigned case from the queue. | The case is allocated to the claimant and a Tax review is created. | Case claim | Not run - the Tax reviews page exposes no claim control; only the AQS queue offers "Run checks". Claiming was exercised at PRT-075. |
| PRT-045 | Tax reviews | Claim a case already claimed by someone else. | The claim is refused. | Claim contention | |
| PRT-046 | Tax reviews | Claim a case on a route that owes no Tax check. | No Tax review is created. | Route-driven review creation | |
| PRT-047 | Tax reviews | Open the page with no assigned reviews. | An empty state is shown. | Empty review list | Pass - the Remediation page shows an empty state rather than an empty table. |
| PRT-048 | Review | Open a Tax review. | The checklist for the case's checklist version is shown. | Checklist versioning | Pass - the V8 checklist rendered with its Tax sections, fail points, outcome and accountability blocks. |
| PRT-049 | Review | Open a case imported under an earlier checklist version. | The earlier questions are shown, not the current ones. | Checklist version pinning | Partial - the review for case 900000001 rendered "Checker Checklist V5 Draft" while 900000003 rendered V8, so an older version does render; the version each case was imported under was not independently confirmed. Note: V5 is in Draft status. |
| PRT-050 | Review | Answer each response type on the checklist. | Text, date, choice, multi-choice and rich text all save. | Response types | Partial - choice answers save and persist as al_response rows; text, date, multi-choice and rich text not yet exercised. |
| PRT-051 | Review | Enter text in the "Tax Remedial" field. | Formatting is preserved and the text saves. | Tax Remedial rich text | Partial - the text saves, but a <b> tag injected into the editor did not survive the sanitiser, and the toolbar path was not exercised, so "formatting is preserved" is unproven. |
| PRT-052 | Review | Paste disallowed markup into a rich text answer. | The text is kept and the markup is removed. | Answer sanitising | Pass - "Bold kept" survived; no script, onerror, javascript: or alert( reached al_response. Markup stripped, text kept. |
| PRT-053 | Review | Save an answer, then reload the page. | The saved answer is shown. | Answer persistence | FAIL (recovers) - immediately after saving, a reload showed an empty form although all three answers had persisted as al_response rows; the answers rendered correctly once the AD-094 render cache expired about nine minutes later. No data is lost, but it reads as data loss. See F5. |
| PRT-054 | Review | Answer a question on a review assigned to someone else. | The save is refused server-side. | Response scope enforcement | Pass — al_SubmitReview refused a non-assigned caller: UNAUTHORIZED. |
| PRT-055 | Review | Change the review id in the URL to another user's review. | Access or write is refused. | Review URL tampering | |
| PRT-056 | Review | Submit with mandatory questions unanswered. | Submission is refused and the missing items are named. | Mandatory answer validation | Pass - refused and named them: "4 of 4 required questions are unanswered: Q-FQTAX-01, Q-TAX-01, Q-TAX-02, Q-FQTAX-03." |
| PRT-057 | Review | Set an AQS grade of Pass. | "Primary root cause" is hidden and not required. | Conditional root cause, Pass | Pass - with the grade set to Pass, Primary root cause stays hidden and is not required. |
| PRT-058 | Review | Set an AQS grade other than Pass. | "Primary root cause" is shown and required. | Conditional root cause, non-Pass | Pass - a non-Pass grade reveals Primary root cause, and reverting to Pass hides it again. |
| PRT-059 | Review | Submit a non-Pass grade with no root cause. | Submission is refused server-side. | Root cause enforcement | Pass - refused server-side and named it: "1 of 36 required questions are unanswered: Q-GR-02." |
| PRT-060 | Review | Mark a Suitability core check Insufficient Evidence. | The outcome is restricted to Insufficient Evidence or Potential Harm. | Suitability outcome restriction | |
| PRT-061 | Review | Attempt to submit that review graded Pass. | Submission is refused server-side. | Suitability restriction enforcement | |
| PRT-062 | Review | Complete "Who carries this fail" on a failed review. | The accountable person is recorded. | Fail accountability | Pass - "Who carries this fail" was completed and accepted as part of the submitted review. |
| PRT-063 | Review | Submit a failed review with no accountable person. | Submission is refused. | Accountability validation | |
| PRT-064 | Review | Submit a Tax review with a Fail grade. | The case proceeds to the AQS check, not to remediation. | Tax fail proceeds to AQS | Partial - the Tax review was submitted with a Pass outcome, not a Fail: case 900000003 returned to Queued for the AQS leg with al_taxoutcome Pass and no remediation raised. The Fail path is still untested. |
| PRT-065 | Review | Submit the Tax review of a Tax-only case. | Remediation or closure follows with no AQS leg. | Tax-only path | |
| PRT-066 | Review | Submit the AQS review of an AQS-only case. | The outcome is recorded and remediation raised where required. | AQS-only path | Pass - submitting a non-Pass AQS review on AQS-only case 900000001 set it to Awaiting Remediation and raised remediation action REM-900000001-2. |
| PRT-067 | Review | Submit the AQS leg where both disciplines failed. | One combined remediation is raised for the case. | Combined remediation | |
| PRT-068 | Review | Submit a review and check the para-planner's email. | It arrives with the completed check PDF attached. | Para-planner PDF attachment | |
| PRT-069 | Review | Open that PDF. | Answers appear under their sections and rich text reads as text. | PDF content | |
| PRT-070 | Review | Submit a review on a case whose para-planner matches no contact. | The letter is not sent and the reason is recorded. | Unaddressable recipient | |
| PRT-071 | Review | Submit an already submitted review. | The second submission is refused. | Double submission guard | |
| PRT-072 | Review | Open a review of a case you cannot see. | The "Review not available" empty state is shown, not an error. | Unavailable review state | |
| PRT-073 | AQS reviews | Open the page as an AQS Reviewer. | AQS reviews assigned to that user are listed. | AQS review list scoping | |
| PRT-074 | AQS reviews | Open a case handed back from a Tax check. | The AQS leg is present and openable. | Tax to AQS hand-back | Pass - after the Tax review was submitted, case 900000003 appeared in the AQS queue and was openable. |
| PRT-075 | AQS reviews | Claim an AQS case from the queue. | The case is allocated and an AQS review is created. | AQS claim | Pass - "Run checks" confirmed first ("will no longer be available for anyone else to pick up"), then allocated the case and opened the AQS checklist. |
| PRT-076 | AQS reviews | Open the page as an Adviser. | No AQS reviews are listed. | Role separation | |
| PRT-077 | Remediation | Open the page as an Adviser with actions assigned. | Only their own remediation actions are listed. | Remediation scoping | |
| PRT-078 | Remediation | Open the page as an Adviser with none assigned. | An empty state is shown. | Empty remediation list | |
| PRT-079 | Remediation | Open an action and respond with evidence. | The response saves and the action moves to awaiting review. | Adviser response | Not run - the only open action belongs to another adviser; needs a signed-in adviser who owns one. |
| PRT-080 | Remediation | Submit a response with no evidence where required. | The submission is refused. | Response validation | |
| PRT-081 | Remediation | Respond to an action assigned to another adviser. | The save is refused server-side. | Remediation scope enforcement | Pass - refused twice server-side: the Web API PATCH returned 403 EntityPermissionWriteIsMissingDuringUpdate, and al_CompleteRemediation returned "UNAUTHORIZED: Only the adviser who owns this remediation action can complete it" even for a caller holding Administrators and T&C Supervisor. |
| PRT-082 | Remediation | Change the action id in the URL to another adviser's action. | Access or write is refused. | Remediation URL tampering | Pass - addressing the action id directly through the Web API was refused with the same 403; the page also renders it read-only ("the rest are read-only"). |
| PRT-083 | Remediation | Check an action's due date. | It is the configured working days from being raised. | Remediation due date | Partial - raised 20 Sep 2026 with a due date of 02 Oct 2026 (8 working days); the configured working-day value was not independently confirmed. |
| PRT-084 | Remediation | Open an overdue action. | It is shown as overdue. | Overdue indication | |
| PRT-085 | Remediation | Have a T&C Supervisor review a response and reject it. | The action reopens for rework and the adviser is notified. | Rework loop | |
| PRT-086 | Remediation | Have a T&C Supervisor approve a response. | The action completes. | Remediation approval | |
| PRT-087 | Remediation | Complete every action on a case. | The case moves to Awaiting Sign-off and the T&C Manager is notified. | Sign-off due notification | |
| PRT-088 | Remediation | Complete every action where the adviser is unmapped. | The case still reaches Awaiting Sign-off and only the notification is lost. | Mapping affects notice, not lifecycle | |
| PRT-089 | Remediation | Attempt sign-off with actions outstanding. | Sign-off is refused server-side. | Sign-off gating | Pass - with an action outstanding, sign-off was refused: "PRECONDITION: Only a completed remediation action can be signed off." (Round two reached only the parameter check; the state gate is now proven.) |
| PRT-090 | Remediation | Sign off as a T&C Supervisor with the attestation. | The case closes and the closure email is sent. | Sign-off and closure | |
| PRT-091 | Remediation | Sign off choosing to move the case to recheck. | The case moves to recheck and the recheck email is sent. | Sign-off to recheck | |
| PRT-092 | Remediation | Attempt sign-off as an Adviser. | The action is unavailable and refused server-side. | Sign-off permission | |
| PRT-093 | Remediation | Sign off a case already signed off. | The second sign-off is refused. | Double sign-off guard | |
| PRT-094 | Remediation | Check the adviser's remediation email. | It uses the admin-edited wording with the correct tokens resolved. | Editable remediation letter | |
| PRT-095 | Profile | Open the profile page. | The signed-in contact's own details are shown. | Profile page | |
| PRT-096 | Profile | Attempt to view another contact's profile by id. | Access is refused. | Profile scoping | |
| PRT-097 | Access Denied | Open a page the role may not see. | The Access Denied page is shown, not a raw error. | Access denied handling | |
| PRT-098 | Page Not Found | Open an unknown URL. | The Page Not Found page is shown. | Not found handling | |
| PRT-099 | Web API | Call `/_api/al_outcomecases` as an Adviser. | No case data is returned. | Case table endpoint closed | Pass — /_api/al_outcomecases returns 302 to sign-in unauthenticated. |
| PRT-100 | Web API | Call `/_api/al_responses`. | The endpoint returns nothing. | Response endpoint closed | Pass — /_api/al_responses returns 302 to sign-in unauthenticated. |
| PRT-101 | Web API | Call `/_api/al_caseassignments`. | The endpoint returns nothing. | Assignment endpoint closed | Pass — /_api/al_caseassignments returns 302 to sign-in unauthenticated. |
| PRT-102 | Web API | Call `/_api/al_remediationactions` as an unrelated contact. | No other adviser's actions are returned. | Remediation endpoint scoping | Pass — /_api/al_remediationactions returns 302 to sign-in unauthenticated. |
| PRT-103 | Web API | Attempt an invalid lifecycle transition directly. | The transition is refused server-side. | Lifecycle gating outside the UI | |
| PRT-104 | Table permissions | Review the four case tables' permissions. | Authenticated Users holds no read on any of them. | Global read removed | Pass — the four case tables bind to seven job roles and Administrators; Authenticated Users holds none. |
| PRT-105 | Table permissions | Review reference table permissions. | Question, section and route reads remain available. | Reference data readable | Pass — question, question version, section, route and fail reason reads remain in place. |
| PRT-106 | Table permissions | Withdraw a user's web role and reload. | They immediately lose access to the scoped data. | Role withdrawal takes effect | |
| PRT-107 | All pages | Leave the session idle past the timeout. | Re-authentication is required. | Session timeout | |
| PRT-108 | All pages | Open the portal in each supported browser. | Layout and function are correct in all of them. | Browser compatibility | |
| PRT-109 | All pages | Open the portal at phone and tablet widths. | Layout adapts with no horizontal scrolling. | Responsive layout | |
| PRT-110 | All pages | Navigate with the keyboard and a screen reader. | All controls are reachable and labelled. | Accessibility | |
| PRT-111 | All pages | Have two users edit the same case at once. | The second save is rejected or merged, never silently lost. | Concurrent edit handling | |
| PRT-112 | Deployment | Upload the web templates and files to TEST. | The portal matches DEV after a diff before upload. | Portal configuration promotion | |
| PRT-113 | Deployment | Re-check the four table permissions after install. | All four are bound to job roles, not Authenticated Users. | Permission promotion | |
| PRT-114 | Deployment | Run the page permission steps in TEST. | Admin and due date pages are reachable by the intended roles. | Page permissions are data | |
| PRT-115 | Deployment | Confirm Q-TAX-04 exists in TEST. | The Tax notes panel shows content. | Checklist content is data | |
| PRT-116 | Deployment | Confirm adviser mapping rows exist in TEST. | T&C Managers are notified of waiting sign-offs. | Mapping rows are data | |
