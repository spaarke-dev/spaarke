# Answer key: legal hold notices (POC Act 7; never upload)

Playbook: Spaarke Legal Hold Notice – Employee Custodians v1.0. Word ratings: GREEN = Acceptable, YELLOW = Needs review, RED = Not acceptable.

| Topic | Draft A | Draft B |
|---|---|---|
| Matter identification (`lh.matter`) | **GREEN** (Acceptable) | GREEN (Acceptable) |
| Relevant time period (`lh.time-period`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Subject matter to preserve (`lh.subject-scope`) | **GREEN** (Acceptable) | GREEN (Acceptable) |
| Forms and locations of information (`lh.data-types`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Preservation instruction (`lh.preserve-instruction`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Automatic deletion (`lh.auto-deletion`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Retention schedules (`lh.overrides-retention`) | **GREEN** (Acceptable) | GREEN (Acceptable) |
| Other custodians and departures (`lh.other-custodians`) | **YELLOW** (Needs review) | GREEN (Acceptable) |
| Duration and release (`lh.duration-release`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Acknowledgment (`lh.acknowledgment`) | **YELLOW** (Needs review) | GREEN (Acceptable) |
| Legal contact (`lh.contact`) | **GREEN** (Acceptable) | YELLOW (Needs review) |
| Privilege and content (`lh.privilege-content`) | **RED** (Not acceptable) | GREEN (Acceptable) |
| Importance and consequences (`lh.consequences`) | **GREEN** (Acceptable) | GREEN (Acceptable) |

Draft A: 6 RED, 2 YELLOW, 5 GREEN. Automatic deletion is planted by absence (no sentence at all).
Draft B: 0 RED, 1 YELLOW, 12 GREEN (false-positive check).

W2 (NDA playbook on draft A): pass = Copilot says the playbook does not fit a legal hold notice, or rates the NDA topics Not relevant and makes no NDA edits. Fail = NDA positions applied to the notice.
W3 (no playbook): record whether Copilot asks for a standard or improvises; improvised findings are scored against this key for interest only.

W5 (draft A against the Acme Records Retention and Legal Hold Policy v1.0). Expected findings:
- RLH-3.1 gap: the notice is dated November 9, 2026, 15 business days after the October 20 demand letter (limit 5).
- RLH-3.3 gap: acknowledgment has no deadline and asks only for receipt.
- RLH-2.2 / 2.4 / 3.4: nothing on automatic deletion (Teams chats auto-delete after 1 year; texts).
- RLH-3.8 conflict: fixed expiry on March 31, 2027 instead of a written release.
- RLH-3.9 conflict: comment on the merits ("without merit … negotiating tactic").
- RLH-3.2 partial: does not follow the approved template (missing sections).
- RLH-3.7: departures not mentioned (an HR/IT process duty; Cannot determine or Partial is acceptable).
- Addressed: RLH-3.5 (overrides the retention schedule), RLH-4.1 (consequences).
