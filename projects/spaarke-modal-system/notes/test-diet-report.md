# Test diet report — spaarke-modal-system

**Run date**: 2026-08-02
**Branch**: work/spaarke-modal-system
**Scope**: 38 test-related files added/modified between `origin/master` and HEAD (enumerated via `git log --name-only --diff-filter=AM`)

**Classifier adaptation (documented per skill Step 8)**: ADR-038 §7's B1–B17 bans applied in spirit; the 6 canonical KEEP paths are .NET/`tests/**`-specific — this project is CLIENT-ONLY (jest), where the repo's established KEEP-equivalent is the colocated `__tests__/` convention used across `Spaarke.UI.Components` (185+ pre-existing suites). No `tests/**/*.cs` was touched.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (keep at colocated path) | 35 files | confirmed |
| SCAFFOLDING (delete candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 1 file | listed below |
| Test infrastructure (not tests) | 2 files | keep (required by MAINTAIN suites) |
| **Total touched** | **38** | — |

## MAINTAIN — new suites authored by this project (all behavioral, all colocated)

- `SprkModal/__tests__/{sizes,scaledTheme,SprkModal,a11y,uiScale}.test.*` + `presets/__tests__/{Confirm,Choice,Form,Preview,Browse,Wizard}Modal.test.tsx` — the shipped library's contract suite (~130 tests): size math, dismiss semantics (light/explicit/alert incl. real ESC), transform-robust portal structure, aria wiring, scale derivation, busy/submitDisabled/cancelLabel/headerActions. Fails on real regression; no B1–B17 match.
- `ChoiceDialog/__tests__/ChoiceDialog.test.tsx` (12) — the ADR-023 adapter contract (2/3/4 choices, selection/cancel, cancelText wiring, chrome provenance).
- `SprkChat/__tests__/actionConfirmationIntegration.test.tsx` (5) — end-to-end SSE → ConfirmModal integration replacing the retired overlay's coverage; determinism-hardened for full-suite load (30s budgets, in-act network release, MessageChannel polyfill, retryTimes(2) with logged first attempts — full root-cause history in `task-042-completion.md`). High-value; the retry absorber is documented, first-attempt failures remain visible, a real regression fails all 3 attempts.
- `CommunicationConversationPanel/__tests__/ConversationModal.transform.test.tsx` — the FR-08 transform-robust-centering proof under real React 16; the load-bearing invariant test for the whole shell.
- Modified assertions in `FilePreview/__tests__/*` (chrome provenance under the presets).

## MAINTAIN — pre-existing suites of OTHER projects, modified only to stay green

`PinnedMemory{DeleteConfirmation,EditDialog,ListWidget}.test.tsx` (role-query migrations), `SpaarkeAi ComposeConflictDialog.test.tsx` (1 accessible-name fix), `ComposeWorkspace.*.test.tsx` ×4 (added the same `SprkModal: () => null` stub the files' own SendEmailDialog precedent uses), `DailyBriefing` ×2 + its `__mocks__` and `CommunicationConnections`/`RegardingResolver`/`SemanticSearch EntityRecordDialog` + 2 `jest.config.js` (task-090 mock/module-mapping updates for the new adapter export), `EmailComposer/__tests__/openEmail{Compose,Record}.test.ts` (090 repoints). Not this project's tests to delete; all confirmed green at their packages' baselines.

## Test infrastructure (not test methods)

- `SprkChat/__tests__/setupMessageChannelPolyfill.ts` — environment fix (React 19 scheduler starvation on jsdom; unref'd + ref-pinned Node MessageChannel). Required by the integration suite.
- `CommunicationConversationPanel/__tests__/pcfSafeShim.ts` — jest resolution shim for the `dist/pcf-safe` entry.

## AMBIGUOUS — reviewer judgment (1)

- `utils/adapters/__tests__/oobModalSizes.test.ts` — asserts the three named OOB sizes equal their owner-locked values (B6-adjacent: constant vs literal). **Recommendation: KEEP** — these are contract values consumed by ~50 launch sites; silent drift is a real cross-app UX regression, and the test is the only guard. If the reviewer rules it B6, the removal command is:
  ```bash
  git rm src/client/shared/Spaarke.UI.Components/src/utils/adapters/__tests__/oobModalSizes.test.ts
  ```

## Delete commands

None — zero SCAFFOLDING-class findings. The project authored no characterization throwaways, no `Mock<HttpMessageHandler>`-class wiring tests, no DI/ctor tests, no coverage filler; every new suite asserts user-observable behavior of shipped components.
