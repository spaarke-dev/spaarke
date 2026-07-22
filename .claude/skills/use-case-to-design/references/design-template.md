# design.md template — use-case-to-design

> Fill every lens. An unknowable item becomes an explicit **Unresolved Question**, never an omission.
> Output path: `projects/{project-name}/design.md`. Downstream: `/design-to-spec {project}`.

---

# {Use Case Name} — Design

> **Program**: ai-advanced-capabilities-development (or standalone)
> **Project**: `{project-folder}`  ·  **Round**: r{N}
> **Date**: {YYYY-MM-DD}  ·  **Owner**: {owner}
> **Driver**: Use case (vertical). This design is defined by the use case, not by a horizontal capability.

## Lens 1 — Use Case Definition

- **Jobs to be done**: {review / analysis / drafting / research / triage — name the ones in scope}
- **Personas**: {who does this — paralegal, associate, partner, ops, risk} and their anchoring activity.
- **Triggers**: {how it starts — user uploads doc, clicks chip, asks in chat, scheduled, webhook}
- **Inputs → Outputs**: {input artifacts → what the user gets back}
- **Concrete sub-tasks** (the closed list):
  1. {e.g., "Review NDA from represented party's perspective → Issue/Summary/Recommended-Change table + risk rating"}
  2. {e.g., "Analyze a selected clause on demand"}
  3. {e.g., "Draft/redline a fix inline"}
- **Scope boundaries / non-goals**: {explicitly OUT — e.g., "single-doc only; mass tabular review is r2"}
- **Done-criteria**: {what "this use case works" means, observably}
- **Business value**: {concrete — time saved, risk caught, revenue enabled; not "AI-powered"}

## Lens 2 — Surface / UX

- **Target surfaces**: {SpaarkeAi workspace · Compose/Tiptap editor · Context pane · SprkChat · DataGrid — which and why}
- **Interaction walk-through** (concrete, end-to-end):
  ```
  {step-by-step: user does X → surface shows Y → AI streams Z → user accepts/rejects → result}
  ```
- **Reused vs. new UI**: {name existing components reused; list any net-new UI (justify in Governance Seeds §11)}
- **Required states**: loading · empty · error · **uncertainty** (how a low-confidence / decline result renders)
- **Citations/provenance UX**: {how sources surface — inline links, source viewer, citation block}

## Lens 3 — AI Capabilities Required

> Frame: "For this use case to work, the AI service MUST be able to…". Name against the real model (Actions/Skills/Tools/Knowledge/Bindings over the ADR-039 closed catalog / ADR-040 ledger). NOT playbook nodes.

| Capability need | Primitive type | Description |
|---|---|---|
| {e.g., produce a structured NDA review} | Prompted Action | {JPS prompt + output schema} |
| {e.g., verify quoted evidence} | Tool / grounding | {citation verification} |
| {e.g., propose inline edits} | Binding (Compose disposition) + redline | {} |
| {e.g., remember counterparty positions} | Memory (Record scope) | {} |
| {e.g., retrieve clause references} | Knowledge/RAG source | {which index} |
| {e.g., surface review steps} | Execution trace | {Context pane} |

## Lens 4 — Have vs. Gap

> Verdict each lens-3 capability against **live code** (check `PROGRAM-ROADMAP.md` §1 first; Explore-audit anything else). Precedence: **REUSE > ACTIVATE > COMPLETE > BUILD**.

| Capability | Verdict | Evidence (file:line) | Note / what's needed |
|---|---|---|---|
| {citation verification} | REUSE | `Services/Ai/CitationVerification/GroundingVerifier.cs` | wired |
| {pinned memory in prompt} | ACTIVATE | `MemoryCompositionService.cs` (dark) | wire ComposeAsync into prompt path |
| {citation source viewer} | COMPLETE | `CitationBadge.tsx` + `context_highlight` SSE | assemble into one viewer |
| {tabular doc×question grid} | BUILD | — | net-new (justify in §11) |

**Verdict legend**: REUSE = wired, use as-is · ACTIVATE = built-but-dark, wire it · COMPLETE = partial, finish it · BUILD = absent, net-new.

## Lens 5 — Configuration

> The concrete seed set. "Configure the Actions" is NOT acceptable — name the rows.

- **Actions** (`sprk_analysisaction`): {code@v, kind prompted/coded, JPS prompt source, output schema}
- **Bindings** (`sprk_playbookconsumer`): {consumerCode, disposition, risk, toolDescription, surfaces, events}
- **Tools** (`sprk_analysistool`): {which tool rows / capability grants this use case needs projected}
- **Knowledge / reference docs**: {which index, which docs to seed; attribution}
- **Grid config** (`sprk_gridconfiguration`): {if tabular — columns → Action bindings}
- **Model tiers**: {per Action}
- **License attribution**: {e.g., "nda-review prompt adapted from Mike OSS `mike-workflows`, MIT, attribution retained"}

## Lens 6 — Acceptance & Evaluation

- **Closed test set**: {5–8 real input docs, mix of clean/problematic, + expected outputs}
- **Negative / authorization cases**: {≥1 — e.g., wrong-tenant doc, insufficient evidence → decline, unauthorized capability}
- **Eval harness cases**: {`legal-eval-config.yaml` entries; `metrics/citation_accuracy.py` targets}
- **Success metrics**: {latency, citation accuracy, finding recall, redline acceptance rate}

---

## Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (per CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>Y|N</bff>
  <spaarkeai>Y|N</spaarkeai>
  <ci-workflows>Y|N</ci-workflows>
  <skill-directives>Y|N</skill-directives>
  <root-claude-md>Y|N</root-claude-md>
</hot-path-declaration>
```

### New Components (§11 three-question gate) — one row per BUILD item from Lens 4
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| {name} | {file:line or "none"} | {Yes→extend / No + why} | {behavior/contract that fails — NOT "flexibility"} |

*(If no BUILD items: "No new components — this use case is reuse/activate/complete only.")*

### Platform-Enabler Flag (demand-pull discipline)
- Does this use case pull through any **shared** platform capability (scheduler · `IGateResolver` · model-tier · EvaluatorGate · `sprk_analysis` results table · memory-composition wiring)?
- For each: **{name}** — minimal increment this use case needs = {…}. Adopt-and-harden here, OR (if a 2nd consumer needs it identically) flag as a candidate standalone enabler project.

### Candidate ADR Tensions (per CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Likely path (A/B/C) | Rationale |
|---|---|---|---|---|
| {ADR-XXX} | {MUST/MUST NOT} | {} | {} | {} |

*(If none: "No ADR tensions anticipated.")*

## Unresolved Questions
- [ ] {question} — Blocks: {what}

---
*Design produced by use-case-to-design. Next: `/design-to-spec {project}`.*
