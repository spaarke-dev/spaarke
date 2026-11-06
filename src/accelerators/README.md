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
  positions/          Spaarke baseline playbook(s)
  samples/            sample customer playbook
  skills/nda-review/
  evals/              corpus generator, expected issues, behaviour cases, local e2e runner
  onboarding.md, sources.md
build/
  Build-Accelerator.ps1  entry point
  build_accelerator.py   assemble, gate, test, package
  gates.py               lint rules (tests in build/tests)
  render_word_playbook.py
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
| `t0/word-playbooks/*.docx` | Word legal plugin: Review contract › Attach from Cloud |
| `e2e/` | Local end-to-end outputs on the synthetic corpus |
| `manifest.json` | Versions, gate results, SHA-256 of every output |

## Rules

Engines: stdlib + `lxml`; no network, no secrets, no LLM calls; JSON in/out; `--help`; exit codes
documented in each file; never `xml.etree` for writing. Each skill carries its own copy of the
engines it needs and stays within 20 files / 10 MB (gated). Test fixtures never ship.
