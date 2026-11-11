# External inputs review (2026-11-05)

Two outside inputs were checked against our own tenant evidence (`notes/mvp-nda/results.md`, `run-log.md`) and the researcher notes:

- **A.** A Copilot/Cowork expert's demo of a "legal-redliner" Cowork skill (19 screenshots in `notes/demo-video/`).
- **B.** `notes/Spaarke-Accelerator-Review-Discussion.md`, a Q&A compiled 2026-11-04 from Microsoft documentation and secondary sources.

Marks: ✅ verified in our tenant · 📄 documented by Microsoft · ❓ unverified · ✗ contradicted by our evidence.

---

## A. The legal-redliner demo

### A.1 What it shows

- **When:** the task list and outputs are dated 7–12 April 2026, with Cowork still marked "(Frontier)" and Claude Opus 4.6 in the model picker. That is **six months before Copilot for legal and Legal for Cowork shipped** (2026-10-15). Today an MSA redline request would compete with Microsoft's `review-contract` and `edit-contract`: in our T-09, "Review this MSA" went to Legal for Cowork.
- **Install:** a personal skill folder at OneDrive `Documents/Cowork/Skills/legal-redliner/`, holding `SKILL.md` (20 KB), `references/` (2 files), `scripts/segment_contract.py` (12 KB) and `assets/` (3 files).
- **Script:** `segment_contract.py` imports only `json`, `re`, `sys` and `pathlib`. It segments **plain text** (already extracted by the PDF or docx skill) into clauses with IDs and flags, and outputs JSON. It doesn't touch the `.docx`.
- **Redline:** the redlined Word file comes from Cowork's built-in Word/docx skill, and an Excel "audit pack" comes from the Excel skill. The audit pack has four sheets: Change Log, Risk Summary, Human Review Checklist and Coverage Summary. The screenshots show the dashboard and the Excel file; **they don't show the tracked changes in the Word output.**
- **Preview:** a risk-dashboard Adaptive Card is shown *before* any files are generated.
- **Verbatim clauses:** "pre-approved" boilerplate in `assets/standard-clauses-uk.md` is inserted word for word for missing clauses.
- **Citations:** the agent web-fetches citations from legislation.gov.uk.
- **Routing:** the user's prompt names each step ("using the segmentation script", "compare against our UK standard positions").
- **Email:** "Send email" sits under **Always allowed**.
- **Container paths:** the skill-builder prompt cites `/opt/workspace-config/.claude/skills/` and `/mnt/user-config/.claude/skills/`, and states "Max 20 custom skills".

### A.2 Are Python scripts in skills supported in Cowork today (November 2026)?

**Yes. Verified in our tenant on 2026-11-01, on the current service, in both install routes.**

| Evidence | Detail |
|---|---|
| ✅ T-05 (plugin upload) | `probe.py` reported Python 3.12.15, lxml 6.1.3, python-docx 1.2.0, `soffice`, `pdftoppm`, and a writable working folder. All six engines ran unmodified and the full review passed its 10 checks. |
| ✅ T-03 (Claude-format plugin) | `scripts/probe.py` ran from `/mnt/shared/plugins/<id>/1.1.0/skills/nda-review` with `ok: true` |
| ✅ T-02 (OneDrive folder skill, no plugin) | The same engines ran from `Documents/Cowork/Skills/nda-review/`, and the review passed all 10 checks |
| ✅ T-06 to T-11 | Ten more full runs with scripts, on Auto, Sonnet 5.5 and GPT 5.5 |
| 📄 Microsoft Learn (re-checked 2026-11-05, **unchanged since 10-30**) | [cowork-plugin-development](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development) (updated 2026-11-03): "Scripts (`scripts/`) \| Executed, not loaded into context", still with `scripts/extract-clauses.py` as the example. [use-cowork](https://learn.microsoft.com/en-us/copilot/cowork/use-cowork) (updated 2026-11-04): "Some background operations, like script execution, run without displaying individual steps." Caps unchanged: 50 custom skills, 1 MB per `SKILL.md`, 20 companion files and 10 MB per skill (5 MB per file). No deprecation and no runtime detail in whats-new or in Message Center titles. **The runtime still isn't documented:** no interpreter version, packages, egress rule or time limits. The old `/microsoft-365/copilot/cowork/...` URLs now return 404; use `/copilot/cowork/...`. Source: researcher note `cowork-skill-runtime-and-mvp-claims-2026-10-30.md`, addendum 2026-11-05. |
| ✗ Word | Word doesn't load or run third-party skill scripts. Word itself said "Python execution isn't available here" (L21, L25). |
| Excel (preview) | Runs `scripts/*.js` as Office.js, not Python |

**Caveats that stay in the design:**
- The runtime is undocumented, so every engine skill runs `probe.py` first and stops if something is missing.
- Re-verify at T-14 and after Ignite (17–20 Nov 2026).
- The demo's stdlib-only, plain-text approach is a valid fallback if `lxml` ever disappears. It can't produce our tracked changes, bookmarks or linked summary, though.

### A.3 Comparison with our NDA accelerator, and what we take

| Demo element | Ours | Verdict |
|---|---|---|
| `segment_contract.py`: plain text, stdlib only | `segment_clauses.py` reads the OOXML and returns paragraph keys | **Keep ours.** Tracked changes, comments and bookmarks need anchors in the real document. Plain-text segmentation is the documented fallback. |
| Redline through the built-in Word skill | `ooxml_redline.py`, driven by a validated edit plan | **Keep ours** for the packaged deliverable. Our T-12 and T-13 show the built-ins: Word Copilot makes whole-paragraph replacements; `edit-contract` makes word-level edits but no comments, no summary, no clean copy and no external copy. The demo shows no redline evidence. |
| `references/uk-standard-positions.md`: positions inside the skill | Positions in a playbook file outside the skill; the baseline is a fallback (protocol §1, rule 2) | **Keep ours.** A position inside a skill can't be changed without editing the skill, can't be governed, and can't reach Word. |
| `assets/standard-clauses-uk.md`: approved wording inserted verbatim | The playbook's "Model language" per topic. Only 5 of 24 NDA topics have any. | **Adopt the rule.** When a topic has approved model language, the edit uses it verbatim. The model drafts only when none exists, and says so in the comment. This raises the priority of writing model language for all topics (H2). |
| `assets/privilege-notice.md` header | None | **Candidate (attorney decision):** a privilege header on the internal redline only |
| `assets/cover-note-template.md` | `assets/email-cover.html`, `summary-intro.md` | Same idea; nothing to change |
| Risk dashboard as an Adaptive Card *before* files are made | Edit plan → files → checks, then a chat summary | **Adopt as an option:** a "plan preview" card (counts, High items, escalations) with Approve / Change before generating. Skipped in unattended mode. The demo shows the card renders; whether its buttons send anything back is still S7. |
| Excel audit pack (4 sheets) | None; the internal redline's page-1 summary covers part of it | **Candidate output:** an engine generates it deterministically from `edit-plan.json` (change log, risk summary, review checklist, topic coverage). Owner decision. |
| Web fetch for citations | §5.7-9 "cite only from the document, references or a fetched official source; else `[verify]`" | Same rule. Fetching is agent-side and depends on the tenant's web-search policy. Scripts get no network. |
| "Parallel agent" for the docx and xlsx steps | Not relied on | ❓ Undocumented. Learn says Cowork runs skills sequentially. Don't depend on it. |
| "Always allowed: Send email" | Email to the requesting attorney only, after approval | **Keep ours.** Onboarding warns against blanket email pre-approval. |
| `description: \|` (block scalar) in frontmatter | One plain line (gate `description-plain`) | L22: the folder loader shows block scalars literally. Keep the gate. |
| "Max 20 custom skills" | 50 per user | ✗ Out of date: 50 per user (📄). 20 is the per-plugin skill cap and the companion-file cap. |
| `/opt/...` and `/mnt/user-config/...` paths | Never hard-coded | Implementation detail. We saw `/mnt/shared/plugins/...` for plugins (T-03). |
| Quality bar of 200–400 lines and ≥ 8 triggers | Lean `SKILL.md`, detail in `references/`, gated description | Keep ours. Line counts aren't quality. Adopt the troubleshooting table, which we already have as "Common issues". |

**Net:** the demo confirms our architecture (skill folder + references + scripts + assets; mechanics in code). It adds three ideas worth testing: a plan-preview card, an Excel audit pack, and verbatim approved wording. It doesn't change the decision to keep our OOXML engines.

---

## B. The discussion Q&A (2026-11-04)

| Item | Claim | Our evidence | Verdict |
|---|---|---|---|
| P1 Q1 | The Word legal plugin is on by default; Cowork needs a user step | S5: on in Word desktop, absent in Word web; T-09: Legal for Cowork present | ✅ consistent. Add: Word **desktop** only. |
| P1 Q2, P2 Q1 | Distribute as a partner plugin in the App Store, with a readiness scan | Design OD-8: manual delivery in R1; §3.2: Partner Center out of scope | **Out of R1 scope.** Record it for the commercial track. The production route to test is admin deploy (T-14). |
| P1 Q3 | Third-party plugins don't work in Word | L21, L25 | ✅ consistent. Add: Word still **lists** our plugin and skills, and that is a user-facing hazard (S17). |
| P1 Q4 | One playbook per review run; a `.docx` library by speciality | Protocol §2 P2 (one playbook per agreement type) | ✅ consistent |
| P1 Q5 | "Draft precedent playbook": UI undocumented | S6 (`create-playbook`) not yet run | ❓ Keep as S6 |
| **P1 Q6** | "Whether Cowork executes bundled Python is undocumented. Deterministic logic lives in the Spaarke MCP server (BFF); bundled scripts are never load-bearing." | ✅ Python runs (A.2). Design §14: Spaarke hosts nothing at T0/T1; SC-15: no `src/server/**` change in R1 | ✗ **Rejected.** It contradicts verified evidence and R1's no-hosting premise. A BFF-hosted engine is the T2 connector path (§10), not R1. Verify item 4 is answered. |
| P1 Q7, P2 Q6 | "Business skills" are a Dynamics/Power Platform label, not a Cowork mechanism | — | ✅ Agree. Nothing to change. |
| P1 Q8, Q9 | Work IQ MCP for the BFF; SharePoint Embedded working storage | §10: SPE returns 403 to Cowork's Graph access | Strategy input for the connector project. Not R1. |
| P1 Q10–12, P2 Q7 | Foundry IQ for department-scale retrieval, through a published agent | Strategy §6 (T2/T3) | Not R1. Consistent with the strategy. |
| **P1 Q13, P2 Q4** | The declarative agent is the vehicle for Word; package every surface in one M365 app | Owner directive 2026-11-02: no declarative agents in the package. S18 never run. | ✗ **Conflicts with an owner decision.** Its document-interaction claims are untested. Keep it as a post-R1 option only. |
| P2 Q2 | Copilot Chat ≠ in-app Copilot | — | ✅ Useful terminology. Use it in the guides. |
| P2 Q3 | Two deliverables from one tree (plugin + folder drop); playbooks aren't skills | S1, T-02 ✅; protocol §1 rule 2 | ✅ consistent. Add L22 and L23 (folder-skill quirks). |
| **P2 Q5** | The playbook "save" UI is undocumented | L24 ✅: Word desktop saves an editable playbook object; the import drops the Topic ID and the fallback | ✗ **Behind our evidence.** Saving exists. Sharing and reuse are T-P4. |
| Verify list | 1 precedent playbook · 2 saved playbook · 3 declarative agent in Word · 4 scripts · 5 partner-plugin default | 1 = S6 open · 2 = T-P4 open · 3 = out of scope · 4 = ✅ answered · 5 = new, for T-14 | — |
| Sources | Several community or third-party sites (WindowsForum, aiswitch.co.kr, aguidetocloud, merill.net mirrors) | — | Fine for orientation. Runbook claims need a Learn page or tenant evidence. |
