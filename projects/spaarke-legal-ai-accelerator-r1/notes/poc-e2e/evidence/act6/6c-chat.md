# Act 6c chat: `nda-library-review` (our skill) on the NDA batch folder

Cowork task "NDA Review Dashboard Across Acme Team Site", 2026-11-06 00:50. Copied from the chat as shown.

**Prompt:** "Review all the NDAs in the Acme Company team site folder Shared Documents/Legal Knowledge/Inbox/NDA batch/ against our playbook, and give me the library view: a grid of NDAs by topic with a dashboard. Save the results to my OneDrive Documents/Cowork/Test/POC-Act6/."

Routed to the custom skill `nda-library-review` without being named.

**Plan card:** "Plan: review 16 NDAs (b01–b13 Word, b14–b16 PDF) in the NDA batch folder, mutual side, balanced posture, against "Spaarke NDA – Mutual v1.0" (team-site playbook; validated, covers all 24 topics; marked POC test positions, not attorney-approved). Outputs to Documents/Cowork/Test/POC-Act6/: NDA Library Review.xlsx (grid), NDA Library Review - Dashboard.html, and a data folder of per-NDA review files. Note: quotes from the 3 PDFs can't be script-checked. Start?" We chose Start.

**Approvals:** 2 × Create folder (`Test`, which already existed, then `POC-Act6`), 1 × Create folder (`NDA Library Review - data`, "Create this and the rest"), and 2 × Upload file content ("Upload this and the rest", asked again for the data files).

## Final message (verbatim)

I reviewed all 16 NDAs and saved a grid of NDAs by topic plus a dashboard. 14 of the 16 have at least one RED (a term the playbook says to escalate or that is missing a required protection). b01 Kestrel matches the playbook on every topic, and b02 Ninefold and b13 Petrichor have only YELLOWs (acceptable fallback positions). This is a draft for attorney review, not legal advice.

Standards: Spaarke NDA – Mutual v1.0 (your playbook on the Acme Company team site), mutual side, balanced posture. The playbook is marked as test positions that an attorney hasn't approved yet. It covers all 24 review topics, so nothing fell back to the generic baseline. Skill: nda-library-review v0.1.2-poc.

Coverage: b01–b13 are Word files and b14–b16 are PDFs. Nothing was skipped, and none of the agreements contained text trying to give instructions to a reviewer.

Totals: across 384 topic ratings there are 25 RED, 21 YELLOW and 338 GREEN.

| NDA | RED | YELLOW | Main problems |
|---|---|---|---|
| b16 Oakhollow | 3 | 4 | Residuals clause (8.1); derived-data use for its own business purposes (4.5); no responsibility for Representatives (4.2) |
| b10 Verdant Loop | 3 | 1 | No duty to return or destroy information (7.1); all obligations end 1 year after the Effective Date, including for trade secrets (8.1) |
| b03 Larkspur | 2 | 1 | Derived-data use for product development (4.5); bars reporting to government agencies and has no DTSA notice (10.1) |
| b05 Quillon | 2 | 0 | Independent-development exclusion lacks "without use of or reference to" (3.1(d)); no reverse-engineering ban although pre-release software will be shared |
| b06 Marrow & Finch | 2 | 0 | No liability for Representatives (4.2); 24-month no-solicit and no-hire with no general-solicitation exception (9.1) |
| b07 Bluefield | 2 | 1 | One-way non-compete binding only Acme, though headed "Non-Solicitation" (9.1); disclaims confidentiality duties implied by law (12.1) |
| b08 Saltmarsh | 2 | 0 | Waiver of injunctive relief plus a $1,000 liability cap (9.1); exclusive London venue (12.3) |
| b09 Cobaltline | 2 | 1 | Only marked information is protected (2.1); spoken and visual disclosures are not protected (2.2) |
| b11 Harborlight | 2 | 1 | Acme's feedback is excluded from protection, a one-sided carve-out (3.2); may disclose under legal compulsion on its own judgment, with no notice (5.1) |
| b12 Ironvale | 2 | 0 | Use "for any lawful purpose" (4.1); discloser warrants its information is accurate and complete (6.1) |
| b04 Tidewell | 1 | 1 | Residuals clause (8.1) |
| b14 Glasswing | 1 | 0 | Either party may assign without consent (12.2) |
| b15 Northbeam | 1 | 4 | No protected-reporting clause or DTSA notice; whether the notice is needed depends on the parties and should be checked |
| b02 Ninefold, b13 Petrichor | 0 | 4 / 3 | Fallback positions only |
| b01 Kestrel | 0 | 0 | Matches the playbook throughout |

Topics most often RED (2 NDAs each): derived data and licence-back, residuals, Representatives, non-solicitation, trade-secret preservation, and whistleblower/DTSA notice. The most common YELLOW is housekeeping (4 NDAs): the party descriptions in b07, b09, b10 and b15 say "a Ohio", "a Arizona", "a Oregon" and "a Illinois" corporation, so confirm each counterparty's correct legal details.

Quote checks:
- Word files: all 289 quotes were found word for word in the agreements, and none were missing.
- PDFs (b14–b16): a script can't check quotes from PDFs, so their 67 quotes show as "unchecked" in the grid. I compared them against the PDF text myself.

Files saved in OneDrive at Documents/Cowork/Test/POC-Act6:
- NDA Library Review.xlsx: the grid, one row per NDA and one column per topic. Each cell shows the rating and clause number, and the detail rows hold the quotes and notes. Each NDA links to its file on the team site.
- NDA Library Review - Dashboard.html: summary tiles, a heatmap and the top issues.
- NDA Library Review - data: a folder with one review file per NDA, so the run can be audited or re-scored.

Redline the RED ones with nda-review?
