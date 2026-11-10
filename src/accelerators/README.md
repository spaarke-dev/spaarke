# Spaarke legal accelerators

Practice accelerators for Microsoft 365 Copilot Cowork and the Word legal plugin (design:
`projects/spaarke-legal-ai-accelerator-r1/design.md`). **Status: NDA MVP spike.** Positions are
test positions, not attorney-approved.

```
core/
  engines/            Python run in the Cowork sandbox (stdlib + lxml only)
    probe.py            what the sandbox can run
    segment_clauses.py  section map + unique paragraph keys
    playbook_parse.py   Markdown playbook -> JSON; closed topic ids
    playbook_import.py  four-column Word playbook (create-playbook / Word format) -> our Markdown, rows mapped to topic ids
    ooxml_redline.py    edit plan -> surgical tracked changes, comments, bookmarks; external + clean copies
    summary_table.py    page-1 linked summary (internal redline)
    check_outputs.py    blocking checks incl. untracked-edit detection
    xlsx_lite.py        stdlib .xlsx writer: styled cells, links, native bar charts with cached values
    review_visuals.py   review dashboard (static HTML + SVG) and audit pack (.xlsx) from review.json + plan
    library_grid.py     documents x topics grid (.xlsx) and heatmap (HTML); quotes checked against the text
    tests/              unittest on generated fixtures (must-fire + must-not-fire)
  schemas/            edit-plan, review-result, legal-playbook (JSON Schema)
  shared-references/  guardrails, standards protocol, playbook format (copied into skills)
  skills/accelerator-setup/
practices/nda/
  accelerator.yaml    what the build assembles
  topics.yaml         topic catalogue: the join key
  positions/          Spaarke baseline playbook and the library playbook (Spaarke NDA – Mutual v1.2)
  samples/            sample customer playbook
  skills/nda-review/
  agent/              Agent Builder instructions (the recipe's text)
  evals/              corpus generator, expected issues, behaviour cases, local e2e runner
  onboarding.md, sources.md
practices/legal-hold/   non-contract type: no sides (playbook_parse --no-side)
  accelerator.yaml, topics.yaml, positions/, agent/
  skills/legal-hold-review/
build/
  Build-Accelerator.ps1  entry point (one practice: plugin, skills, Word playbooks)
  build_accelerator.py   assemble, gate, test, package
  Build-Package.ps1      entry point for the individual-base package (runs the nda build first)
  build_package.py       Documents/ tree + two zips; package_docs.py writes its Word documents
  gates.py               lint rules for skills, plugins and packages (tests in build/tests)
  render_word_playbook.py  four-column and readable (table-free) playbooks
  out/                   generated (git-ignored)
```

## Build

```powershell
./build/Build-Accelerator.ps1 -Practice nda
```

Needs a real Python 3.9+ (not the Windows Store stub) with `pyyaml`, `python-docx`, `lxml`.
Outputs in `build/out/nda/`:

| Output | Use |
|---|---|
| `t0/spaarke-nda-mvp.zip` | Cowork: Customize › Upload plugin (unified manifest, `agentSkills[]`) |
| `t0/spaarke-nda-mvp-claude-format.zip` | Claude-format plugin, to test conversion (spike S1) |
| `t0/skills/<name>/` and `.zip` | Copy to OneDrive `Documents/Cowork/Skills/`, or upload a single skill |
| `t0/word-playbooks/*.docx` | Word copy (Microsoft's four columns, checked at build) for the Word legal plugin: Review contract › New playbook › Attach from Cloud. `* (headings).docx` is the transitional heading layout, dropped once the Word tests pass (backlog X3) |
| `e2e/` | Local end-to-end outputs on the synthetic corpus |
| `manifest.json` | Versions, gate results, SHA-256 of every output |

### Individual-base package

```powershell
./build/Build-Package.ps1          # nda build, then the package; -SkipNdaBuild when out/nda is current
```

Outputs in `build/out/package/`: `Spaarke Legal Accelerator - Individual base v0.1.zip` (extract into
OneDrive `Documents/`: `Cowork/Skills/...` and `Legal Knowledge/...`) and `Spaarke test documents v0.1.zip`.
Gates: unique file names under `Legal Knowledge/`, paths of at most 200 characters, a custom
instruction of at most 1,500 characters, no answer-key material. The test documents come from the
POC kit (`--kit`, default `projects/spaarke-legal-ai-accelerator-r1/notes/poc-e2e`).

## Rules

Engines: stdlib + `lxml`; no network, no secrets, no LLM calls; JSON in/out; `--help`; exit codes
documented in each file; never `xml.etree` for writing. Each skill carries its own copy of the
engines it needs and stays within 20 files / 10 MB (gated). Test fixtures never ship.
