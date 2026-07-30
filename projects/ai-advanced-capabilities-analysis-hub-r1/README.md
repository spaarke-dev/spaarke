# AI Advanced Capabilities — Analysis Hub & Session Persistence (`ai-advanced-capabilities-analysis-hub-r1`)

> **Status**: Planning (project-pipeline run 2026-07-28) · **Branch**: `work/ai-advanced-capabilities-analysis-hub-r1`
> **Owner**: ralph.schroeder · **Round**: r1 · **Spawned from**: `ai-advanced-capabilities-nda-r1` UAT
> **Sibling**: `ai-advanced-capabilities-research-r1` · **Program**: `projects/ai-advanced-capabilities-development/`

## What this project delivers

Generalize the proven NDA advisory vertical into a first-class **Analysis platform**:
1. A durable **`sprk_analysis` business spine** — create, find, reopen, associate AI analyses.
2. **Two-tier session persistence** bound to the Analysis record (fork-on-analysis) so sessions survive close/refresh/reopen and never get lost.
3. An **Analysis hub widget** + per-type **creation wizard**.
4. A **clean retirement** of the superseded `sprk_analysisworkspace` code page (no capability migration).

ONE Analysis experience (the SpaarkeAi three-pane), rendered in two hosting contexts: the SpaarkeAi workspace and a code-page modal launched from a record form. ≈80% composition of existing frameworks.

## Graduation criteria (from spec Success Criteria)

- [ ] 12 pre-existing e2e failures pass (green baseline)
- [ ] User can create an Agreement Review from the hub (card → wizard → running analysis)
- [ ] An analysis is a durable `sprk_analysis` record, findable + reopenable with session/review/files restored
- [ ] Launching an analysis forks a new bound session + archives prior with warning
- [ ] Sessions survive TTL expiry via Cosmos (no empty-session data loss)
- [ ] Matter/Project record shows an Analysis subgrid; 2b/2d record entry paths work
- [ ] `sprk_analysisworkspace` fully retired — no 404 deep-links, no dangling refs, casing reconciled
- [ ] BFF publish-size ≤60 MB on every BFF task + no new HIGH CVE
- [ ] Record-driven opens route via `openSpaarkeAi`, not `surfaceLaunchRegistry`

## Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Coordination (binding)**: `Services/Ai/` is sole-owned by `spaarke-ai-architecture-redesign-r2` — consume `PublicContracts/` seams, NO fork, `/conflict-check` before every BFF PR. Merge-order `ConversationPane`/widget-registry touches against `spaarke-ai-architecture-redesign-r1`.

## Human gates

- **Owner schema** (HUMAN GATE): `sprk_worktype`, `sprk_regardingmatter/project/document`, `sprk_description` (✅ created), `sprk_aichatsummary.sprk_analysis` FK — owner pre-creates; project consumes. `sprk_description` + `sprk_project` confirmed; remaining columns pending.
- **UQ-1** (fork-on-analysis layer): recommended **Option B — new BFF `POST /api/ai/analysis/fork` endpoint** (session GUIDs are server-minted; atomic fork). Pending owner confirm → ADR-013/§10 Path A exception.

## Key artifacts

- [`spec.md`](spec.md) — 22 FRs / 7 NFRs (source of truth for requirements)
- [`PLAN.md`](PLAN.md) — 8-phase WBS + discovered resources + critical path
- [`design-discussion.md`](design-discussion.md) — authoritative design (owner decisions §11.7 / §12 / §13)
- [`CLAUDE.md`](CLAUDE.md) — project AI context
- `tasks/TASK-INDEX.md` — task registry (generated in pipeline Step 3)

## Deferred / out of scope

Tabular doc×question review grid · per-message Dataverse cold persistence · Legal Research + Patent work types (cards ship "coming soon" only) · side-by-side source preview (`SourceViewerPanel`, retired) · net-new Dataverse column creation (owner-owned) · in-chat proactive "open Agreement Analysis" offer (UQ-4 = NO).
