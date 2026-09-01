# Checker Checklist V8 — content catalogue

Source: `Checker Checklist - V8.docx`, supplied 2026-08-26. This file is the authoritative transcription used to seed `al_Checklist`, `al_ChecklistVersion`, `al_Section`, `al_Question`, `al_QuestionVersion` and `al_FailReason`. Wording is reproduced verbatim. Mandatory flags, select cardinality, section ownership and the CRP rule are not in the document; they come from the process mapping session 2 workshop and are recorded as AD-019 to AD-022.

Checklist code `CHK-CHECKER`, version `V8`.

## Mandatory rule (AD-019)

Every displayed answerable question is mandatory before its section can be submitted. Observation, note and comment fields are optional. A hidden section is not validated; a conditional section that is displayed is validated in full. The per-question flag is still frozen on `al_QuestionVersion.al_IsMandatory` so that a later rule change cannot rewrite history (AD-004).

## Section ownership (AD-020)

| Section | Owner role |
|---|---|
| `S-TAX` | Tax team |
| `S-AMLCRA`, `S-FQOUT` | AQS checker |
| `S-E1` to `S-E5`, `S-CRP`, `S-CD`, `S-GRADE` | AQS checker |
| Remediation (separate form) | Adviser |
| Remediation validation, and sign-off for Insufficient evidence and Potential harm | T&C Manager |
| Checklist configuration | Manager / Admin |

## What is and is not checklist content

| Block in the document | Target |
|---|---|
| Case header | `Outcome Case` columns, captured at intake. Not versioned questions. |
| Tax check section | Section `S-TAX`, owned by the Tax reviewer |
| AML and CRA checking points | Section `S-AMLCRA` |
| File Quality fail points | `al_FailReason` seed rows, attached to `Response`. Not questions. |
| File Quality outcome | Section `S-FQOUT` |
| Suitability core checks E1–E5 | Sections `S-E1` to `S-E5` |
| Centralised Retirement Proposition | Section `S-CRP`, conditional |
| Consumer Duty overlay | Section `S-CD` |
| Checker judgement and grading | Section `S-GRADE`, plus `Outcome` |
| Remediation and escalation | Out of scope. The document marks it "PICKED UP ON ANOTHER FORM"; it maps to `Remediation Action`, `Sign-off` and `Recheck`. |

## Response types observed

| Code | Options | Used by |
|---|---|---|
| `Text` | free text, single line | header fields |
| `MultilineText` | free text, multi-line | case notes, fail observation, even better if |
| `Date` | date only | advice date, check date |
| `SingleSelect` | one bespoke policy list | primary root cause (AD-055) |
| `MultiSelect` | option list per question, several permitted | tax check reason |
| `PassFail` | Pass, Fail | file quality outcome |
| `PassFailInsufficient` | Pass, Fail, Insufficient evidence | suitability, CRP |
| `YesNoNA` | Yes, No, N/A | AML and CRA |
| `YesNoInsufficient` | Yes, No, Insufficient evidence | Consumer Duty |
| `YesNo` | Yes, No | remedial action required |
| `Grade` | Pass, Pass with issues, Insufficient evidence, Potential harm | advice quality grade (BR-005) |

## Case header (Outcome Case columns, not questions)

| Field | Response type | Options |
|---|---|---|
| Adviser name | Text | |
| Adviser status | SingleSelect | PreCAS, CAS, Enhanced, Watchlist |
| Adviser code | Text | |
| Paraplanner | Text | |
| Paraplanner code | Text | |
| Product(s) | Text | |
| Case type | SingleSelect | New advice, Ongoing, Review, Switch/Transfer |
| Advice date | Date | |
| Product / solution type | SingleSelect | Accumulation investment, Accumulation Pension, IHT, Protection, No change reviews |
| Sample source | SingleSelect | Random, Mandatory, High Risk, Thematic |
| Checker name | Text | |
| Check date | Date | |
| Client name / initials | Text | |
| IO reference | Text | The document asks "CAN THIS PRE-POPULATE FROM UPLOAD" — yes, this is the BR-001 import key. |
| Pre or post check | SingleSelect | Pre, Post |
| Vulnerable client? | SingleSelect | Yes, No, Potentially vulnerable, N/A |
| Tax check required | SingleSelect | Yes — complete tax check section. Drives the BR-004 route. |
| Tax team disposition | SingleSelect | Submit to AQS, Return to paraplanner |

## S-TAX — File Quality, Tax check section

Owner: Tax team. Rendered only when the route includes Tax (BR-004).

| Code | Question | Response type | Mandatory | Options |
|---|---|---|---|---|
| Q-TAX-01 | Tax check reason | MultiSelect | Yes | LSA/LSDBA/TTFAC, Trust, IHT, Tax calculation, Other |
| Q-TAX-02 | Tax check outcome | PassFailInsufficient | Yes | PASS, INSUFFICIENT EVIDENCE, FAIL |
| Q-TAX-03 | Case notes | MultilineText | No | |

Notes: the Tax outcome is a three-value scale, not the four-value AQS grade in BR-005. Tax check reason is multi-select because a case can fail for several tax reasons at once (AD-022).

Q-TAX-02 is recorded as `PassFailInsufficient`, not `SingleSelect`. Its options in the source document are PASS, INSUFFICIENT EVIDENCE, FAIL, which is exactly that scale; "SingleSelect" in the transcription described cardinality rather than a vocabulary. See AD-055.

## S-AMLCRA — File Quality, AML and CRA checking points

Owner: AQS checker. Response type `YesNoNA` throughout, all mandatory.

| Code | Question |
|---|---|
| Q-AML-01 | ID verification completed and retained for all relevant clients/parties. |
| Q-AML-02 | CRA completed with mandatory fields and risk rating recorded. |
| Q-AML-03 | CRA outcome recorded on FactFind/KYC and aligns to the file. |
| Q-AML-04 | CDD, source of funds/wealth and ongoing monitoring requirements met where applicable. |
| Q-AML-05 | High Risk CRA cases have supporting form, approval and rationale on file. |

## S-FQOUT — File Quality outcome

Owner: AQS checker.

| Code | Question | Response type | Mandatory |
|---|---|---|---|
| Q-FQ-01 | File quality outcome | PassFail | Yes |
| Q-FQ-02 | Fail observation | MultilineText | No |
| Q-FQ-03 | Remedial action required? | YesNo | Yes |

## Suitability core checks

Owner: AQS checker. Response type `PassFailInsufficient` throughout, all mandatory. Each section carries an "Outcome lens" statement, which is checker guidance rather than an answerable question; it belongs in section help text, not `al_Question`.

### S-E1 — Client Objectives & Information (COBS 9.2)
| Code | Question |
|---|---|
| Q-E1-01 | Client objectives clearly evidenced and specific |
| Q-E1-02 | Financial situation and needs sufficiently captured |
| Q-E1-03 | Inconsistencies or gaps identified and resolved |
| Q-E1-04 | Vulnerability indicators identified and considered (if applicable) |

Outcome lens: Does the evidence support that the advice was built around the client's actual needs and circumstances?

### S-E2 — Risk, Capacity & Loss (COBS 9.2 / FG)
| Code | Question |
|---|---|
| Q-E2-01 | Attitude to risk recorded and internally consistent |
| Q-E2-02 | Capacity for loss assessed and referenced in recommendation |
| Q-E2-03 | Any mismatch clearly explained and justified |

Outcome lens: Would a reasonable third party conclude the client was not exposed to foreseeable harm?

The E2 outcome lens row carries a single stray tick box in the source document. Treated as a formatting artefact, consistent with every other outcome lens row.

### S-E3 — Research & Recommendation Rationale (COBS 9.3)
| Code | Question |
|---|---|
| Q-E3-01 | Recommended solution aligns to objectives and constraints |
| Q-E3-02 | Reasonable alternatives considered or explained |
| Q-E3-03 | Product / portfolio risks explicitly addressed |
| Q-E3-04 | Switches / transfers justified (where applicable) |

Outcome lens: Is the recommendation clearly suitable, not just technically admissible?

### S-E4 — Costs, Charges & Value (COBS / Consumer Duty — Price & Value)
| Code | Question |
|---|---|
| Q-E4-01 | Adviser charges clearly disclosed and evidenced |
| Q-E4-02 | Ongoing charges justified relative to service provided |
| Q-E4-03 | Any concessions / off-tariff pricing approved and recorded |
| Q-E4-04 | Overall value conclusion coherent |

Outcome lens: Is there credible evidence the client received fair value?

### S-E5 — Suitability Report & Client Communication (COBS 9.4 / CD Understanding)
| Code | Question |
|---|---|
| Q-E5-01 | Report explains why the recommendation is suitable |
| Q-E5-02 | Key risks and trade-offs are clear and balanced |
| Q-E5-03 | Language is clear, fair and not misleading |
| Q-E5-04 | Material disadvantages explicitly highlighted |

Outcome lens: Could a reasonable client understand what they were agreeing to and why?

## S-CRP — Centralised Retirement Proposition

Owner: AQS checker. Response type `PassFailInsufficient`, all mandatory when the section applies.

Applicability is derived by the system from the case product/solution type (AD-021). The checker gets no manual applicability control, and the "Mark N/A" branch at step 4.6 of the flow is a system outcome, not a checker decision. Where CRP does not apply, the four responses are written as N/A rather than omitted, so the response set stays complete for MI. Whether ad hoc investment withdrawals trigger CRP is unresolved and tracked as OD-016.

| Code | Question |
|---|---|
| Q-CRP-01 | Cashflow model stress tests on file |
| Q-CRP-02 | Sequencing risk discussed with client |
| Q-CRP-03 | Derisking discussed / discounted |
| Q-CRP-04 | Annuity / drawdown discussion completed, including the option of securing required/basic income needs through an annuity |

## S-CD — Consumer Duty overlay

Owner: AQS checker. "Short yes/no judgements only." Response type `YesNoInsufficient`, all mandatory.

| Code | Question |
|---|---|
| Q-CD-01 | Products & Services outcome |
| Q-CD-02 | Price & Value outcome |
| Q-CD-03 | Consumer Understanding outcome |
| Q-CD-04 | Consumer Support outcome |

The instruction "Record any detail once in section H" refers to a section letter that does not appear in V8. Read as the Case Notes field in S-GRADE.

## S-GRADE — Checker judgement and grading

Owner: AQS checker. Regrade of Insufficient evidence and Potential harm is owned by the T&C Manager (AD-020).

| Code | Question | Response type | Mandatory | Options |
|---|---|---|---|---|
| Q-GR-01 | Advice Quality Grade | Grade | Yes | PASS, PASS WITH ISSUES, INSUFFICIENT EVIDENCE, POTENTIAL HARM |
| Q-GR-02 | Primary root cause | SingleSelect | Yes | FactFind quality, Risk / capacity mismatch, Research / rationale, Charges / value, Client communication, Process / documentation, AML / CRA, Retirement Proposition, Adviser judgement |
| Q-GR-03 | Case Notes | MultilineText | No | |
| Q-GR-04 | Even Better If... | MultilineText | No | |

Q-GR-01 matches the four BR-005 outcomes and the AD-008 colour tokens exactly. Q-GR-02 stays single-select so that trend MI has one dominant cause per case (AD-022).

## Fail reasons — `al_FailReason` seed rows

Two-part codes preserve the document's category prefix.

| Code | Category | Reason |
|---|---|---|
| FR-AML-01 | AML | ID verification issue |
| FR-AML-02 | AML | No CRA completed or missing data fields |
| FR-AML-03 | AML | CRA information not recorded on FactFind |
| FR-AML-04 | AML | CDD not completed in line with standards |
| FR-AML-05 | AML | CRA highlighted a High Risk, with no supporting form completed |
| FR-BRE-01 | Breach | Any other process breach has been identified |
| FR-BRE-02 | Breach | Any other regulatory breach has been identified |
| FR-REC-01 | Record Keeping | Client consent not evident on file |
| FR-REC-02 | Record Keeping | Concession required but not on file |
| FR-REC-03 | Record Keeping | Standard of file quality |
| FR-REC-04 | Record Keeping | File chronology does not align with the advice process |
| FR-REC-05 | Record Keeping | Information given relating to the recommendation/advice is incorrect |
| FR-REC-06 | Record Keeping | LPA/EPA not on file or has not been registered |
| FR-REC-07 | Record Keeping | Missing documents (provided when requested) |
| FR-REC-08 | Record Keeping | Missing documents (unable to provide) |
| FR-REC-09 | Record Keeping | No client agreement or client acceptance in place |
| FR-REC-10 | Record Keeping | Service & Charges brochure not issued or out of date |
| FR-REC-11 | Record Keeping | TOB not provided or out of date |
| FR-TAX-01 | Tax check | Not completed when this should have been |
| FR-TAX-02 | Tax check | Insufficient evidence to complete the check or to pass |

## Not answered by the document

1. Display order across sections. The document order is assumed to be the display order.
2. The product/solution type values that make CRP applicable are not enumerated. AD-021 fixes the mechanism; the mapping table itself still needs the list, and the ad hoc investment withdrawal case is open as OD-016.
3. Whether Fail observation becomes required when File quality outcome is Fail. AD-019 makes comment fields optional, so it is optional today. Flagged because a Fail with no observation gives remediation nothing to work from under BR-006.
4. Per-question option lists for `SingleSelect`. The schema has none, so a `SingleSelect` question's options are fixed by its response type. Resolved for V8 by AD-055; a future question needing its own list would need the schema change AD-022 declined.
