# Deferrals & Issues — spaarkeai-compose-r1

> **Protocol**: CLAUDE.md §"Deferrals & Issues" (BINDING). Every ISS entry MUST be mirrored to a GitHub issue per the per-project rule. The `push-to-github` skill scans for entries without GitHub URLs and blocks push until they're filed.
>
> **Purpose of this file**: canonical "do not lose" registry. Everything operational that should outlive this PR's chat context — Path A tensions, deferred decisions, pre-existing issues, open spike items, follow-up tasks — lives here. Each entry has a permanent home (GitHub issue, design.md/spec.md anchor, downstream task POML, or this file).
>
> **Last updated**: 2026-06-29 (Wave 10 operator review gate sign-off)

---

## Index — Issues (ISS) + Path A tensions + Spike Open Items + Known Follow-ups

| ID | Type | Title | Status | GitHub / Anchor |
|---|---|---|---|---|
| **ISS-001** | ISS | LegalWorkspace standalone Vite build broken (`@spaarke/daily-briefing-components/widgets` import unresolved) | ✅ RESOLVED in this PR | `src/solutions/LegalWorkspace/vite.config.ts` |
| **ISS-002** | ISS | BFF: HIGH CVE on `Microsoft.Kiota.Abstractions 1.21.2` (GHSA-7j59-v9qr-6fq9) | 📋 OPEN (Compose carry-forward) | [#516](https://github.com/spaarke-dev/spaarke/issues/516) |
| **ISS-003** | ISS | SemanticSearch: 104 pre-existing test failures (319/423) | 📋 OPEN | [#518](https://github.com/spaarke-dev/spaarke/issues/518) |
| **ISS-004** | ISS | SpaarkeAi solution: 1 moderate severity pre-existing CVE in production deps | 📋 NOTED — operator review at W10 (likely defer to SpaarkeAi-team triage) | not yet filed (operator decides) |
| **T-1** | Path A | Compose checkout substrate = Dataverse-side `DocumentCheckoutService` (vs design.md §14 row 4 original "SPE-native check-out") | ✅ APPROVED 2026-06-29 post-Wave-0 | design.md §14 row 4 + spec.md ADR Tensions T-1 row |
| **T-2** | Path A | W5-026 `LoadDocxAsync` / `SaveDocxAsync` round-trip — full coverage via W5-027 integration tests (not in-task unit tests) | ✅ FORMALIZED | POML 026 `<notes>` block |
| **T-3** | Path A | W4-033 SemanticSearch SCAFFOLDING smoke test declined per ADR-038 §7 ban list | ✅ DOCUMENTED | POML 033 `<notes>` block |
| **OI-5** | Spike #3 OI | Should `sprk_lastheartbeatutc` PATCH bump `modifiedon` on `sprk_document`? | 📋 OPEN (operator decision — non-blocking; default Dataverse behavior in place) | Spike #3 §7.2 + this file |
| **FU-1** | Follow-up | ComposeEditor (W4-045) should pause heartbeat when `checkoutStatus !== 'acquired'` | ✅ **RESOLVED 2026-06-29** in code-review cleanup R3 (PR #515 commit `fcb69ed17`) — heartbeat hoisted to `useComposeHeartbeatGate` hook with `checkoutStatus === 'acquired'` guard | n/a (in-PR fix) |
| **FU-4** | Follow-up | Remove `virtual` modifiers from `ComposeSessionService` by rewriting `ComposeServiceTests` to use real instance + `ChatSessionManager` test double (R4 Option C trade-off; small testability smell) | 📋 OPEN (non-blocking; documented in `ComposeSessionService.cs` XML doc) | this file + `ComposeSessionService.cs` XML doc |
| **FU-2** | Follow-up | FR-06 concurrent-Save live test against deployed BFF + live Dataverse Alt Key | 📋 OPEN (belongs to W10 smoke-after-deploy OR separate `tests/integration/Spe.Integration.Tests/` track) | this file + W8-061 POML notes |
| **FU-3** | Follow-up | Pattern D placeholder swap: `LegalWorkspace/sections/composeEditor.registration.ts` (W1b-040 inline placeholder → `@spaarke/compose-components` real widget) | 📋 OPEN (intentionally deferred; Path A modal launch is R1 mount path) | W4-042 agent report + design notes |
| **DEF-7SCs** | Deferred SCs | 7 success criteria from spec.md require live Dev BFF verification (SC4/SC5/SC9-live/SC10-live/SC13/SC14/SC15) | 📋 OPEN — operator runs at W10/W11 | `notes/audits/success-criteria-audit.md` + W10 task 080/081 |

---

## ISS-001 — LegalWorkspace standalone Vite build broken (RESOLVED in this PR)

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: ✅ **RESOLVED 2026-06-29** — no GitHub issue needed; commit included in this branch.

**Reproduction (before fix)**:
```pwsh
cd src/solutions/LegalWorkspace
npm run build
# → Rollup failed to resolve import "@spaarke/daily-briefing-components/widgets"
#   from "...sections/dailyBriefing/dailyBriefing.registration.ts"
```

**Root cause**: `LegalWorkspace/vite.config.ts` had `resolve.alias` entries for every shared lib except `@spaarke/daily-briefing-components`.

**Fix applied**: 3-line addition to `src/solutions/LegalWorkspace/vite.config.ts` mirroring Events/AI.Widgets pattern (sharedLibPaths + react include + resolve.alias).

**Verification**: post-fix LegalWorkspace standalone Vite build → ✅ 3347 modules / 0 errors.

**Discovered by**: Wave 1b-040.

---

## ISS-002 — BFF Microsoft.Kiota.Abstractions 1.21.2 HIGH CVE

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: 📋 **OPEN** — operator approved CARRY-FORWARD at W10 review gate. Compose R1 ships with pre-existing CVE; fix tracked via #516.
**GitHub**: [#516](https://github.com/spaarke-dev/spaarke/issues/516)
**Advisory**: GHSA-7j59-v9qr-6fq9 (HIGH)

**Reproduction**:
```pwsh
dotnet list package --vulnerable --include-transitive --project src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj
```

**Root cause**: `Sprk.Bff.Api.csproj` line 71: `<PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.21.2" />` — direct (not transitive via `Microsoft.Graph 5.101.0`).

**Likely fix paths** (full investigation in #516):
1. Preferred: bump `Microsoft.Graph` to latest 5.x + remove explicit Kiota pin
2. Fallback: bump Kiota directly to GHSA-fixed minor

**Discovered by**: Wave 1b-020 (Compose ran `dotnet list package --vulnerable` per §10 BFF Hygiene rule #5).

**Compose impact**: Compose did NOT introduce; 0 new HIGH CVEs introduced by Compose (verified W9-072).

---

## ISS-003 — SemanticSearch 104 pre-existing test failures

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: 📋 **OPEN** — operator approved skip-fix at W10 review gate; tracked via #518.
**GitHub**: [#518](https://github.com/spaarke-dev/spaarke/issues/518)

**Reproduction**:
```pwsh
cd src/client/code-pages/SemanticSearch
npm test
# → Tests: 104 failed, 319 passed, 423 total
```

**Likely scope**: `useSavedSearches.test.ts`, `useFilterOptions.test.ts` — mock-surface drift.

**Discovered by**: Wave 3-032 + Wave 4-033 (stash-verified pre-existing).

**Compose impact**: None — moved hook (`useDocumentActions`) had its 14 KEEP-path tests added to `@spaarke/document-operations`; the 104 failures live in OTHER SemanticSearch test files unrelated to the moved hook.

---

## ISS-004 — SpaarkeAi solution: 1 moderate pre-existing CVE

**Type**: ISS (noted but not filed)
**Status**: 📋 NOTED at W10 — operator decides whether to file (likely defer to SpaarkeAi-team triage)
**GitHub**: not yet filed

**Reproduction**:
```pwsh
cd src/solutions/SpaarkeAi && npm audit --omit=dev
# → 1 moderate severity vulnerability
```

**Compose impact**: None — Compose's added packages (`@spaarke/compose-components` + `@spaarke/document-operations`) both audit clean (0 vulns each). The 1 moderate is in pre-existing SpaarkeAi prod deps.

**§10 #5 verdict**: Compose §10 #5 PASS (only HIGH severity blocks). Moderate carries forward.

**Discovered by**: Wave 9-072.

---

## T-1 — Compose checkout substrate = Dataverse-side (Path A)

**ADR Tension** (CLAUDE.md §6.5 Path A — project-scoped exception)

**Rule challenged**: design.md §14 row 4 original wording: "per-user single-session lock via **SPE check-out**. Word for Web users automatically see 'Checked out to X' via SPE's built-in indicator"

**Conflict**: Spike #3 found existing `DocumentCheckoutService` (~1170 LOC) already implements check-out/check-in/discard/conflict UX/version tracking. SPE-native wrapper would duplicate ~85% for 1 capability gain (auto-banner in Word for Web/Desktop).

**Resolution**: Path A approved 2026-06-29 at post-Wave-0 operator review gate. Reuse existing service; document trade-off; R2+ escape hatch pre-documented in Spike #3 §3.

**Impact**: Phase 5 LOC ~600 → ~150; Word for Web/Desktop won't auto-warn on cross-surface concurrent open; last-writer-wins documented.

**Permanent home**:
- design.md §14 row 4 — amended wording
- spec.md ADR Tensions table — T-1 row with full rationale + R2 escape hatch
- Spike #3 §6 (operator-approved)

---

## T-2 — `LoadDocxAsync`/`SaveDocxAsync` round-trip via 027 integration tests (Path A)

**ADR Tension**: ADR-038 §4 + §7 (banned patterns)

**Rule challenged**: POML 026 implied unit tests for `ComposeDocumentService.LoadDocxAsync` + `SaveDocxAsync`. Full SPE round-trip unit test would require B1 `Mock<HttpMessageHandler>` (banned) or B2 `Mock<IRequestAdapter>` (banned).

**Resolution**: Path A formalized at W5-026. Argument-validation tests + Phase-5 stub contract tests kept in W5-026; full round-trip coverage moved to W5-027's integration-contract tests (`tests/integration/contract/Api/Compose/`).

**Permanent home**:
- POML 026 `<notes>` block
- W5-027 integration-contract test class XML doc

---

## T-3 — W4-033 SemanticSearch SCAFFOLDING smoke test declined (Path A)

**ADR Tension**: ADR-038 §7 build-vs-maintain criteria

**Rule challenged**: POML 033 AC#2 asked for a consumer smoke test for `useDocumentActions` post-extraction.

**Conflict**: Such a test fits B6 mirror antipattern + B7 all-mocks trivial. The 14 KEEP-path tests in `@spaarke/document-operations` already cover the canonical hook; a mirror at consumer side would be deleted by `/test-diet`.

**Resolution**: Path A — declined. AC#2 satisfied via alternative evidence: TS build clean + 14 KEEP-path tests + App.tsx import + zero test-count regression.

**Permanent home**:
- POML 033 `<notes>` block
- W9-071 ADR-038 conformance audit (`notes/adr-038-conformance.md`)

---

## OI-5 — Should `sprk_lastheartbeatutc` PATCH bump `modifiedon`?

**Source**: Spike #3 §7.2 Open Item 5

**Question**: When `RefreshHeartbeatAsync` PATCHes `sprk_lastheartbeatutc=UtcNow`, should Dataverse default behavior (which bumps `modifiedon`) be allowed, or should the PATCH explicitly suppress `modifiedon` change?

**Trade-off**:
- ALLOW (current default): operator sees "active editing now" via `modifiedon` indicator; audit-log noise on every heartbeat
- SUPPRESS: clean audit; loses "active editing now" visibility

**Current state**: ALLOW (default Dataverse behavior; W7-052 did not explicitly suppress).

**Why non-blocking**: behavior is conservative; auditor sees more not less. If audit-log noise becomes an issue, suppress is a 1-line PATCH header change.

**Permanent home**:
- this file (registry)
- W7-052 task POML notes

---

## FU-1 — ComposeEditor heartbeat-gate when `checkoutStatus !== 'acquired'` (RESOLVED)

**Source**: W7-051 final report (multi-tab UX implementation)
**Status**: ✅ **RESOLVED 2026-06-29** in code-review cleanup R3 (PR #515 commit `fcb69ed17`)

**Resolution**: Heartbeat hoisted from `ComposeEditor` (W4-045) to a new `useComposeHeartbeatGate` hook at `src/solutions/SpaarkeAi/src/components/compose/hooks/useComposeHeartbeatGate.ts`. The hook guards with `if (checkoutStatus !== 'acquired') return;` BEFORE the visibility-state check, so cancelled/failed/probing/discarding tabs no longer hit the heartbeat endpoint. `ComposeEditor` is now a pure drafting surface with no lock-lifecycle concerns.

**Original issue**: Client heartbeat (W4-045) fired every 3 min regardless of checkout state. After force-close, a cancelled tab continued heart-beating a lock it no longer held.

**Server-side mitigation was in place**: W7-052 `RefreshHeartbeatAsync` had same-user guard — returned 404 (no info leak) for cross-user or no-lock heartbeats. So cancelled-tab heartbeats failed harmlessly. R3 fix eliminates the wasted HTTP traffic entirely.

---

## FU-4 — Remove `virtual` modifiers from `ComposeSessionService` (R4 Option C trade-off)

**Source**: Code-review cleanup R4 Option C (2026-06-29)

**Issue**: To collapse the single-impl `IComposeSessionService` interface to concrete per ADR-010 strict, `ComposeSessionService` was changed from `sealed class` → `class` with `virtual` on 3 public methods. This is purely for the Moq test boundary in `ComposeServiceTests` (which uses `Mock<ComposeSessionService>(...)`). The `virtual` modifiers are a small "for testability" smell.

**Cost-of-doing-nothing**: code-quality smell only. `virtual` modifiers signal "subclasses may override" to readers when no subclassing is intended. The single legitimate "override" is the Moq test mock.

**Fix path** (medium effort, ~2-3 hr): rewrite `ComposeServiceTests` to use a real `ComposeSessionService` instance + `Mock<ChatSessionManager>` (or a `ChatSessionManager` test double). This is integration-first per `tests/CLAUDE.md` preference; eliminates the need for `virtual`. Estimated ~30 test method updates.

**Permanent home**: this file (registry) + `ComposeSessionService.cs` XML doc (line-level note inviting the future refactor)

---

## FU-2 — FR-06 concurrent-Save live test

**Source**: W8-061 final report

**Issue**: FR-06 acceptance ("5 concurrent promotes → exactly 1 `sprk_document` row") depends on the live `sprk_graphitemid_uk` Alt Key (✅ in place in Dev per W1a OI-1). Today tested via:
- W5-026: race idempotency against **in-memory mocked** `IGenericEntityService` (proves algorithm, not live behavior)
- W5-027 + W8-061: endpoint-contract level (no concurrency exercise)

**What's NOT tested**: empirical Live Dataverse Alt Key constraint under 5 concurrent HTTP POSTs.

**Where it belongs** (per W8-061 agent's analysis):
- W10 task 080/081 smoke-after-deploy as an acceptance check
- OR a separate `tests/integration/Spe.Integration.Tests/` track

**Permanent home**: this file (registry) + W10 task notes

---

## FU-3 — Pattern D placeholder swap (LegalWorkspace registration shim → real widget)

**Source**: W4-042 + W5-042 architectural analysis

**Status**: INTENTIONALLY DEFERRED per Calendar Pattern D precedent

**Issue**: `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (W1b-040) renders an inline `ComposeWorkspacePlaceholder` Skeleton. The "real" replacement would be to import `ComposeWorkspace` from `@spaarke/compose-components` — but this creates a circular dep (`@spaarke/legal-workspace → SpaarkeAi`).

**R1 resolution**: Compose's R1 mount path is **Path A modal launch** (via `launch-resolver.ts` → SpaarkeAi modal). The LegalWorkspace standalone workspace-picker mount path is a SECONDARY surface that retains the placeholder. Matches Calendar (`CalendarWorkspaceWidget` from `@spaarke/events-components` mounts; standalone LegalWorkspace doesn't expose the rich widget).

**Cost-of-doing-nothing**: standalone LegalWorkspace's "Compose" workspace layout shows the placeholder, not the real editor. Users intended to reach Compose via the modal launch path — they will encounter the rich editor; the workspace-picker path remains an R2+ enhancement.

**Future resolution paths**:
- (a) SpaarkeAi-side `SECTION_REGISTRY.register('compose-editor', …)` sentinel at bootstrap (factory-injection pattern)
- (b) Migrate `ComposeWorkspace` to `@spaarke/compose-components` (shared lib) — requires refactoring its dependencies
- (c) Accept Pattern D limitation indefinitely (Calendar precedent)

**Permanent home**: this file (registry) + W1b-040 / W4-042 / W5-042 POML notes + design.md (mount-path section)

---

## DEF-7SCs — 7 Success Criteria require live Dev BFF verification

**Source**: W9-070 success-criteria audit

The 7 SCs operator-deferred to W10/W11 (live verification post-deploy):

| SC | Criterion | Code/test in place | Live verification path |
|---|---|---|---|
| **SC4** | Path A modal launch works against real `sprk_document` | W6-046 launch-resolver + ribbon button + W5-042 modal | Operator: deploy ribbon button to Dev → click "Open in Compose" on a `sprk_document` → verify modal opens with `ComposeWorkspace` |
| **SC5** | Path B ephemeral upload works against deployed Assistant | W4-044 EmptyState CTA + W5-042 EmptyState handlers | Operator: deployed Assistant upload → "Open in Compose" → verify ephemeral mount |
| **SC9-live** | `compose-summarize` E2E against deployed BFF + real playbook | W8-060 in-process trace (7 regression tests) + smoke write-up §7 | Operator: live HTTP call against Dev BFF |
| **SC10-live** | Open-in-Word web + desktop buttons functional | W2-031 `useDocumentActions` + W4-043 Toolbar buttons | Operator: click each button against a deployed `sprk_document` |
| **SC13** | SPE check-out visible in Word for Web | n/a — known limitation per T-1 | **Documented as known R1 limitation (last-writer-wins)**; no live verification expected to pass |
| **SC14** | Multi-tab conflict UX (two-tab manual test) | W7-051 ConflictDialog + 12 component tests | Operator: open same doc in 2 Compose tabs → verify modal + Force-close flow |
| **SC15** | 15-min orphan lock wallclock test | W7-052 sweeper + 9 unit tests with `TimeProvider` | Operator: open Compose → close laptop → wait 17 min → verify lock released |

All 7 are normal "live verification belongs to operator post-deploy" items, not project gaps.

**Permanent home**: `notes/audits/success-criteria-audit.md` + W10 task 080/081 POMLs

---

## How to file new entries

Per project CLAUDE.md "Deferrals & Issues — tracking obligation":

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr that's not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Telemetry / monitoring gap requiring follow-up | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

**How to file**: Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH this file AND a GitHub issue in one step.

**CLAUDE.md §11 rule applies**: every entry must name a concrete behavior or contract that fails without the work.
