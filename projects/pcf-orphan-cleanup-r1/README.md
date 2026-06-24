# PCF Orphan Cleanup — R1

> **Status**: Phase 0 — Project Setup (created 2026-06-22)
> **Type**: Quality / hygiene — source-tree cleanup + Dataverse cleanup + type-system alignment
> **Predecessor research**: chat-routing-redesign-r1 TypeScript drift audit → triggered the inventory pass
> **Estimated effort**: 3–5 days end-to-end (1 source PR, 1 Dataverse session, 2 React-types PRs, 1 replay session)
> **Production blast radius**: HIGH (Dataverse customcontrol deletions are not name-stable for restoration — see [design.md §3.5 Resurrection playbook](design.md))

## What this project does

Retires PCFs and canvas apps that no production traffic flows to. Aligns the remaining workspace on one canonical React-types contract.

**Three streams of work that share one execution flow**:

1. **Source-tree cleanup** — delete 3 PCF folders that no consumer imports (UQC, DrillThroughWorkspace, SpeDocumentViewer).
2. **Dataverse cleanup** — delete 11 published `customcontrol` records + 4–6 hosting canvas apps that no live ribbon / form / page references.
3. **React-types alignment** — `@spaarke/ui-components` declares `@types/react` as peerDep (multi-major); VisualHost re-pinned from React 18 to React 16 to match its `platform-library` runtime per ADR-022 (closes the "bucket D" drift from the chat-routing-redesign-r1 audit).

## Why this is one project, not three PRs

The three streams share:
- One inventory of "what's alive and what isn't" (built during chat-routing-redesign-r1's TypeScript-drift investigation).
- One execution flow (pre-flight backups → source PR → Dataverse session → types PRs → replay).
- One production blast-radius gate (Dataverse cleanup can't be safely batched with source-only PRs).

Doing them separately invites re-drift between PRs and risks deleting the wrong artifact at the wrong time.

## Graduation criteria

This project is done when:

- [ ] UQC + DrillThroughWorkspace + SpeDocumentViewer folders absent from `main`.
- [ ] 11 orphan `customcontrol` records absent from spaarkedev1 + spaarkedev2 (Dataverse MCP query returns empty).
- [ ] 4–6 orphan canvas apps absent from both environments.
- [ ] VisualHost re-pinned to React 16; deployed; smoke-test of recent enhancements passes.
- [ ] `@spaarke/ui-components` declares `@types/react` as peerDep with multi-major range.
- [ ] No new TS2322 / TS2305 / TS2307 errors across the 11 remaining PCFs and 4 code-pages.
- [ ] [`pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) updated with completion footnote.
- [ ] Per-control deletion log filed in [`notes/dataverse-cleanup-log.md`](notes/dataverse-cleanup-log.md).
- [ ] Baseline solution backups archived in `backups-2026-06-22/`.

## Key files

| Where | What |
|---|---|
| [README.md](README.md) | This file |
| [spec.md](spec.md) | FRs / NFRs / Success Criteria |
| [design.md](design.md) | Full procedure — pre-flight, source delete, Dataverse cleanup, React-types fix, resurrection playbook |
| [CLAUDE.md](CLAUDE.md) | Project-scoped AI context (always load first) |
| [current-task.md](current-task.md) | Active task pointer |
| [plan.md](plan.md) | Phase breakdown + parallel groups |
| [notes/research-findings-2026-06-22.md](notes/research-findings-2026-06-22.md) | All research compiled during the chat-routing-redesign-r1 investigation that led to this project |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task tracker |
| `backups-2026-06-22/` | Baseline solution ZIPs (captured in Task 001 — the safety net for Dataverse-side recovery) |

## Cross-references (authoritative source artifacts)

- [`projects/ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) — the inventory this project acts on
- [`projects/ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md) — the operational procedure (this project's `design.md` references it as canonical)
- [`projects/ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md`](../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) — 2026-05-14 bundle-size baseline (will need refresh post-cleanup)
- [`projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) — 2026-06-01 test-rot audit (different lens; complementary data)
- [`.claude/adr/ADR-022-pcf-platform-libraries.md`](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — binding constraint for "React 16 APIs only" in PCFs

## Origin note

This project was scoped during a 2026-06-22 investigation triggered by the chat-routing-redesign-r1 report on TypeScript drift in `@spaarke/ui-components` consumers (4 buckets: A=mixed `dist/`+`src/` imports, B=broken SSE re-export stub, C=tsconfig module mismatch, D=`@types/react` 18-vs-19 collision). The investigation found that 3 of the 4 buckets (A, B, C) only mattered for UniversalQuickCreate, which the inventory pass then confirmed was an orphan (ribbon command migrated to a Code Page in v4.0.0). Bucket D remains as one VisualHost re-pin.

The wider Dataverse orphan-PCF cleanup was discovered "for free" during the inventory query and added to scope to avoid leaving the cleanup half-done.
