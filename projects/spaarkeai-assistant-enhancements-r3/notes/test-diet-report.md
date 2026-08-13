# Test diet report — spaarkeai-assistant-enhancements-r3

**Run date**: 2026-08-13
**Branch**: work/spaarkeai-assistant-enhancements-r3
**Scope**: 33 test files touched during R3 (derived via `git log --no-merges --name-only --pretty=format: --grep="assistant-r3\|QW1\|QW2\|reconcile merge" | grep -iE "test|Tests" | sort -u`) — 12 BFF `.cs` files (11 unit + 1 integration) and 21 client `.test.ts(x)` files.
**Gate**: ADR-038 §7 (17-ban build-vs-maintain classifier) / `tests/CLAUDE.md` "Expect to Defend at Project Close" / spec FR-B09.

**Scope note on JS/TS**: ADR-038's Domain section states it applies to `tests/unit/**` and `tests/integration/**` (.NET) and explicitly does **not** apply to React/PCF Jest tests ("separate framework, separate ADR if needed in the future"). The 21 client `.test.ts(x)` files below are classified using the *same three questions* (behavior broken on delete / KEEP-path-equivalent / caller-observable vs. hidden-implementation) as a matter of consistency, but no B-number is cited for JS findings since B1–B17 are C#-specific. No formal KEEP-path taxonomy exists for `src/client/**`/`src/solutions/**` today — file-location "PATH-VIOLATION" is therefore not raised for JS files.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP, confirmed) | 33 files (30 fully; 3 with 1 flagged method each) | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 whole files; 3 individual methods (B3) | see below — recommend deferral, not isolated deletion |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 hard; 1 soft recommendation (`StatedProfileSecurityTests.cs`) | see note below |
| **Total files scoped** | **33** | — |

**Headline finding: zero DELETE candidates at file level.** Every R3-authored/modified test file in scope earns its place — each protects a concrete, named production behavior (an ADR invariant, a privacy/security boundary, a contract between client and server, or branched business logic), not a coverage-filler or mirror test. This is a well-disciplined test suite for a project of this size.

The only findings are three **method-level** B3 (DI-registration test) flags inside otherwise-strong files, and one soft path-relocation suggestion. No `git rm` is warranted.

---

## BFF `.cs` — unit tests (`tests/unit/Sprk.Bff.Api.Tests/**`)

| File | Classification | Rationale / behavior that breaks if deleted |
|---|---|---|
| `Api/Ai/ChatEndpointsMapStoredTabsTests.cs` | **MAINTAIN** | Anchors the FR-03 stored-tab → live-tab mapping (`ChatEndpoints.MapStoredTabsToWorkspaceTabs`) including the layout-tab-by-DisplayName re-point and order-preservation via synthetic timestamps. Delete → the client `PATCH /sessions/{id}/tabs` → prompt round-trip silently breaks (tabs vanish from the agent's workspace state or arrive out of order). `internal` access is a documented, deliberate seam (see class doc comment) — not a B8 private-reflection violation. |
| `Services/Ai/Chat/AgentTurnLoopContractTests.cs` | **MAINTAIN** | Large, high-value contract suite: per-turn tool budget enforcement, NFR-07 argument redaction (content vs. identifier), citation-enforcement/repair-suffix (ADR-039 grounded outputs), ToolChain ledger persistence bookkeeping (ADR-040), prompt-cache-stable fingerprinting (NFR-04), and the FR-12 tab-economy pre-filter. Every assertion is behavioral (state after an action), not interaction-shape. Delete → silent regression on tool-budget bypass, PII leaking into the ToolChain audit log, or uncited claims shipping ungrounded. |
| `Services/Ai/Chat/PreferenceNotPermissionInvariantTests.cs` | **MAINTAIN** | See "Special attention" note below — this is the ADR-039 "preference ≠ permission" governance guard. |
| `Services/Ai/Chat/SprkChatAgentFactoryWorkspaceStateTests.cs` | **MAINTAIN** | The FR-03/FR-04/ADR-015 privacy-trim contract for `BuildWorkspaceStateBlock` — asserts the emitted prompt fragment carries ONLY `{type,label,active}` per tab + one `{id,type,label}` active-item handle, and explicitly proves ~16 named content probes (body/tldr/selectionText/snippet/etc.) never leak. This is a genuine content-exfiltration regression protector, not a mirror test. |
| `Services/Ai/Chat/WidgetContextTypeResolverTests.cs` | **MAINTAIN** | Pure domain-logic mapping (widgetType → closed `WidgetContextType`) feeding the FR-12 tool-economy pre-filter; named-scenario `[Theory]` cases, real assertions. Delete → tab-economy pre-filter silently mis-scopes capabilities. |
| `Services/Ai/Context/ContextBinderOrgContextTests.cs` | **MAINTAIN** | FR-E5 BU/team block composition order + budget + soft-fail-to-null on reader failure + the ADR-039 preference-only pin (org context never reaches `AgentToolFilterContext`). Real security/architecture invariant. |
| `Services/Ai/Context/StatedProfileSecurityTests.cs` | **MAINTAIN** (soft path-relocation candidate — see below) | Two load-bearing security invariants: (1) cross-user isolation — captures the actual Dataverse `QueryExpression` and proves the profile read is keyed ONLY by the caller-supplied `systemuserid`; (2) preference≠permission under an explicit prompt-injection payload — proves injected text cannot manufacture a dispatch operand and never reaches the grounding pre-filter. This is genuine authZ/prompt-injection defense, not a mirror or wiring test. |
| `Services/Ai/Handlers/DailyBriefingOverviewHandlerTests.cs` | **MAINTAIN** (1 method flagged) | Happy path against a REAL `BriefingService` (not a full mock), failure-soft degrade, cancellation, missing-user fail-closed, and an ADR-015 telemetry test that captures actual log output and asserts matter names/narrative never appear. Strong file. One method — see B3 flag below. |
| `Services/Ai/Handlers/EmailDraftToolHandlerPerItemToolsTests.cs` | **MAINTAIN** | FR-09/FR-10 per-item email tools: proves the draft is authored-body-only (never embeds the quoted thread — client-owns-thread invariant), OBO fail-closed on access-denied (no AI call made), ADR-015 telemetry, and catalog-row JSON contract tests (`CatalogRows_*`) that read real `infra/dataverse/*.json` files and assert the handler-class/method-discriminator/context/side-effect-class fields — genuine drift protection between code and catalog data. No bans found. |
| `Services/Ai/Handlers/EmailDraftToolHandlerTests.cs` | **MAINTAIN** (1 method flagged) | The DRAFT-ONLY invariant group is the load-bearing core: server-pinned `statuscode=1`, a hostile-args test proving no argument vocabulary can escalate to "sent", exactly one metadata-GET + one POST (`VerifyNoOtherCalls`), regarding-association mapping, OBO access-denied surfacing. Excellent security-adjacent suite. One method — see B3 flag below. |
| `Services/Ai/Handlers/GridOverviewHandlerTests.cs` | **MAINTAIN** (1 method flagged) | FR-06 parameterized overview tool: server-side `{{today}}` injection (never client-supplied, never `GETDATE()`), OBO row-level denial pass-through, record-id citations, ADR-015 telemetry. Real regression coverage for a documented live UAT defect (R2 `GETDATE()` rejection). One method — see B3 flag below. |

### Special attention — `PreferenceNotPermissionInvariantTests.cs` reflection tests

This file uses `System.Reflection` over **public** members (`GetProperties`, `GetConstructors`) to prove `AgentToolFilterContext` carries no profile/memory-derived member and no profile construction channel exists. This is **not** a B8 violation: B8 bans reflecting into `BindingFlags.NonPublic` / private members to test implementation details through a back door. Here the reflection targets the **public** surface as an architectural fitness-function — the same category ADR-038 §5 names as the accepted replacement for lost DI-registration signal ("NetArchTest-style architecture tests"). The calibration test (`ProfileMemoryDenyList_CatchesEveryStatedProfileMemberName...`) is a live tripwire, not a vacuous check: it dynamically re-derives the current `StatedProfile` member list via reflection and asserts every one is caught by the deny-list, so it automatically re-fires if a new profile field is ever added without updating the guard. **Verdict: KEEP.**

### Special attention — `CatalogToolDescriptionParity` / registration-contract-enforcement style tests

No file literally named `CatalogToolDescriptionParity*` exists in the R3 diff; the closest R3-scope equivalents are the catalog-row JSON contract tests inside `EmailDraftToolHandlerPerItemToolsTests.cs` (`CatalogRows_ThreeNewEmailTools_...`, `CatalogRows_AllEmailDraftHandlerRows_...`) and the client-side `registration-contract-enforcement.test.ts` (below). Both read REAL catalog/registry data and assert a structural contract holds across every row/entry — these are genuine contract-anchor tests. **Verdict: KEEP.**

### B3 flag — `HandlerType_IsRegisteredInDi` (3 occurrences)

`DailyBriefingOverviewHandlerTests.cs`, `EmailDraftToolHandlerTests.cs`, and `GridOverviewHandlerTests.cs` each carry one method matching ADR-038 B3 verbatim:

```csharp
[Fact]
public void HandlerType_IsRegisteredInDi()
{
    ...
    services.Where(d => d.ServiceType == typeof(IToolHandler) && ...)
        .Should().Contain(typeof(XyzHandler), because: "the handler must be auto-discovered by the assembly scan (ADR-010)");
}
```

This is textbook B3 (`Assert.NotNull(services.GetRequiredService<X>())`-shape — asserts wiring, not behavior). **However**: this is not an R3-introduced pattern — it is the repo-wide `HandlerContractTestTemplate` convention (explicitly named in `EmailDraftToolHandlerTests.cs`'s docblock: "4-point contract tests (HandlerContractTestTemplate, retargeted)") replicated across dozens of pre-existing BFF handler test files outside R3's diff. Deleting it in only these 3 R3 files would desynchronize them from the ~50+ sibling handler test files still carrying the identical method, without fixing the underlying pattern. Per ADR-038 §7 "How this list is used" bullet 3, retroactive sweeps of an established repo-wide pattern belong to a dedicated Phase-2.5-style project task, not a per-project diet pass.

**Recommendation**: do not delete in isolation. Flag for the next repo-wide BFF-handler-test sweep. If the reviewer wants to act now anyway, the 3 methods are safe to remove individually (see commands below) — each file's `Handler_IsDiscoverableByHandlerClassName` test already covers the adjacent, load-bearing "HandlerId == nameof(handler class)" runtime-routing contract, so removing `HandlerType_IsRegisteredInDi` loses no unique signal beyond the wiring assertion itself.

---

## BFF `.cs` — integration test (`tests/integration/Spe.Integration.Tests/**`)

| File | Classification | Rationale |
|---|---|---|
| `Workspace/Pillar9PrivacyFilterTests.cs` | **MAINTAIN** | End-to-end FR-58/FR-59 privacy-default proof through the real `BuildWorkspaceStateBlock`/`TryDeriveVisibleState` pipeline: 3-tab scenario (visible+state / visible+no-state / hidden+state) proves only the correctly-gated tab surfaces, with explicit non-visible filename/selectionText leak probes. This is exactly the "vertical-slice, not mock-trust" category ADR-038 §2's `seam` KEEP path describes in spirit (file predates that path's formalization but the class doc comment states the rationale explicitly: "Why this lives in Spe.Integration.Tests ... a regression on either side of the filter is caught here, not via mock-trust"). |

---

## Client `.test.ts(x)` — Spaarke.AI.Widgets

| File | Classification | Rationale |
|---|---|---|
| `src/__tests__/registration-contract-enforcement.test.ts` | **MAINTAIN** | FR-15's structural guard: every registered widget must declare a real `assistantContract` or an explicit opt-out; proves the runtime `throw` fires at all 5 registration call-sites when the field is missing/malformed, and that real per-item/overview contracts are unchanged. High-value, genuine runtime-invariant coverage. |
| `src/__tests__/WorkspaceWidgetRegistry.interactionPattern.test.ts` | **MAINTAIN** | FR-13: `getWidgetInteractionPattern` is the single read-point, and the file proves the `respond`/`direct`/`hybrid` value is *consistent* with the widget's own `perItemCards[].landing` values for every registered widget — an authoritativeness invariant, not an independent guess. |
| `src/registry/__tests__/WorkspaceWidgetRegistry.test.ts` | **MAINTAIN** | Base registry mechanics (register/resolve/cache/replace/unknown-type-never-throws). Pre-existing file, only touched to add the new required `assistantContractOptOut(...)` fixture field for FR-15 compile-compat — not R3-authored scaffolding. |
| `src/widgets/workspace/__tests__/EmailWorkspaceWidget.test.tsx` | **MAINTAIN** | FR-C1 producer-wiring: proves the persisted `EmailTabWidgetData` carrier omits `snippet` (ADR-015 content walk-back), includes the transient `communicationId` bridge field, and no-ops when not mounted as a tab. Real defect-guard shape. |
| `src/widgets/workspace/__tests__/register-document-viewer-widget.test.ts` | **MAINTAIN** | Registration + FR-11 per-item card contract (labels, landing=chat for all three, `interactionPattern` correction from placeholder to `respond`). |
| `src/widgets/workspace/__tests__/register-search-criteria-result-widget.test.ts` | **MAINTAIN** | Registration + FR-15 explicit opt-out proof for a widget with no honest `contextType` fit. |
| `src/widgets/workspace/__tests__/register-structured-output-stream-widget.test.ts` | **MAINTAIN** | Same shape as above, for the structured-output-stream widget. |
| `src/widgets/workspace/__tests__/register-workspace-widgets.test.ts` | **MAINTAIN** | Large, well-organized suite: registration presence/metadata for 7 R1 widgets, factory resolution (proves it resolves to the REAL component, not the generic fallback — catches the exact mock-path-mismatch defect documented in the file's own "test-repair task 021" note), idempotency, the `communications-list` upgrade-in-place identity contract, the closed 6-value `contextType` union (compile-time exhaustiveness + runtime), and the FR-08/FR-15 assistant-contract shape matrix. No bans found. |

## Client `.test.ts(x)` — Spaarke.Communication.Components

| File | Classification | Rationale |
|---|---|---|
| `.../EmailComposeActions/__tests__/useEmailComposeActions.test.tsx` | **MAINTAIN** | Per-mode recipient prefill, FR-10 thread-preserving `bodyOverride` (with an explicit DEFECT-guard assertion that a whole-body-replace would be wrong), a NEGATIVE "no forked composer" test verified by React-element *identity* (not string match — a fork under a different name would fail this), real send-path assertion against the existing `/api/communications/send` route, and a dark-mode render-with-no-console-errors check. |
| `.../EmailWorkspace/__tests__/EmailWorkspace.mapping.test.ts` | **MAINTAIN** | Pins a real owner-UAT regression fix (review-dot wiring ignoring denormalized regarding columns) plus the FR-C1 `deriveEmailWorkspaceVisibleState` compact-carrier derivation (snippet cap, privacy-default omission on empty body, null-on-incomplete-identity). |
| `.../composerPrefill/__tests__/composerPrefill.bodyOverride.test.ts` | **MAINTAIN** | Pure-logic FR-10 BINDING invariant test with an explicit `NEGATIVE (defect guard)` case and a byte-for-byte no-override regression check. Textbook domain-logic KEEP-class test. |

## Client `.test.ts(x)` — Spaarke.UI.Components

| File | Classification | Rationale |
|---|---|---|
| `EmailComposer/__tests__/EmailComposer.quoted-thread-survives-resparkle.test.tsx` | **MAINTAIN** | D-5 fix proof at the component level, INCLUDING an explicit `REGRESSION GUARD` test that reproduces the pre-fix defect (thread dropped) to prove the test harness actually distinguishes fixed-vs-broken — a strong methodological signal this isn't a vacuous pass. |
| `EmailComposer/__tests__/EmailComposer.reducer.test.ts` | **MAINTAIN** | Pure reducer/state-machine coverage explicitly self-identified in its own docblock as "ADR-038 domain-logic behavior contracts (MAINTAIN-class), not scaffolding" — and the content backs that claim: per-mode `initialState` seeding, the full `SET_MODE` transition matrix, attachment/association reducers with real dedup/case-insensitivity edge cases, `mapStateToSendRequest` selection→payload mapping. |

## Client `.test.ts(x)` — SpaarkeAi solution

| File | Classification | Rationale |
|---|---|---|
| `conversation/__tests__/ConversationPane.document-per-item-cards.e2e.test.tsx` | **MAINTAIN** | FR-11 document per-item cards end-to-end: card render on active-item, correct instruction text armed per card, `onDecorateOutboundBody` id-only stamping (explicit ADR-015 negative assertions that `label`/`content`/`textContent` never appear), one-shot-consumption proof, suppression on deselect/non-document, dark-mode. |
| `conversation/__tests__/ConversationPane.email-per-item-cards.e2e.test.tsx` | **MAINTAIN** | FR-09/FR-10 email per-item cards end-to-end through the REAL `SendEmailDialog`/`EmailComposer` chain (only Xrm adapters mocked) — proves the AI draft request uses the TOOL-FETCHED body/subject, never the conduit's `label` (ADR-015), and that Summarize lands in chat with no composer opened. |
| `conversation/__tests__/followOnElementType.test.ts` | **MAINTAIN** | FR-14 deterministic card-vs-chip resolver — explicitly proves the resolver's *signature* structurally cannot read message text (no keyword-heuristic mis-fire possible), including an adversarial "smuggle an extra `text` field" case. |
| `conversation/__tests__/ProactiveCardStack.test.tsx` | **MAINTAIN** | UI-standard-compliance tests for the FR-14 disclosure-header collapse rule (0/1/2+ slot behavior, toggle state, no raw keys leaking into the human-readable label). |
| `workspace/__tests__/activeItemConduit.test.tsx` | **MAINTAIN** | The cross-pane conduit's 4 binding invariants (single-active-item replace-not-append, null clears, multi-select maps to null, content-free handle enforced both at compile time via `@ts-expect-error` and at runtime by dropping a smuggled field) plus off-provider null-safety. File's own docblock self-classifies correctly as "Component/Contract Test (KEEP)". |
| `workspace/__tests__/registerComposeWidget.test.ts` | **MAINTAIN** | Compose Direct-widget registration + the `composeWidgetVisibility` (Pillar 9) projection function across all three door variants (stored/upload/draft) plus null-safety on missing/malformed payloads. |
| `workspace/__tests__/WorkspacePane.document-active-item.test.tsx` | **MAINTAIN** | `deriveActiveItemHandle` tab-focus branch — id/type/label-only enforcement (negative test that `textContent` never reaches the handle even when present on `widgetData`), the D-3 synchronous-vs-async race non-recurrence proof, legacy-dispatch-site fallback, and a regression check that the sibling compose branch is unaffected. |
| `workspace/__tests__/WorkspacePane.email-active-item.test.tsx` | **MAINTAIN** | `deriveEmailActiveItemFromPatch` bridge — content-free handle enforcement, persistable-patch strips the transient `communicationId` field, deselect clears without clobbering the persisted carrier, non-email-widget/no-id/non-object negative cases. |

---

## Soft path-relocation note (not a hard finding)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Context/StatedProfileSecurityTests.cs` tests cross-user isolation and prompt-injection defense — content that matches ADR-038's `tests/integration/auth/**` (security-auth) KEEP-path definition more closely than its current `tests/unit/**` location. This is **not** flagged as a hard PATH-VIOLATION because the entire `tests/unit/Sprk.Bff.Api.Tests/**` project (576 `.cs` files) predates the ADR-038 path reorganization and has not yet been split into the 7 canonical KEEP-path directories (`tests/unit/domain/**` does not currently host any of the ~576 files; `tests/CLAUDE.md` itself notes the reorg as "after task 050 path reorganization completes"). Moving one R3 file in isolation would not align it with any real canonical destination yet and would just orphan it. No `git mv` is emitted — this is a note for whenever the repo-wide reorg task runs.

---

## Delete / relocation commands (emitted per skill contract — READ-ONLY, reviewer executes)

No whole-file deletions or moves are warranted. If the reviewer chooses to act on the deferred B3 flags now (see rationale above — recommended only as part of a repo-wide sweep, not in isolation):

```bash
# OPTIONAL, method-level only — NOT recommended in isolation (see B3 flag rationale above).
# Use the Edit tool to remove just the HandlerType_IsRegisteredInDi [Fact] method from:
#   tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DailyBriefingOverviewHandlerTests.cs
#   tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EmailDraftToolHandlerTests.cs
#   tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/GridOverviewHandlerTests.cs
# No git rm / git mv needed — no whole file qualifies for deletion or relocation.
```

## Count delta

- Tests files touched during project: 33 (12 BFF `.cs`, 21 client `.test.ts(x)`)
- Files classified MAINTAIN: 33 (30 clean; 3 with one flagged method each)
- Files classified SCAFFOLDING (whole-file delete): 0
- Files classified AMBIGUOUS: 0
- Individual methods flagged B3 (deferred, not deleted): 3
- Net post-diet expected file count: 33 (unchanged)

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17.
