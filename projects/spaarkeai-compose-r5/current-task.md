# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-28 (project setup — design.md authored, worktree created)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | **Setup phase.** `design.md` written + committed to master. Worktree created. **spec/plan/tasks NOT yet generated.** |
| **Status** | ⏭️ Ready for `/design-to-spec spaarkeai-compose-r5` (then `/project-pipeline`). Execution gate (R4.5 merge) is CLEARED. |
| **Active task** | none (project not yet piped) |
| **Next Action** | Run `/design-to-spec spaarkeai-compose-r5` → produces `spec.md`, mirroring the design.md §10 coordination clauses into binding FRs/NFRs. |

## What exists
- ✅ `design.md` (12 sections, R4-template; committed to master `7989462e8`).
- ✅ `README.md` (authoritative gap ledger G1–G12) + `notes/COORDINATION-with-r4.5.md` (both already on master from prior sessions).
- ✅ `CLAUDE.md` (project context — loads automatically; carries all binding rules + coordination).
- ❌ No `spec.md`, `plan.md`, `tasks/`, Project Issue, or `projects/INDEX.md` row yet.

## Scope (11 gaps) — G1,G2,G3,G4,G5,G7,G8,G9,G10,G11,G12
**G6 is NOT R5** (R4.5 shipped it). REQ-3 READ side = R4.5; R5 owns EDIT side. See `CLAUDE.md` / `README.md`.

## Coordination facts the next session must NOT re-derive (full detail in CLAUDE.md §Coordination)
- **R4.5 is MERGED to master (2026-07-28)** — execution gate cleared; `NumberingComputationEngine` + `CitationResolver.cs` are in `Services/Compose/`.
- **⚠️ docxBridge hazard:** NEVER delete `docxBridge.ts` — G1/G2/G7 depend on `buildContentModel`/`stampParaIds`/paraId helpers (only R4.5's read fn `docxToTipTapHtml` was removed).
- **Reuse, don't fork:** G3 → `NumberingComputationEngine`; G10 → `CitationResolver`; G7 → R4.5 transient-mount projection identity.
- **Two contended files** (rebase onto post-R4.5 versions): `ComposeService.cs`, `ComposeWorkspace.tsx`.
- **analysis-hub-r1 (NFR-09):** downstream consumer of Compose save/versioning/redline; shares `Spaarke.Compose.Components` + `ConversationPane` compose-routing/e2e tests — R5 must not regress its reopen-restore / retirement parity.

## Open decisions seeded for design-to-spec (design.md §12)
- Q1 confirm R5-D1…D5 (esp. R5-D2 clean-apply approach; R5-D5 G10 stays in R5).
- Q3 origin-marker home (Dataverse field vs SPE metadata; recommend Dataverse field).
- Q4 G4 table scope (full tracked structure vs cell-content first slice).
- Q5 G12 revision-op granularity (single-by-id vs also accept-all/reject-all).
- Q6 deploy order (R4.5→master→R5) + optional reciprocal coordination note into R4.5 notes.

## Health
- Worktree off updated master (`7989462e8`); local synced with origin at setup time.
- BFF=Y, SpaarkeAi=Y. Publish baseline ~46.11 MB (≤60 ceiling). Zero new runtime package expected.

## How to resume
`/design-to-spec spaarkeai-compose-r5` or "where was I?" → this file + `CLAUDE.md`.
