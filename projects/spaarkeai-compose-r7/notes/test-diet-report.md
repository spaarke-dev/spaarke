# Test diet report — spaarkeai-compose-r7

**Run date**: 2026-08-17
**Branch**: work/spaarkeai-compose-r7
**Scope**: tests touched between merge-base `749dd273e` (origin/master) and HEAD `2dc9dc293`
**Classifier**: ADR-038 §7 build-vs-maintain criteria (17-ban list B1–B17)
**Gate**: CLAUDE.md §7 project-close test-diet gate (BINDING, FR-B09). This report is cited by the 090 wrap-up PR.

---

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP — confirmed) | 37 files | keep (no removal) |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (R7-introduced) | 0 | — |
| **Total test files touched** | **37** | — |

**Net verdict: CLEAN DIET — nothing to delete or move.** Every R7 test delta tests real behavior at a sanctioned path. No `git rm` / `git mv` commands are emitted. This is expected: each of the 19 implementation tasks passed `adr-check` at its Step 9.5, and adr-check enforces the ADR-038 §7 bans on each task's test deltas — so scaffolding was rejected at authoring time, not accumulated to wrap-up.

---

## Scope note — two test conventions

R7 spans two test suites with different conventions; both are classified below:

1. **Server C# (xUnit) under `tests/**`** — the classifier's native target. KEEP paths per ADR-038 §7 + E-40: `tests/integration/{auth,regression,data-mutation,tenant,contract,seam}/**`, `tests/unit/domain/**`, and (per root CLAUDE.md §10 bullet 6, the sanctioned home for BFF service tests) `tests/unit/Sprk.Bff.Api.Tests/**`.
2. **Client jest (TS/TSX) co-located under `src/client/shared/**`** — the Compose/UI convention (test file next to source, standalone-jest vs CI-only split). ADR-038's literal `tests/**` KEEP-paths do not apply to co-located client tests; these are classified **by behavior** (do they exercise real branching / lock a regression?), which is the substance the ban-list protects.

---

## Delete commands

_None. No file or method classified SCAFFOLDING._

## Path-move commands

_None. No R7-introduced path violation. (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ComposePdfIntakeSourceTests.cs` sits at the §10-sanctioned BFF-unit path shared by the entire pre-existing BFF unit suite — not an R7 path violation.)_

## Ambiguous — reviewer judgment

_None._

---

## Maintain — confirmed (no action)

### NEW server C# tests (2)

| File | KEEP path | Why maintain |
|---|---|---|
| `tests/integration/seam/Compose/ComposeMountPdfProjectionSeamTests.cs` | integration/seam | Task 050 — 3 seam tests for the async `ProjectForMount` PDF fork + `Content`-echo correctness. Vertical-slice seam behavior (E-40 KEEP category). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ComposePdfIntakeSourceTests.cs` | unit/Sprk.Bff.Api.Tests (§10) | Task 073/FR-11 — asserts cause-specific messages (circuit-open vs timeout vs corrupt) + status mapping (Corrupt→422 else→503). Behavioral, not mirror/wiring. |

### MODIFIED server C# tests (15)

All are updates to existing **maintained** behavior-tests at KEEP paths — driven by two legitimate production changes + the 075 test-hygiene batch. No new scaffolding introduced:
- **013 data-path migration** (`CreateAsync`→`UpsertAsync` atomic promote): `ComposeServiceCreateOnSaveTests.cs`, `ComposeServicePromoteRecordCompletenessTests.cs`, `ComposeContentDedupTests.cs`, `ComposeCreateOnSaveEndpointContractTests.cs`, `PromoteDurableFkVisibilityTests.cs`.
- **FR-11 facade migration** (`ParseAsync`→`ParseWithDiagnosticsAsync`): `ComposePdfIntakeRoundTripSeamTests.cs`, `ComposeFidelitySeamTests.cs`, `ComposeUploadFidelityTests.cs`, `ComposeOriginRoutingSeamTests.cs`, `ComposeTemplateChromeProvenanceSeamTests.cs`, `ComposeTransientKeyDedupSeamTests.cs`.
- **075 test-hygiene batch**: `ComposeServiceCreateOnSaveTests.cs` (FakeTimeProvider flake fix), Notifications seam tests (`OutboxServiceSeamTests.cs`, `PendingPollFallbackSeamTests.cs`, `SignalRDeliverySeamTests.cs` — seam tighten), `nda-interrupted-clauses.docx` fixture (paraId regen, not a test method).

### NEW client jest tests (10)

| File | Class | Why maintain |
|---|---|---|
| `composeDraftStore.test.ts` | MAINTAIN | 10 tests of localStorage set/get/clear + id-match gate (client draft persistence behavior). |
| `composeHotkeys.test.ts` | MAINTAIN | 20 pure-predicate tests: IME guard (`isComposing` + legacy keyCode 229), Ctrl/Cmd disambiguation, Shift split between describe-change and focus-chat. Real branching. |
| `composeIdentity.test.ts` | MAINTAIN | logical-id resolution + fork-name uniquify helpers (FR-07 dedup key behavior). |
| `ComposeSaveNameDialog.test.tsx` | MAINTAIN | 13 tests of the FormModal name/filename contract (FR-02). |
| `ComposeEditor.blankPageEditable.test.tsx` | MAINTAIN | D8 regression guard — asserts blank `<p></p>` mounts editable (textbox present, no reference-only), template parity, and non-docx→reference-only negative case. Locks a fragile mount-branch. |
| `ComposeEditor.describeChangeHotkey.test.tsx` | MAINTAIN | Ctrl+Space caret describe-change wiring (FR-04, CI-only). |
| `ComposeEditor.focusChatHotkey.test.tsx` | MAINTAIN | Ctrl+Shift+Space focus-chat emission (FR-05, CI-only). |
| `ComposeWorkspace.draftAutosave.test.tsx` | MAINTAIN | client-only autosave dirty-tick behavior (FR-03, CI-only). |
| `ComposeWorkspace.reloadFromSource.reducer.test.ts` | MAINTAIN | 3 reducer tests — `loadSucceeded` stamps `driveId` (D4 regression: reload no longer blanks). |
| `SprkChatInput.focusInput.test.tsx` | MAINTAIN | `focusInput()` imperative-handle contract (FR-05, CI-only). |

### MODIFIED client jest tests (10)

Updates to existing maintained tests reflecting the shipped behavior changes (Save dropdown, autosave invariant flip, Add-Comment toolbar, sourceFormat-aware editable gate, identity plumbing): `ComposeFormatToolbar.test.tsx` (+5 Add-Comment tests), `ComposeWorkspace.unmountFlush.test.tsx` (docblock reconciled to the Path-A invariant flip; assertions unchanged), `ComposeEditor.referenceOnly.test.tsx`, `ComposeWorkspace.bornInEditorSave.test.tsx`, `ComposeWorkspace.imports.test.tsx`, `ComposeWorkspace.renderOnSave.reducer.test.ts`, `ComposeWorkspace.renderOnSave.test.tsx`, `ComposeWorkspace.saveOpLogPreservation.test.tsx`, `ComposeWorkspace.search.test.tsx`, `stepOperationInterceptor.test.ts`. All behavioral; no scaffolding introduced.

---

## Count delta

- Test files touched during project: **37** (12 new, 25 modified)
- Classified MAINTAIN: **37**
- Classified SCAFFOLDING: **0**
- Classified AMBIGUOUS: **0**
- Net post-diet expected count: **unchanged** (0 reviewer-confirmed deletes)

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. Client co-located jest tests classified by behavioral substance (the property the ban-list protects) since ADR-038's literal `tests/**` KEEP-paths address the C# suite only.

---

## Delta reconciliation — sessions 3–4 (2026-08-19)

The original diet (above, 2026-08-17) covered the 20-task set. Sessions 3–4 (R-5 Cosmos fix, banner unification, 1b file re-attach) added/modified 3 r7-owned test surfaces AFTER the wrap-up. Re-classified here (the many other test files touched in the branch since Aug 18 are OTHER projects' tests pulled in via master merges — not r7's, excluded):

| Test | Change | Classification | Verdict |
|---|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CosmosPersistenceSerializerTests.cs` | NEW (R-5) | **MAINTAIN** — behavioral regression guard binding to the PRODUCTION serializer options (`AiPersistenceModule.CosmosJsonSerializerOptions`); locks the `ttl:null`→400 write-outage fix + the test-vs-prod serializer gap. Not a ban-list target (no ctor-null / DI-registration / `Mock<HttpMessageHandler>`; asserts real serialized output). | **KEEP** |
| `src/solutions/SpaarkeAi/.../__tests__/ConversationPaneChrome.files-availability.test.tsx` | NEW (1b) | **MAINTAIN** — user-facing component behavior (the "no longer available" 24h re-attach chip variant + back-compat); deterministic, no scaffolding. | **KEEP** |
| `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.test.tsx` | MODIFIED (banner) | **MAINTAIN** — harness updated so the existing "surfaces the unresolved-target banner" behavior test still exercises the notice end-to-end after it moved to the host rail (`onRedlineErrorChange`). Preserves an existing MAINTAIN test; no new scaffolding. | **KEEP** |

**Delta totals**: +2 new MAINTAIN, 1 modified MAINTAIN, **0 SCAFFOLDING, 0 AMBIGUOUS, 0 deletes**. Combined project total: **MAINTAIN 39, SCAFFOLDING 0**. Net post-diet count unchanged. **Project-close gate: CLEAN** (§7 / ADR-038 §7).
