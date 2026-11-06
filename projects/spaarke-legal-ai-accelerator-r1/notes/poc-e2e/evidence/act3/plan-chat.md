# Act 3 step 2: Cowork plan preview (verbatim from chat, 2026-11-05 23:04)

Prompt: `Review this NDA: "Brightwater Mutual NDA.docx" in the Acme Company team site, Shared Documents/Legal Knowledge/Matters/Brightwater Systems/.`

Routing: Spaarke NDA (MVP) (nda-review). Connectors: SharePoint, Work IQ. 17 steps before the plan.

---

Ralph, I've reviewed the Brightwater Mutual NDA and drafted the proposed changes. Nothing has been built yet. Please check the plan below before I create the files.

Standards: Spaarke NDA – Mutual v1.0 (customer playbook) · Skill: nda-review v0.1.2-poc

I applied the defaults from your profile: a mutual review with a balanced approach. The playbook covers every checklist topic and passed validation. The playbook itself says its positions are test positions and haven't been approved by an attorney. The agreement had no existing tracked changes, hidden text or text addressed to automated reviewers.

Proposed changes (High priority first)

| # | Section | Topic | Priority | Revision |
|---|---|---|---|---|
| 2 | 2.1 | Definition / marking | High | Protect unmarked information that a reasonable person would understand to be confidential. Right now it's protected only if marked or designated in writing within 30 days. |
| 4 | 3.2 | One-sided carve-out | High | Delete. Acme's requirements, specifications and feedback are excluded from protection, and Brightwater's are not. The section becomes "Intentionally Omitted." |
| 6 | 4.2 | Representatives | High | Add: "Each party is responsible for any breach of this Agreement by its Representatives." |
| 7 | 4.4 | Derived data | High | Delete. It lets either party use derived or aggregated data for its own product development. |
| 10 | 8.1 | Residuals | High | Delete and escalate. Information kept in "unaided memory" could be used freely. |
| 11 | 9.1 | Term | High | Protection lasts 3 years after each disclosure instead of 2. Trade secrets stay protected for as long as they qualify. |
| 3 | 3.1(d) | Independent development | Medium | Add "without use of or reference to the Disclosing Party's Confidential Information." |
| 5 | 4.1 | Reverse engineering | Medium | Add a mutual ban on reverse engineering. Prototype hardware and pre-release firmware are in scope, and the draft doesn't address this. |
| 8 | 5.1 | Compelled disclosure | Medium | Replace "in its reasonable judgment" with prompt notice where legally permitted, cooperation on a protective order and disclosure of only what's required. |
| 9 | 7.1 | Return / destruction | Medium | Add a 30-day deadline, written certification on request and an exception for backups or copies the law requires keeping. |
| 1 | Preamble | Housekeeping | Low | Comment only: confirm the Effective Date, which is blank. |
| 12 | Signature | Housekeeping | Low | Comment only: confirm Jordan Ellis has authority to sign for Acme. |

Escalations (prohibited under the playbook)

- Residuals (8.1): needs senior approval to accept any version. I recommend deleting it.
- Also prohibited, fixed by edits above: the one-sided carve-out (3.2), the derived-data clause (4.4), no responsibility for Representatives (4.2), disclosure on the recipient's own judgment (5.1), the unqualified independent-development exclusion (3.1(d)) and no reverse-engineering ban (4.1).

Accepted as playbook fallbacks, no edit

- 12-month mutual non-solicit with a general-solicitation exception (10.1)
- California law with non-exclusive California courts (14.3); California is Brightwater's home state

Open questions

1. What Effective Date should go in the preamble?
2. Is Jordan Ellis (VP, Operations) authorized to sign for Acme?
3. The draft has no notices clause. Should one be added?
4. Do you want to push for Delaware law, or accept California?
5. Does the business accept the non-solicit, or should it come out?

Once you approve, I'll create five files: an internal redline with comments, a redline for Brightwater, a clean revised copy, a review dashboard and an audit pack. This is a draft for attorney review, not legal advice.

Card: "Should I build the redlines, dashboard and audit pack from this plan?" Approve and build / Change something / Skip.
