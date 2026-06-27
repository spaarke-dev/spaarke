# PCF Orphan Cleanup — Design

> **Project**: pcf-orphan-cleanup-r1
> **Design version**: 1.0 (2026-06-22)
> **Operational reference**: this design document points to the existing [orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md) as the canonical procedure. This file captures decisions + rationale + cross-links; the procedure doc captures the step-by-step.

---

## 1. The decision tree that led to this project

Triggered 2026-06-22 by a chat-routing-redesign-r1 report flagging TypeScript drift in `@spaarke/ui-components` consumers. Four buckets:

```
Bucket A — Mixed dist/+src/ imports     ← UQC-only
Bucket B — Broken SSE re-export stub    ← UQC-only
Bucket C — tsconfig module mismatch     ← UQC-only
Bucket D — @types/react 18 vs 19/16     ← affects 3 PCFs + shared lib
```

The investigation followed this chain:

1. **Is UQC even in use?** → discovered no project literally named "PCF-code-page-inventory" but found two adjacent inventory artifacts (bundle-sizes from 2026-05-14; test-rot from 2026-06-01). Neither answered the deployment question.
2. **New inventory pass** → built [`pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) using source-tree enumeration + live Dataverse MCP query.
3. **UQC verdict**: deployed in Dataverse (v3.15.2) but ribbon command migrated to a Code Page in v4.0.0 (evidence: [sprk_subgrid_commands.js:582-585](../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js#L582-L585)) — confirmed orphan.
4. **Adjacent finding**: 9 other PCFs deployed in Dataverse without any source-tree backing. Orphans of prior migrations that never completed cleanup. Folded into scope.
5. **VisualHost question**: owner has recent enhancements; downversioning concerns. Verified with grep of `src/client/pcf/VisualHost/control/**/*.{ts,tsx}` for React 18-only APIs → zero hits. Safe to re-pin.
6. **DTW + SDV question**: owner unsure if used; source-tree grep returned no external importers. Confirmed unused 2026-06-22; folded into Part B (source delete) and Part C (Dataverse delete for SDV; just canvas-app cleanup for DTW since DTW PCF was never deployed).

## 2. Three streams, one execution flow

The procedure doc structures execution as five Parts:

- **Part A** — Pre-flight (backups + 4-check verification per control) — see [procedure §1](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#1-pre-flight-binding-before-any-destructive-action)
- **Part B** — Source-tree deletion (UQC + DTW + SDV) — see [procedure §2](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#2-part-b--source-tree-deletion-pr-1)
- **Part C** — Dataverse cleanup (11 customcontrols + 4-6 canvas apps) — see [procedure §3](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#3-part-c--dataverse-cleanup-single-session-gated)
- **Part D** — React-types alignment (shared lib peerDep + VisualHost re-pin) — see [procedure §4](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#4-part-d--shared-lib--pcf-react-types-alignment-prs-2--3)
- **Recovery** — Resurrection playbook for "we deleted something we needed" — see [procedure §3.5](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#35-resurrection--recovery--explicit-playbook)

The procedure doc is the binding step-by-step. This design.md captures the WHY behind the choices.

## 3. Sequencing (the 14-day plan)

```
Day 1-2:  PR 1 (Part B source deletion: UQC + DTW + SDV) — owner review + merge   [Task 002]
Day 3:    Pre-flight §1.1 + §1.2 in spaarkedev1                                    [Task 001]
Day 4:    Part C cleanup session in spaarkedev1 (4-6 hours, one focused block)     [Task 003]
Day 4-10: 1-week soak — monitor for regressions
Day 11:   PR-D1 (shared lib peerDep)                                                [Task 004]
Day 12:   PR-D2 (VisualHost re-pin) — deploy, smoke test                            [Task 005]
Day 13:   Part C cleanup in spaarkedev2 (replay)                                    [Task 006]
Day 14+:  Cleanup log finalize + inventory refresh                                  [Task 007]
```

Note Task 001 runs in parallel with Task 002 — pre-flight verification doesn't block source deletion (orthogonal write paths).

## 4. Key design decisions

### D-01 — Source delete in one PR vs three

One PR. Each of UQC, DTW, SDV is independent (no cross-folder dependencies). Bundling reduces review overhead. If a reviewer challenges any one, `git restore` removes that folder from the PR pre-merge.

### D-02 — SDV FormXML grep is a hard gate, not advisory

SpeDocumentViewer was the only one of the three source-delete candidates that was actively deployed AND likely to be bound to specific forms (document-viewer PCFs typically land on form sections of document-bearing entities). Treating its FormXML grep as mandatory protects against the "we forgot about that one form" failure mode.

### D-03 — PlaybookBuilderHost explicitly excluded

PlaybookBuilderHost has:
- Non-standard source layout (no top-level `ControlManifest.Input.xml` per inventory §2)
- Recent Dataverse activity (modified 2026-01-20)
- Higher version number than most orphans (v2.25.0)
- A canvas-app host that may still be referenced

This combination suggests it's NOT a simple orphan. Owner triage required before any deletion action.

### D-04 — Bucket D fixes VisualHost DOWN to React 16, not UP to React 19

Two reasons:
1. **Platform-library runtime is React 16.14.0** (declared in VisualHost's manifest at line 148). Type pin should match runtime per ADR-022.
2. **VisualHost source has zero React 18-only API usage** (verified 2026-06-22). Aligning DOWN is feature-preserving.

The shared lib stays at `@types/react ^19.0.0` for its own self-build but declares peerDeps as `>=16.14 || >=17 || >=18 || >=19` so each consumer's pin wins. ADR-022's "React 16 APIs only" constraint applies to the shared lib too — verified by the same grep as part of PR-D1.

### D-05 — 7-day soak between environments

Mirrors the production-release procedure cadence. Allows business-hours regression discovery in spaarkedev1 before spaarkedev2 takes the same hit. The cost (slower rollout) is much smaller than the cost of double-environment recovery if a regression slips through.

### D-06 — Project ends at spaarkedev2

Production replication is governed by [deploy-new-release skill](../../.claude/skills/deploy-new-release/SKILL.md) and is a separate execution. The cleanup project's job is to prove the procedure works in two non-prod environments; production rollout is the standard release-procedure's job.

## 5. Risk register

See [procedure §6](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#6-risk-register) for the full 8-row risk register. The two HIGH-severity risks specific to this project:

| Risk | Mitigation |
|---|---|
| SpeDocumentViewer was bound to forms we didn't find | NFR-02 elevates the FormXML grep to a hard gate on SDV specifically. Recovery via §3.5.2 (baseline backup re-import) if the grep missed something. |
| Re-pinning VisualHost breaks recent enhancements | Owner-verified 2026-06-22: VisualHost source has zero React 18-only API usage. PR-D2 re-verifies pre-merge. Smoke-test covers chart / drill-through / MetricCard / recent enhancements. |

## 6. Applicable ADRs

- **[ADR-022 — PCF Platform Libraries](../../.claude/adr/ADR-022-pcf-platform-libraries.md)** — DIRECTLY binding. "React 16 APIs only" is the rationale for D-04 (VisualHost re-pin DOWN, not UP).
- **[ADR-006 — PCF Over Webresources](../../.claude/adr/ADR-006-pcf-over-webresources.md)** — relevant context (PCFs are the preferred extension surface for in-form UI; this project doesn't violate that, just retires unused PCFs).
- **[ADR-012 — Shared Component Library](../../.claude/adr/ADR-012-shared-component-library.md)** — `@spaarke/ui-components` peerDep contract change in FR-04 is consistent with ADR-012's "context-agnostic" goal (multi-major peerDep range = more consumers).

## 7. What this project intentionally does NOT do

See [spec.md §4 Explicitly out of scope](spec.md#4-explicitly-out-of-scope). Notable exclusions:

- PlaybookBuilderHost retirement
- Code Page / Vite SPA cleanup
- Shared lib React-18 API removal beyond what the FR-04 audit surfaces
- Production environment deployment (deferred to standard release procedure)

## 8. Operational procedure (canonical reference)

For step-by-step execution, see:

📋 **[orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md)**

The procedure doc is the binding operational reference. This design.md captures the rationale; the tasks in [tasks/](tasks/) capture the decomposition.
