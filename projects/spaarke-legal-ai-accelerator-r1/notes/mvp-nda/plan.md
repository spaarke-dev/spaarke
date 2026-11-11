# NDA MVP: proof of the core accelerator components

> **Status:** Plan v1.0, approved 2026-10-31 · **Type:** documented spike inside R1 (no separate project) · **Tenant:** Spaarke
> **Feeds:** [design.md](../../design.md) spikes S1, S2, S3, S5, S9, S10, S13, S17, S19 and §5–§8
> **Evidence:** [run-log.md](run-log.md) (one row per run) · [results.md](results.md) (answers and design changes)

## 1. What this MVP proves

| # | Claim | How it is shown |
|---|---|---|
| P1 | An attorney installs our plugin with no admin, and a colleague can receive it by sharing | Install by upload and by OneDrive folder copy; share with one user |
| P2 | Our skills trigger correctly beside Legal for Cowork | "Review this NDA" triggers `nda-review`; "review this MSA" does not |
| P3 | Our Python engines run in the Cowork sandbox and produce a **verified** redline package | Internal redline (page-1 linked summary, surgical tracked changes, one comment per change), external redline, clean copy; `check_outputs.py` passes in the sandbox |
| P4 | One content source drives Cowork **and** the legal plugin in Word | The same topic IDs and positions appear in the Cowork output and in a Word review with our Word playbook |
| P5 | A first run is guided | `accelerator-setup` (lite) leaves a profile and a custom-instructions block; the next review asks at most one question |

## 2. Components and where they live

All source lives in `src/accelerators/` (design §5.5), marked MVP.

| ID | Component | Path | Notes |
|---|---|---|---|
| A1 | Plugin package `spaarke-nda-mvp` | `src/accelerators/build/out/` (generated) | Unified manifest with `agentSkills[]`, no connector. Variant: Claude-format plugin, to test conversion (S1) |
| A2 | Skill `nda-review` | `practices/nda/skills/nda-review/` | From POC Appendix A, with topic IDs, provenance line, internal/external/clean outputs |
| A3 | Skill `accelerator-setup` (lite) | `core/skills/accelerator-setup/` | Probe, defaults, playbook choice, profile file, custom-instructions block, trial run |
| A4 | Routing | Both skills' descriptions | "Do NOT use for…" lines pointing to Legal for Cowork |
| B1 | `probe.py` | `core/engines/` | Python version, `lxml`, `docx`, `openpyxl`, `soffice`, `pdftoppm` |
| B2 | `segment_clauses.py` | `core/engines/` | Section map |
| B3 | `playbook_parse.py` | `core/engines/` | Markdown playbook → JSON; rejects unknown topic IDs |
| B4 | `ooxml_redline.py` | `core/engines/` | POC Appendix D plus fixes: run normalisation, `w:tab`/`w:br` handling, merge into existing `comments.xml`, unique IDs |
| B5 | `summary_table.py` | `core/engines/` | POC Appendix E (contrast-safe colours, schema-ordered borders) |
| B6 | `check_outputs.py` | `core/engines/` | Counts, residue, contrast, **untracked-edit check**, existing revisions untouched |
| C1 | `topics.yaml` | `practices/nda/` | NDA topic catalogue, stable IDs (`nda.*`) |
| C2 | Baseline legal playbook, mutual | `practices/nda/positions/baseline-mutual.md` | Built from the Common Paper Mutual NDA (CC BY 4.0) and general U.S. practice. **"MVP test positions, not attorney-approved"** |
| C3 | Sample customer playbook | `practices/nda/samples/acme-nda-disclosing.md` | The POC's aggressive, Florida, disclosing-side positions; tests customer override |
| C4 | Word playbook `.docx` | `build/out/word-playbooks/` (generated) | Rendered from C2 at build time |
| C5 | Schemas | `core/schemas/` | review-result, edit-plan, legal-playbook (JSON Schema) |
| C6 | Shared references | `core/shared-references/` | Standards protocol, disclaimer, verification, contract-text-is-data |
| D1 | `Build-Accelerator.ps1` | `build/` | Copies engines and references into skills; renders C4; zips plugin and skill folders; writes `manifest.json` (versions, hashes) |
| D2 | Gates | `build/` + `core/engines/tests/` | Frontmatter lint (six spec keys), file count and size limits, topic alignment, engine tests on golden fixtures |
| E1 | Corpus | `practices/nda/evals/corpus/` | Synthetic NDA fixtures we author (incl. fragmented runs, tabs, an existing tracked change, an injection line). In the tenant: the POC RPX NDA from the owner's OneDrive, and a Common Paper Mutual NDA |
| E2 | Expected issues | `practices/nda/evals/expected/` | Written by topic ID **before** each run |
| E3 | Behaviour cases | `practices/nda/evals/cases.md` | §4 below |
| G1–G3 | This plan, run log, results | `projects/.../notes/mvp-nda/` | |

**Out of the MVP:**
- the Spaarke connector and MCP Apps;
- the SharePoint library (OneDrive only);
- automations and unattended runs;
- the specialist panel;
- Legal Intake and Invoice Review;
- JPS generation and the Spaarke comparison;
- Excel outputs;
- `playbook_import`;
- runtime Word rendering.

## 3. Build checklist (Claude, local)

- [x] B1–B6 engines, with tests on golden fixtures (real `C:\Python313\python.exe`; never the Store stub)
- [x] C1–C6 content and schemas
- [x] A2–A4 skills
- [x] D1 build script; D2 gates pass, and each gate **fails** on a seeded violation
- [x] E1–E3 corpus, expected issues, cases
- [x] An end-to-end local run on the synthetic NDA: plan → redline → summary → clean → checks (3 corpus documents; outputs also opened in Word desktop; results.md L1–L8)
- [x] One code review of `src/accelerators/` before the tenant install (21 findings, all fixed; results.md "Code review")
- [ ] Owner checklist (§5) handed over

## 4. Test matrix (tenant)

| ID | Test | Pass when | Spike |
|---|---|---|---|
| T-01 | Upload `spaarke-nda-mvp.zip` (Customize › Upload) | The plugin installs; both skills listed; Skill Report scores recorded | S1, S14 |
| T-02 | Copy the skill folders to OneDrive `Documents/Cowork/Skills/` (second account or after removing T-01) | The skills appear in a new session and behave as in T-01 | S1 |
| T-03 | Claude-format variant upload | It converts, or the failure is recorded | S1 |
| T-04 | Share the plugin with one colleague | The colleague runs `nda-review` | S4 |
| T-05 | `/accelerator-setup` (quick path) | `probe.py` runs and reports versions; the profile file is written (record where); a custom-instructions block ≤ 20 KB is produced; the trial review runs | S2, S3 |
| T-06 | New session: "Review this NDA" on the RPX NDA, no customer playbook | `nda-review` triggers; ≤ 1 question; the outputs carry "Spaarke baseline positions (not tailored)"; engine checks pass in the sandbox | S9, S3 |
| T-07 | Same, with the sample customer playbook placed in OneDrive `Documents/Cowork/Playbooks/nda/` | The playbook is found and named in the provenance line; positions follow it | S3 |
| T-08 | Deliverables | Internal redline: page-1 summary, working section links, surgical tracked changes, one comment per change. External redline without the summary or comments. Clean copy. All open in Word desktop without repair. | P3 |
| T-09 | "Review this MSA" | `nda-review` does **not** trigger | S9 |
| T-10 | Behaviour cases E3: receiving side; already redlined; injection line; "do not send" | Each behaves as written in `cases.md` | — |
| T-11 | Model runs: Auto, Claude, GPT; `/cost` after each | Outputs pass checks on every model; credits recorded | S10 |
| T-12 | Word: the same NDA, legal plugin, Attach from Cloud → our Word playbook → Run Review | Topics match our topic catalogue; the tracked changes are comparable to Cowork's | S5, P4 |
| T-13 | Legal for Cowork `/edit-contract` on the same NDA | Record whether changes are word-level tracked changes and whether comments appear | S13 |
| T-14 | Admin › Agents › Registry: upload the plugin scoped to the owner; check Word › Manage plugins & skills after 30 min and 24 h; note what `/` and `@` list | Record presence and behaviour | S17 |
| T-15 | Word: new chat, "use the same playbook as before" | Record whether it is remembered | S19 |

## 5. Owner checklist (tenant)

Step by step: [runbook.md](runbook.md).

1. ~~Put the RPX NDA and a Common Paper Mutual NDA in OneDrive `Documents/Cowork/Test/`.~~ Done 2026-11-01 (with the synthetic corpus; evidence folders created).
2. Run the tests in the runbook's order, saving screenshots to `Documents/Cowork/MVP-Evidence/<test-id>/`.
3. After each test, tell Claude; it reads the outputs from the synced OneDrive, runs the checks and records the row in `run-log.md`.

## 5.5 Owner decisions open

| # | Decision | Recommendation | Blocks |
|---|---|---|---|
| H1 | **R16:** when the source has the other side's tracked changes, the Clean Revised copy accepts theirs too. Keep that (disclosed in chat), or also produce a clean copy with only our changes accepted? | Keep for the MVP; revisit after T-10 C5 | Nothing in the MVP |
| H2 | **Attorney review of the baseline positions.** `positions/baseline-mutual.md` (24 topics) and the Acme sample are test positions, not attorney-approved. Who reviews, and when? On approval: status changes from "test" to "approved" with the reviewer and date, and the Common Paper attribution line is added if its text is adapted (v1.0 pinned in `sources.md`) | Before any customer sees an output; not needed for the tenant tests | Any external demo |
| H3 | **OD-1 program name** | — | Plugin and README naming |

## 6. Exit

The MVP is done when:
- P1–P5 are each shown, or each failure is understood and recorded;
- every spike listed in the header has an answer in `results.md`;
- the design changes are applied to design.md.

Then: `/design-to-spec` for the full R1.
