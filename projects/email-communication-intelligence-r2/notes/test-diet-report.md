# Test diet report — email-communication-intelligence-r2

**Run date**: 2026-09-07 (project-close re-run) · supersedes the 2026-08-31 run (which was **clean / 0 deletes**)
**Branch**: work/email-communication-intelligence-r2
**Scope**: test files **authored by r2** since the 2026-08-31 diet (triage-category fix + R3-CARD + task 064).

> **Scoping note (important).** A raw `git log --since=2026-08-31 -- tests/** '**/__tests__/**'` on this
> branch lists ~60 files — but the branch has merged `origin/master` repeatedly, so that view is polluted
> with **other projects'** tests that landed on master (Compose r8, ExternalAccess r3, UnifiedAccessControl
> r2, SdapClient, the Create*Wizard services, Office contract tests from other streams). Those are **out of
> scope** — each is dieted by its owning project. The r2-authored delta is isolated to the four files below,
> confirmed by inspecting r2's own commits: `309e7f674` (064), `2de7a006d`+`a5d2ae216` (R3-CARD),
> `ceba44928`+`143739ded` (triage `$choices` fix).

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 4 | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| Stale reliability-registry entries | 0 | — |
| **Total r2-authored test files touched** | **4** | — |

## Delete commands
None — no scaffolding-class tests. (Consistent with the 2026-08-31 run: 0 deletes.)

## Path-move commands
None — both C# files are already at canonical KEEP paths.

## Maintain — confirmed (no action)

| File:scope | KEEP path | Why maintain |
|---|---|---|
| `tests/integration/contract/Api/Ai/ChatDocumentEndpointsContractTests.cs` (064 `IngestFromDocument_*`) | `tests/integration/contract/**` | Behavior contract test: `IngestFromDocument_WhenArchive_Returns200…`, `_WhenDocumentMissing_ReturnsNotFound`, `_WhenNotEmailArchive_Returns422`, `_WhenCommunicationId_ResolvesArchiveAndReturns200` — real HTTP-status assertions on the E1c endpoint, `{Method}_{Scenario}_{ExpectedResult}` names, no `Mock<HttpMessageHandler>` / no `GetRequiredService` wiring / no ctor-null. Clean vs all 17 bans. |
| `tests/integration/seam/Ai/ActionRunnerChoicesResolutionSeamTests.cs` (triage `$choices` enum injection) | `tests/integration/seam/**` | Real vertical-slice seam: live `ActionRunner` + `PromptSchemaRenderer` + `LookupChoicesResolver`; positive asserts the category enum lands in the constrained schema, control asserts free string. Not a wiring/mock test. |
| `src/client/shared/Spaarke.Communication.Components/.../EmailAssociationsAndTracking.test.tsx` (E1b launcher + R3-CARD modal blocks) | co-located `__tests__` (shared-lib jest convention) | RTL behavior tests: "New record" launcher → `confirmCandidate` write; "See all" modal lists the hidden 4th + confirm files it; GUID-only card shows type+reason. Real component render + interaction + write-path assertion — not scaffolding. |
| `src/client/shared/Spaarke.Communication.Components/.../__tests__/provenanceIdentity.test.ts` (R3-CARD logic) | co-located `__tests__` (shared-lib jest convention) | Pure-logic behavior tests: `looksLikeGuid` classification, `deriveConnections` name-fallback, `derivePrimaryReview.allCandidates` full-set-behind-top-3-cap. Value/state assertions, not coverage-filler. |

## Count delta
- r2-authored test files touched since last diet: 4
- MAINTAIN: 4 · SCAFFOLDING: 0 · AMBIGUOUS: 0 · PATH-VIOLATION: 0
- Net post-diet expected count: unchanged (no deletes)

## Verdict
**Clean — 0 deletes, 0 path-moves, 0 ambiguous, 0 stale registry entries.** No reviewer action required.
All four r2-authored deltas are behavior/contract/seam tests at their canonical KEEP paths (ADR-038 §7).

## Industry citation
Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes). 17-ban classifier B1–B17; the eighth KEEP path (`tests/Spaarke.ArchTests/**`, Amendment A1) not exercised here.
