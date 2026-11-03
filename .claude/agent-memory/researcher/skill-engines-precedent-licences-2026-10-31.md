---
name: skill-engines-precedent-licences-2026-10-31
description: Anthropic docx/xlsx/pptx/pdf skills are PROPRIETARY (patterns only) and ship validators not a redline generator; 'Solve, don't defer'; Legal for Cowork edit-contract makes native tracked changes; Cowork Word skill tracked changes/bookmarks/formulas undocumented; copilot-camp ships no scripts; OSS licences (lexdays AGPL, docx-redline MPL Py>=3.12, python-redlines .NET); date/arith benchmarks; engine-by-engine verdict
metadata:
  type: reference
---

# Should legal Agent Skills ship deterministic scripts? Precedent, Cowork built-ins, OSS, evidence (2026-10-31)

Tags: **Cited** = primary source says it · **Secondary** = credible non-primary (news, vendor blog, agent self-report) · **Inferred** = follows from cited facts, not stated · **Unknown** = no source found.

Prior related research (researcher memory): cowork-skill-runtime-and-mvp-claims-2026-10-30, cowork-spe-playbooks-automation-2026-10-30, copilot-for-legal-mechanics-walkthrough-2026-10-29, adeu-architecture-study-2026-06-29, openxml-docx-compose-r2-2026-06-29. This note adds the Anthropic-skills precedent, licence check, OSS inventory and benchmark evidence.

## Summary

1. The "mechanics in code" rule has a direct, documented precedent: Anthropic's production document skills (docx/xlsx/pptx/pdf) ship ~11.4k lines of Python helpers and validators, and Anthropic's authoring guidance says "Prefer scripts for deterministic operations", "Solve, don't defer" and "Provide utility scripts ... more reliable than generated code" (Cited). Microsoft's Cowork docs show the same `scripts/` convention ("Executed, not loaded into context") but Microsoft's own Cowork samples ship no scripts (Cited).
2. Anthropic's docx skill does **not** ship a redline *generator*. The model hand-edits `word/document.xml`; the bundled code is a run-merger, a comment helper, an XSD + untracked-edit **validator**, and a LibreOffice accept-all. There is no bookmark or hyperlink helper (Cited). The four document skills are **proprietary, source-available** — copying or deriving from them is prohibited (Cited). Patterns may be reimplemented; code may not be copied.
3. Cowork's built-in Word skill is documented only as "Create and edit Word documents". Nothing on Learn says Cowork's Word skill makes tracked changes, comments, bookmarks or hyperlinks, or that its Excel skill writes formulas (Cited absence). Legal for Cowork's `edit-contract` **does** produce "negotiation-ready redlines with tracked changes" and works inside documents that already contain tracked changes (Cited). Copilot in Word (not Cowork) gained word-level Track Changes and comment management, GA June–July 2026 (Cited, MC1384426).
4. For a single-document playbook review in Word, a custom redline engine is redundant with Microsoft's plugin. It is **not** redundant for: verification that every change is tracked, margin comments tied to a plan, bookmarks + page-1 hyperlinked summary, a clean copy, batch/multi-document runs, arithmetic, date math, LEDES parsing, claim checks, Excel formulas — none of which Cowork documents as built-in.
5. No OSS redliner is stdlib-only. The best fit is `docx-redline` (MPL-2.0, lxml + python-docx ≥1.2, Python ≥3.12, 0 stars, single maintainer). `python-redlines` (MIT) needs .NET binaries; `adeu` (MIT) pulls fastmcp/pydantic/rapidfuzz; `lexdays` (deadline calculator) is AGPL-3.0 — do not vendor. `holidays` (MIT, dateutil only) is the only broadly maintained date library. Keep our own lxml-only engines; borrow patterns and test fixtures, not code.
6. Benchmarks support "mechanics in code" for dates and arithmetic (Test of Time: 13.5–16% on duration questions; "most common error ... deviation of precisely one day"; Date Fragments: up to −10 points on uncommon dates; Faith and Fate on multi-digit multiplication) and for legal content (58–88% hallucination on case questions; 17–33% even in Lexis/Westlaw RAG tools). No benchmark covers tracked-change XML; the evidence there is Anthropic's own validator docstring ("easy to do by accident and invisible in the accepted view"). The benchmarked models are 1–2 generations older than Cowork's Opus 5.5 / GPT 6.1 — the direction holds, the magnitude is unknown.

---

## Q1. Precedent in the Agent Skills ecosystem

**Repository state.** `github.com/anthropics/skills` HEAD `ca45fc496a3436251f191354091d557da31c15b5` (2026-10-28 12:20 -0700). Document skills live under `skills/docx`, `skills/xlsx`, `skills/pptx`, `skills/pdf`. Cited.

**Licence.** README: "Many skills in this repo are open source (Apache 2.0). We've also included the document creation & editing skills that power Claude's document capabilities ... These are source-available, not open source." Each of `skills/{docx,xlsx,pptx,pdf}/LICENSE.txt`: "© 2025 Anthropic, PBC. All rights reserved ... users may not: Extract these materials from the Services or retain copies ... Reproduce or copy ... Create derivative works ... Distribute, sublicense, or transfer". `skills/skill-creator/LICENSE.txt` is Apache-2.0. Cited. **Consequence: we cannot borrow Anthropic's docx scripts, templates or XSD bundle; we can read them for patterns.**

**What the document skills ship (exact paths, line counts).** Cited.

| Skill | Scripts | Purpose |
|---|---|---|
| docx | `scripts/merge_runs.py` (310) | Coalesce fragmented `<w:r>` runs so text is findable; never merges across `w:ins`/`w:del` |
| | `scripts/comment.py` (368) + `scripts/templates/{comments,commentsExtended,commentsIds,commentsExtensible,people}.xml` | Writes the six cross-linked comment parts; prints the range/reference markers the model must place |
| | `scripts/accept_changes.py` (135) | Clean copy via a LibreOffice StarBasic macro (`AcceptAllTrackedChanges`) |
| | `scripts/office/validate.py` (173), `validators/base.py` (875), `validators/docx.py` (466), `validators/redlining.py` (299), `validators/pptx.py` (441), `office/schemas/**/*.xsd` (ECMA-376 + MS wml-2010/2012/2018 schemas) | XSD validation, auto-repair (paraId overflow, `xml:space`), and **untracked-edit detection** (`--original --author`) |
| | `scripts/office/soffice.py` (192) | LibreOffice wrapper ("bare `soffice` hangs in this sandbox") |
| xlsx | `scripts/recalc.py` (308) | LibreOffice recalculation; JSON `status`, `total_errors`, `error_summary` |
| pptx | `scripts/add_slide.py` (367), `clean.py` (309), `thumbnail.py` (311) + shared `office/` | Slide duplication with package bookkeeping; orphan cleanup; visual grid |
| pdf | 8 scripts (11–122 lines): `extract_form_field_info.py`, `extract_form_structure.py`, `fill_fillable_fields.py`, `fill_pdf_form_with_annotations.py`, `check_bounding_boxes.py`, `check_fillable_fields.py`, `convert_pdf_to_images.py`, `create_validation_image.py` | Analyze → plan → validate → fill → verify |

The `office/` tree (helpers, validators, 45 XSDs) is **duplicated verbatim in docx, xlsx and pptx** — Anthropic accepts duplication per skill rather than a shared package. Total script lines across the four skills: 11,445. Cited.

**Third-party imports used by those scripts:** `lxml.etree`, `defusedxml`, `pypdf`, `pdfplumber`, `PIL`, `openpyxl`; plus external binaries `soffice`, `pandoc`, `pdftoppm`, and npm `docx`/`pptxgenjs`. Stdlib: `zipfile`, `re`, `tempfile`, `subprocess`, `argparse`, `json`. Cited.

**How Anthropic's docx skill does tracked changes (SKILL.md, verbatim):**
- "**Edit** an existing document | `unzip` → edit `word/document.xml` → `zip`"
- "`python scripts/merge_runs.py unpacked/   # coalesce fragmented runs so text is findable`"
- "`python scripts/office/validate.py out.docx --original doc.docx   # XSD checks` ... `# redlining? add --author "<the name you redlined under>" to check every edit is tracked`"
- "**Tracked changes:** when redlining, validate with `--author` ... it reports any text you changed without a `<w:ins>`/`<w:del>` around it, which is easy to do by accident and invisible in the accepted view."
- "To produce a clean copy with all tracked changes accepted: `python scripts/accept_changes.py in.docx out.docx`."
- "Comments require six cross-linked files. Use the helper".
- "After writing a `.docx`, render it and look at it: `soffice ... --convert-to pdf` / `pdftoppm -jpeg -r 100`".
- `grep -i 'hyperlink\|bookmark' docx/SKILL.md docx/scripts/*.py` → **no matches**. Bookmarks/internal hyperlinks are raw XML the model writes itself.

So the Anthropic precedent is **helpers + validators + render-and-look**, with the model doing the XML edit. Our `ooxml_redline.py` (plan → apply) is a *stricter* design; Anthropic's guidance supports that too (below). Inferred.

**xlsx rules (verbatim):** "**Zero formula errors.** Never ship while `recalc.py` reports `errors_found`." "**Use formulas, never hardcoded results.** Write `sheet['B10'] = '=SUM(B2:B9)'`, not the Python-computed total." "A green recalc proves your formulas *evaluate*, not that they are *right*." Cited.

**Documented rationale.**
- Anthropic engineering, "Equipping agents for the real world with Agent Skills" (2025-10-16): "Large language models excel at many tasks, but certain operations are better suited for traditional code execution." "Beyond efficiency concerns, many applications require the deterministic reliability that only code can provide." "Claude can run this script without loading either the script or the PDF into context." "because code is deterministic, this workflow is consistent and repeatable." Cited. The post does not use "solve, don't punt" or "utility scripts".
- platform.claude.com "Skill authoring best practices" (fetched 2026-10-31): heading **"Solve, don't defer"** — "handle error conditions rather than deferring to Claude"; **"Provide utility scripts"** — "Even if Claude could write a script, pre-made scripts offer advantages: Are more reliable than generated code; Save tokens; Save time; Ensure consistency across uses"; **"Prefer scripts for deterministic operations:** Write `validate_form.py` rather than asking Claude to generate validation code."; **"Create verifiable intermediate outputs"** — plan-validate-execute, "When to use: Batch operations, destructive changes, complex validation rules, high-stakes operations."; low-freedom scripts "when operations are fragile and error-prone, consistency is critical". Also: "**Claude API:** Has no network access and no runtime package installation"; "Don't assume packages are available". Cited. **Correction: Anthropic's phrase is "Solve, don't defer", not "solve, don't punt".**
- `skills/skill-creator/SKILL.md`: "`scripts/` - Executable code for deterministic/repetitive tasks"; "If all 3 test cases resulted in the subagent writing a `create_docx.py` or a `build_chart.py`, that's a strong signal the skill should bundle that script."; "For assertions that can be checked programmatically, write and run a script rather than eyeballing it — scripts are faster, more reliable, and can be reused". Cited.
- agentskills.io specification: `scripts/` "Contains executable code that agents can run. Scripts should: Be self-contained or clearly document dependencies; Include helpful error messages; Handle edge cases gracefully. Supported languages depend on the agent implementation." Best practices: "Validation loops", "Plan-validate-execute", "Bundling reusable scripts ... If you notice the agent independently reinventing the same logic each run ... write a tested script once". Using-scripts page: non-interactive, `--help`, structured stdout/diagnostics on stderr, idempotency, `--dry-run`, meaningful exit codes, predictable output size. Cited.

**Microsoft-side precedent.** cowork-plugin-development (ms.date 2026-10-02, updated 2026-10-21): example tree `scripts/ # Executable utilities` → `extract-clauses.py`; layering table "Scripts (`scripts/`) | Executed, not loaded into context"; "Skills use the Agent Skills open standard—the same format supported by Claude Code ...". Claude-plugin conversion: `skills/*/SKILL.md` "Copied verbatim - identical format"; `bin/` executables "Not applicable"; `hooks/`, `agents/`, `commands/` "Not yet supported". Cited. But `microsoft/copilot-camp` `src/cowork` at `b5b1f8a1e488713063b46883d5f56d3442607155` (2026-10-28) contains only `SKILL.md` + manifests — **zero `.py/.js` files** across its three Cowork samples. Cited. Whether Legal for Cowork's 20 skills bundle scripts: Unknown (package not public).

## Q2. Microsoft Cowork built-ins

| Question | Answer | Tag | Source |
|---|---|---|---|
| What the Word/Excel/PowerPoint/PDF skills do | One-liners only: "Create and edit Word documents", "Create and edit Excel spreadsheets", "Create and edit PowerPoint presentations", "Work with PDF documents". | Cited | use-cowork (2026-09-14 / upd 2026-10-21) |
| Native tracked changes from the built-in Word skill | Not documented anywhere on Learn. | Cited absence / Unknown | use-cowork, cowork-faq, whats-new (all re-read 2026-10-31) |
| Native tracked changes from Legal for Cowork | Yes: "Tell Copilot what to change and it produces negotiation-ready redlines with tracked changes across relevant sections" (`edit-contract`); "Copilot works inside documents that already contain tracked changes, separating prior revisions from new proposals"; `summarize-redlines`: "Explain each tracked change, its impact, and who benefits." Whether changes are word-level or paragraph-level is not stated. | Cited / Unknown on granularity | support.microsoft.com Copilot for Legal transparency doc (undated); cowork-available-plugins (upd 2026-10-29) |
| Comments from Cowork | Not documented for Cowork. Word Legal Agent (Frontier, April 2026) "keeps tracked changes and can also insert comments explaining the changes." | Unknown (Cowork) / Secondary (Word agent) | techcommunity 4516218 |
| Bookmarks / internal hyperlinks | Not documented. POC agent self-report: "The in-place live editing surface in Cowork does not support hyperlinks or bookmarks". | Unknown / Secondary (agent self-report) | repo POC note §2.5 |
| Excel skill writes formulas | Not documented for Cowork. Copilot in Excel Agent Mode (not Cowork) generates formulas. If Cowork's Excel skill derives from Anthropic's xlsx skill, formulas + recalc are mandatory there. | Unknown / Inferred | search 2026-10-31; anthropics/skills xlsx/SKILL.md |
| Does Cowork run Python internally | Learn: "Some background operations, like script execution, run without displaying individual steps."; static checks "scan any bundled code for security issues ... Code is never executed during static checks." POC agent self-report: Python + Node.js, lxml, pandoc, LibreOffice preinstalled; "tool scripts" call M365 tools. **"Tool scripts" appears nowhere on Learn.** | Cited (execution exists) / Secondary (runtime details) / Unknown ("tool scripts") | use-cowork; POC note §3 Q8 |
| Edit-in-place semantics | "Cowork can edit an existing Word, Excel, or PowerPoint file stored in OneDrive or SharePoint. Cowork saves the changes to the same shared file." Version history preserved (Sept 2026). Nothing on tracked changes. | Cited | use-cowork; whats-new |
| Is Cowork's Word skill derived from Anthropic's docx skill | Microsoft: "Working closely with Anthropic, we have integrated the technology behind Claude Cowork into Microsoft 365 Copilot." (2026-03-09). FAQ: "Cowork can use Anthropic Claude models as a subprocessor for most reasoning, drafting, and tool-using work." Skills use the Agent Skills format. The POC-reported preinstalled stack (lxml, pandoc, LibreOffice) matches the Anthropic docx skill's dependency list (`pandoc` · LibreOffice · `pdftoppm`). No public statement about the built-in skills' provenance. | Cited (technology) / Inferred (skills) / Unknown (statement) | microsoft.com/microsoft-365/blog 2026-03-09; cowork-faq; cowork-plugin-development |
| Models | Auto, GPT 6.1 Sol, GPT 6 Astra, GPT 5.6 Terra, GPT 5.5, Claude Opus 5.5, Claude Sonnet 5.5, Claude Fable 5.1 (Frontier), Grok 4.7. "If your organization is within the EU Data Boundary, only GPT models are supported." Admins can switch the Anthropic family off tenant-wide. | Cited | cowork-models (upd 2026-10-24) |

Implication of the last row (Inferred): a skill that relies on the model hand-writing OOXML will behave differently under GPT-only or Grok tenants. Deterministic scripts make the output model-independent — an argument for engines that Microsoft's multi-model picker creates and Anthropic's single-model world did not have.

## Q3. Word legal plugin and where a script is still needed

- Word plugin: suggested edits "inserted as tracked changes (Accept / Accept All)"; playbook must be a `.docx`; "Copilot can currently only perform contract reviews from playbooks." Cited (prior memory, support.microsoft.com get-started page).
- Copilot in Word generally: "support complex edits like tracked changes, comments, and document structure"; "creating, reading, replying to, and managing comments anchored to document text"; GA "early June 2026 ... early July 2026", Windows + Mac. Cited (MC1384426, 2026-06-08).
- Legal for Cowork `edit-contract`: tracked changes (above). `extract-obligations-and-deadlines`: "Create a tracker of obligations and key dates" — format unstated; no claim of date arithmetic. Transparency doc: "Copilot is not jurisdiction-aware by default"; "In complex or lengthy documents, Copilot may miss relevant clauses or misapply playbook guidance"; calculations not addressed. Cited.

**Verdict.** For "review this contract against a playbook and redline it, one document, in Word": the Microsoft plugin already does the redline; a custom engine adds nothing the attorney sees and costs us maintenance. For the Cowork output-file path (files produced into OneDrive/Cowork, not edited in place), and for anything below, a script is still the only documented route:

| Need | Built-in? | Script needed because |
|---|---|---|
| Every edit is tracked, nothing untracked slipped in | No | Anthropic's own validator exists precisely because untracked edits are "easy to do by accident and invisible in the accepted view" |
| Margin comment per change, bound to the plan row | Not for Cowork | Six cross-linked parts; ordering rules (comments before revisions) |
| Bookmarks + page-1 hyperlinked summary | Not documented | POC needed raw `w:bookmarkStart`/`w:hyperlink w:anchor` |
| Clean copy (accept all) | Not documented for Cowork | Anthropic uses LibreOffice; pure-Python accept must also join deleted paragraph marks |
| Multi-document / batch, unattended automations | No | Automations run "with your permissions" but each doc's mechanics still need determinism |
| Counts, residue scan, contrast | No | Plan-validate-execute |
| Notice windows, critical dates, business-day rules | No (partner plugin "Calculate Working Day" exists; FRCP-style rules not in Microsoft skills) | ToT duration/schedule accuracy 13–44% (older models) |
| Rent steps, invoice arithmetic, Excel formulas | Not documented | Anthropic: "Use formulas, never hardcoded results" |
| LEDES 1998B parse, UTBMS validation | No | Pipe-delimited fixed spec; a parser is trivial and exact |
| Claim numbering, dependency, antecedent basis | No | Regex-grade problem; model misses are silent |

## Q4. Open-source building blocks (licence, maintenance, sandbox fit)

Sandbox assumption (from prior research): only Python + lxml confirmed by agent self-report; Learn documents no interpreter, packages, or egress. Declarative-agent sandbox (the only documented one): "no network, no package installs, Don't depend on a package unless its availability is confirmed."

| Library | What | Licence | Latest | Deps / Python | Sandbox fit | Tag |
|---|---|---|---|---|---|---|
| `python-redlines` (JSv4) | Compare two .docx → tracked-change redline | MIT | 2.0.0, 2026-10-24 | Engines are "compiled .NET binaries shipped in separate, optional companion packages" (`[docxodus]`, `[ooxmlpowertools]`); Python ≥3.9 | Poor: needs .NET binaries; compare-based, not plan-based | Cited |
| `docx-redline` (gh-aswanth/docx_walking) | Plan-based ins/del/moves/table rows, comments, `rPrChange`/`pPrChange`, accept/reject by id/author | MPL-2.0 | 1.0.3, 2026-09-17 | `lxml`, `python-docx ≥1.2`; **Python ≥3.12**; 0 stars, 26 commits, single maintainer; no bookmarks/hyperlinks | Medium: needs python-docx 1.2 and Py 3.12 in sandbox (unconfirmed); MPL file-level copyleft is workable | Cited |
| `docx-revisions` (balalofernandez) | Extends python-docx: tracked ins/del, accept_all/reject_all | MIT | 0.1.5, 2026-04-14 | python-docx; Py ≥3.10; no comments | Medium-low | Cited |
| `adeu` (Dealfluence Oy) | CriticMarkup-for-LLM redline engine + MCP server | MIT | 3.0.7 on PyPI (2026) | `lxml`, `python-docx`, `fastmcp`, `pydantic`, `rapidfuzz`, `regex`, `diff-match-patch`, `jinja2`, `structlog`; Py ≥3.12 | Poor as a dependency (heavy); excellent as a pattern source (prior memory: read-CriticMarkup/write-structured asymmetry, reverse-order apply, match_mode errors) | Cited |
| `python-docx` | Base library | MIT | 1.2.x | No tracked-changes API; issue #340 open since 2016; PRs #1534/#1538 unmerged as of 2026-07 | N/A | Secondary (vaquill blog 2026-07; usejunior conformance 2026-06-11) |
| Open XML SDK 3.x / `Codeuctivity.OpenXmlPowerTools` / docx4j | .NET and Java OOXML stacks with first-class revisions | MIT / MIT / Apache-2.0 | 3.5.1 (2026-03) | .NET / JVM | Not runnable in a Python sandbox; relevant to the BFF, not to skills | Cited (prior memory 2026-06-29) |
| `ledes-parser` (travisbashor) | LEDES 1998B → dict/Invoice | MIT | 1.5.1, **2024-07-23** | 2 deps (grammar-based); 1998B only; "DO NOT RELY ON THIS FOR YOUR LEGAL INVOICING" | Poor: stale, WIP disclaimer; 1998B is pipe-delimited and trivially parsed with `csv` | Cited (libraries.io) |
| `ledestool` (karlrees) | 1998B → 1998BI converter | Unknown | — | — | Reference only | Secondary |
| UTBMS code tables | L100–L500, A101–A111, E101–E124 etc. | Maintained by LEDES Oversight Committee (ledes.org); **no official JSON/CSV found; reuse terms Unknown** | — | — | Build our own table from ledes.org after checking terms; Clio exposes codes via API (vendor) | Cited (Wikipedia) / Unknown (licence) |
| `holidays` (vacanza) | 250 country codes, US states, financial markets | MIT | 0.107 (2026) | `python-dateutil` only; Py ≥3.10 | Medium: one non-stdlib dep; large; data could instead be exported to a JSON asset for the jurisdictions we support | Cited (PyPI JSON, GitHub) |
| `lexdays` (DoGood-Studio) | Court deadlines: FRCP 6, CPR 2.8, ZPO, Turkish codes; prints steps with citations; "dates outside the verified holiday data raise an error instead of guessing" | **AGPL-3.0-only** | 0.2.1, 2026-10-21 | Zero deps; Py ≥3.10 | **Do not vendor** (AGPL in a distributed skill); copy the *behaviour* (fail on unknown data, cite the rule) | Cited |
| `robthepcguy/claude-patent-creator` | `mcp_server/claims_analyzer.py`: 35 USC 112(b) "definiteness, antecedent basis, structure" | MIT | 206 stars; 95 commits | Deps not stated (clone failed) | Reference for checks; no standalone PyPI claim-dependency parser exists; USPTO's own methodology used an unpublished Python classifier | Secondary / Unknown |
| Lease / rent-schedule calculators | — | — | — | — | None found; stdlib `decimal` + our own tables | Unknown |
| Cowork partner plugins as alternatives | "Calculate Working Day — Compute valid working days, allowing for holidays and weekends"; "Wolfram Research — Compute answers, run calculations"; "Encodian - Word — Create, populate and manipulate Microsoft Word documents"; "Encodian - Excel" | Vendor terms | — | Require admin to make available; per-user sign-in | Viable for T2 tenants; not for a T0 skill-only kit | Cited (cowork-available-plugins) |

## Q5. Evidence that LLMs are unreliable at these mechanics

| Mechanic | Evidence | Figures | Tag |
|---|---|---|---|
| Date arithmetic | Test of Time (Fatemi et al., ICLR 2025; arXiv 2406.09170) — Claude-3-Sonnet / GPT-4 / Gemini 1.5 Pro | Duration 15.0 / 16.0 / 13.5 %; Schedule 29.6 / 43.6 / 40.0 %; MultiOp 26.6 / 46.9 / 62.6 %; AddSubtract 58.6 / 76.3 / 71.1 %. "the most common error among incorrect answers was a deviation of precisely one day"; "LLMs can approximate the correct calculation but often stumble in the final steps"; leap-year errors. | Cited |
| Date tokenization | Date Fragments (Bhatia et al., EMNLP 2025; arXiv 2505.16088) | "excessive fragmentation correlates with accuracy drops of up to 10 points on uncommon dates like historical and futuristic dates" | Cited |
| Date reasoning bias | DateLogicQA (arXiv 2412.13377, v2 2025-05) | representation-level and logical-level biases across 12 LLMs | Cited |
| Arithmetic | Faith and Fate (Dziri et al., 2023; arXiv 2305.18654) | multi-digit multiplication; "performance can rapidly decay with increased task complexity" | Cited |
| Arithmetic | GSM-Symbolic (Mirzadeh et al., 2024; arXiv 2410.05229) | "performance of all models declines when only the numerical values in the question are altered" — **contested** by arXiv 2605.28700 (2026-05): few statistically significant drops among 20 open-weight models | Cited (both) |
| Arithmetic | "Language Models are Symbolic Learners in Arithmetic" (TMLR 2026; arXiv 2410.15580) | U-shaped digit accuracy: first/last digits mastered, middle digits fail | Secondary |
| Legal content | Large Legal Fictions (Dahl et al., J. Legal Analysis 2024; arXiv 2401.01301) | hallucinations "between 58% of the time with ChatGPT 4 and 88% with Llama 2" on verifiable federal-case questions | Cited |
| Legal RAG tools | Hallucination-Free? (Magesh et al., 2024; arXiv 2405.20362) | Lexis+ AI, Westlaw AI-AR, Ask Practical Law AI "between 17% and 33% of the time" | Cited |
| Verbatim text spans | ACL-Verbatim (arXiv 2605.21102, 2026) | 150M ModernBERT token classifier word-level F1 53.6 vs best LLM extractor 48.7; LLMs "higher recall but ... lower precision" | Cited |
| Verbatim misquotes in legal citations | LePhantomCite (arXiv 2606.21155) | includes a "verbatim misquote" category | Secondary |
| Tracked-change XML | No benchmark. Practice evidence: Anthropic `validators/redlining.py` docstring — "Detects untracked edits ... text that differs from the original without a `<w:ins>`/`<w:del>` wrapper"; SKILL.md — "easy to do by accident and invisible in the accepted view"; `merge_runs.py` — "the string you're looking for is split across runs". Microsoft transparency doc — "may miss relevant clauses or misapply playbook guidance". | — | Cited (practice) / Unknown (benchmark) |

Caveat: every benchmark above tested models one to two generations behind Cowork's current Opus 5.5 / GPT 6.1 / Fable 5.1. The direction (dates, multi-step arithmetic, exact spans degrade; one-day-off errors dominate) is consistent; the current magnitude is Unknown. Anthropic's production skills still mandate code for these operations with current models, which is the strongest available signal.

## Q6. Recommendation

### Engine-by-engine verdict

| Engine | What it does | Microsoft built-in alternative | Precedent | Verdict | Why |
|---|---|---|---|---|---|
| `segment_clauses.py` | Section map `{number, heading, para_key, text}` | None documented | Anthropic `merge_runs.py` (make text findable before editing); `pandoc -t markdown` for reading | **Keep** | Cheap, deterministic, saves tokens, and it is the anchor source for bookmarks and the plan |
| `ooxml_redline.py` | Edit plan → surgical `w:ins`/`w:del`, comments, bookmarks; clean copy | Legal for Cowork `edit-contract` (tracked changes, Cited); Word plugin Run Review (tracked changes, Cited); Copilot in Word comments (Cited) | Anthropic ships helpers + validator, not a generator; `docx-redline` is the closest OSS generator; adeu patterns | **Keep, but reposition** | Redundant for single-doc in-Word review — route that case to the Microsoft plugin and say so in the skill. Required for the Cowork output-file path, plan-bound comments, bookmarks, clean copy, and model-independence. Do not borrow `docx-redline` until spike S2 confirms Py ≥3.12 + python-docx ≥1.2 in the sandbox; its MPL-2.0 would be acceptable. Reimplement (do not copy) Anthropic's untracked-edit check as part of `check_outputs.py`. |
| `summary_table.py` | Page-1 summary with bookmark hyperlinks, contrast-safe colours | None documented; agent self-report says in-place editing cannot do hyperlinks/bookmarks | Anthropic docx skill has no helper (raw XML) | **Keep** | Only route to the deliverable the attorney asked for; pure lxml |
| `check_outputs.py` | Counts (edits = rows = comments), residue, contrast; package sanity | None | Strongest precedent: `office/validate.py`, `validators/redlining.py`, `recalc.py`, "validation loops", "plan-validate-execute" | **Keep and extend** | Add untracked-edit detection (diff original vs. revisions-undone) and the render-and-look step when `soffice`/`pdftoppm` exist; skip XSD bundling (ECMA schemas exceed the 20-file / 10 MB companion cap) |
| `playbook_parse.py` | Markdown playbook → JSON topics; rejects unknown IDs | Microsoft playbook = user `.docx` split into topics by the model; `create-playbook` drafts one | Spec guidance "Reject ambiguous input with a clear error ... enums and closed sets" | **Keep, scope as a format contract** | Its value is version control and closed topic IDs, not LLM reliability. Consider also emitting a `.docx` playbook so the same positions feed the Word plugin |
| `calc_dates.py` | Notice windows, critical dates, business-day rules | Partner plugin "Calculate Working Day" (T2 only); no Microsoft legal skill computes dates | ToT duration 13–16 %; lexdays behaviour (fail on unknown holiday data, cite rule) | **Keep; stdlib `datetime` + JSON holiday asset** | Do not vendor `lexdays` (AGPL). `holidays` (MIT) only if dateutil is confirmed; otherwise export the needed jurisdictions to `assets/holidays-<juris>.json` with source + date |
| `calc_amounts.py` | Rent steps, invoice arithmetic; formulas into Excel | Not documented for Cowork; Copilot in Excel (not Cowork) writes formulas | Anthropic xlsx: "Use formulas, never hardcoded results", `recalc.py` | **Keep; `decimal`; write `<f>` formulas** | If `openpyxl` is absent, write the sheet XML with `zipfile` and set `fullCalcOnLoad`; never ship model-computed totals as values |
| LEDES parser (Wave 2) | 1998B parse + UTBMS validation | None | `ledes-parser` is stale (2024) with a do-not-rely disclaimer | **Build, stdlib `csv`** | 1998B is pipe-delimited with a fixed header; UTBMS table as `assets/utbms.json` after checking ledes.org reuse terms |
| Claim parser (Wave 2) | Numbering, dependency graph, antecedent basis | None | MIT `claims_analyzer.py` as a reference for checks | **Build, stdlib `re`** | Regex on "of claim N"/"according to claim N"; "a/an" before "the/said" |

### Risk if Cowork's sandbox libraries differ
- Only Learn-documented facts: scripts are executed; static scan only; 20 companion files / 5 MB each / 10 MB total; 15 s download. Interpreter, Python version, packages, egress: **undocumented**. The POC's lxml/pandoc/LibreOffice list is an agent self-report.
- Mitigations: (1) ship `scripts/probe.py` and run it first (`sys.version`, imports for `lxml`, `docx`, `openpyxl`, `shutil.which("soffice")`, `which("pdftoppm")`); (2) `lxml` import guarded with a stop-and-report fallback — do **not** fall back to `xml.etree` for writing OOXML (ElementTree rewrites namespace prefixes, which breaks `mc:Ignorable` and Word's tolerance); (3) every engine stdlib-first, `lxml` second, nothing else; (4) each skill carries its own copy of the engines (Anthropic duplicates `office/` per skill for the same reason) and stays within 20 files / 10 MB — 7 engines + probe + fixtures fits if fixtures live in tests outside the shipped skill; (5) Python ≥3.12-only libraries (`docx-redline`, `adeu`) are a hard dependency risk until the probe confirms the version.

## Corrections to our assumptions
1. The Anthropic phrase is **"Solve, don't defer"** (platform.claude.com best practices), not "solve, don't punt". "Utility scripts" is a heading on that page, not in the engineering blog post.
2. Anthropic's docx skill ships **no redline generator**; it ships `merge_runs`, `comment.py`, a validator with untracked-edit detection, and a LibreOffice accept-all. The model edits XML directly. Our generator is the stricter "plan-validate-execute" variant Anthropic recommends for "batch operations, destructive changes, ... high-stakes operations".
3. Anthropic's docx/xlsx/pptx/pdf skills are **proprietary** ("source-available, not open source"; no copying, derivatives or distribution). Borrow patterns, not code. Only `skill-creator` and the example skills are Apache-2.0.
4. Microsoft documents the `scripts/` convention but ships **no scripts** in its own Cowork samples; "tool scripts" is not a documented term. Script runtime remains undocumented.
5. Legal for Cowork `edit-contract` **does** create native tracked changes, and the Word plugin does too. A redline engine is not what makes tracked changes possible; it is what makes them surgical, plan-bound, commented, bookmarked, verifiable and model-independent.
6. `lexdays` is **AGPL-3.0** — do not vendor into a distributed skill. `python-redlines` needs .NET binaries. `docx-redline` needs Python ≥3.12 — unconfirmed in Cowork.
7. Cowork's multi-model picker (GPT 6.1 / Opus 5.5 / Grok 4.7; EU tenants GPT-only) is an argument for engines that did not exist in the single-model Claude world: hand-written OOXML quality will vary by model; script output will not.
8. ECMA XSD bundles (Anthropic's approach to validation) do not fit Cowork's 20-file / 10 MB companion cap; use structural checks and render-and-look instead.

## Reference links
- https://github.com/anthropics/skills (HEAD ca45fc496a3436251f191354091d557da31c15b5, 2026-10-28) — `skills/docx/SKILL.md`, `skills/docx/LICENSE.txt`, `skills/docx/scripts/office/validators/redlining.py`, `skills/xlsx/SKILL.md`, `skills/skill-creator/SKILL.md`, `README.md`
- https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills (2025-10-16)
- https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices (fetched 2026-10-31)
- https://agentskills.io/specification · https://agentskills.io/skill-creation/best-practices · https://agentskills.io/skill-creation/using-scripts
- https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (ms.date 2026-09-14, updated 2026-10-21)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-10-02 / 2026-10-21)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-available-plugins (2026-10-26 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-models (2026-09-14 / 2026-10-24)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq (2026-09-29 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/whats-new (2026-10-28 / 2026-10-29)
- https://support.microsoft.com/en-us/word/copilot-for-legal-transparency-documentation (undated)
- https://www.microsoft.com/en-us/microsoft-365/blog/2026/03/09/copilot-cowork-a-new-way-of-getting-work-done/ (2026-03-09)
- https://mc.merill.net/message/MC1384426 (2026-06-08) · https://techcommunity.microsoft.com/blog/microsoft365copilotblog/copilot-in-word-new-capabilities-for-document-workflows/4508974 (2026-04) · https://techcommunity.microsoft.com/blog/Microsoft365CopilotBlog/word-legal-agent-in-frontier/4516218
- https://github.com/microsoft/copilot-camp/tree/main/src/cowork (b5b1f8a1e488713063b46883d5f56d3442607155, 2026-10-28)
- PyPI: python-redlines 2.0.0 · docx-redline 1.0.3 (https://github.com/gh-aswanth/docx_walking) · docx-revisions 0.1.5 · adeu 3.0.7 (https://github.com/dealfluence/adeu) · holidays 0.107 (https://github.com/vacanza/holidays) · lexdays 0.2.1 (https://github.com/DoGood-Studio/legal-deadline-calculator) · ledes-parser 1.5.1 via https://libraries.io/pypi/ledes-parser
- https://github.com/robthepcguy/claude-patent-creator · https://github.com/karlrees/ledestool
- arXiv 2406.09170 (Test of Time) · 2505.16088 (Date Fragments) · 2412.13377 (DateLogicQA) · 2305.18654 (Faith and Fate) · 2410.05229 (GSM-Symbolic) · 2605.28700 (GSM-Symbolic re-evaluation) · 2410.15580 · 2401.01301 (Large Legal Fictions) · 2405.20362 (Hallucination-Free?) · 2605.21102 (ACL-Verbatim) · 2606.21155 (LePhantomCite)
- Repo: `projects/spaarke-legal-ai-accelerator-r1/design.md` §5.6; `projects/spaarke-legal-ai-accelerator-r1/notes/poc-2026-10-30-cowork-nda-review.md` §2.5, §3 Q8, Appendix D/E
