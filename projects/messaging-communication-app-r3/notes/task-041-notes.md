# Task 041 — Thread pin/favorite UI + persistence (FR-24)

**Status**: implemented, gated, verified. Reuses the task-012 `ThreadList` component (no fork); persists via a
narrow new BFF endpoint mirroring the existing task-004 rename endpoint.

## Field reused (task 040)

`sprk_ispinned` (Boolean) on `sprk_communicationthread`, created live in spaarkedev1 by task 040. Per task-040's
notes, **pre-existing rows read back `null`, not `false`** — every read path in this task normalizes that to
`false` (see below), so no consumer ever observes a three-state value.

## 1. Files changed

**Backend (BFF)**
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs` — added `IsPinned` to
  `ThreadReadResult` (default `false`) and `ThreadListItem` (required).
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` — added `PinnedField`
  const; `ListThreadsAsync` and `ReadByRegardingAsync` now `$select` `sprk_ispinned` and populate `IsPinned` via
  `TryBool(r, PinnedField) ?? false` (the null→false normalization).
- `src/server/api/Sprk.Bff.Api/Services/Communication/IThreadResolver.cs` — added `SetPinnedAsync`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/ThreadResolver.cs` — implemented `SetPinnedAsync` (one
  `UpdateAsync` write, not best-effort — mirrors `RenameThreadAsync`).
- `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SetThreadPinnedRequest.cs` (new),
  `SetThreadPinnedResponse.cs` (new).
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` — added `PATCH
  /api/communications/threads/{threadId}/pin` (`SetThreadPinnedAsync` handler), registered alongside the
  existing rename endpoint.

**Tests (BFF)**
- `tests/integration/contract/Api/Communication/CommunicationSetThreadPinnedContractTests.cs` (new) — 401/403/200
  pin/200 unpin, mirroring `CommunicationRenameThreadContractTests`'s shape. **Deliberately not
  `IClassFixture`-shared** (see file header) — a shared factory across test methods caused a cross-test Moq
  Setup-bleed on the wildcard-matched visibility mock (discovered empirically while gating this task; see §7
  below), so each test method gets its own fresh `WebApplicationFactory`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ThreadResolverTests.cs` — added `SetPinnedAsync_WithTrue_…`
  / `SetPinnedAsync_WithFalse_…`.

**Frontend (shared lib)**
- `src/client/shared/Spaarke.UI.Components/src/services/communicationThreadListApi.ts` — `IThreadListItemDto`
  gained `isPinned: boolean`; `listThreadsByRegarding`'s adapter maps the by-regarding response's `isPinned` (with
  a defensive `!!`); added `setThreadPinned(threadId, pinned, client)` (`PATCH .../pin`).
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/subcomponents/ThreadList.tsx` —
  `IThreadListRow.isPinned` + `IThreadListProps.onTogglePin`; renders a `PinFilled`/`PinRegular` toggle button per
  row (`aria-pressed`, keyboard-reachable native `<button>`, `e.stopPropagation()` so it never also selects the
  row).
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/ConversationWorkspace.tsx` —
  `handleTogglePin` (optimistic update + rollback), `threadListRows` memo now sorts pinned-to-top (stable sort).
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/__tests__/ConversationWorkspace.test.tsx`
  — 5 new tests under "pin/unpin (task 041, FR-24)".

## 2. Reuse — no fork, no second widget

The pin toggle was added directly inside the existing task-012 `ThreadList.tsx` row render and wired through the
existing `ConversationWorkspace.tsx` shell — no new component, no new widget, no new `type`/section-id surface.
`communications-list` / `communications` (NFR-06) were never touched.

**Null-as-unpinned**: normalized server-side (BFF) at the read layer — `TryBool(...) ?? false` in
`CommunicationThreadReadService` — so `ThreadListItem.IsPinned` / `ThreadReadResult.IsPinned` are always a definite
`bool` on the wire. The client types `isPinned: boolean` (never `null`/`undefined` per the BFF contract) but still
defensively coerces with `!!` in two places (the by-regarding adapter, and `ThreadList`'s row render) as
belt-and-braces against any future contract drift — the task-040 caveat is treated as a standing hazard, not a
one-time fix.

## 3. Persistence path — BFF endpoint (not `Xrm.WebApi`)

**Decision**: `PATCH /api/communications/threads/{threadId}/pin`, called via the existing
`communicationThreadListApi.ts` module's `authenticatedFetch` (ADR-028) — the SAME pattern every other write in
this module already uses (`startDirectThread`).

**Per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`**: on the decision-tree's own terms this looks like a
single-entity write to one Dataverse table with no cross-system coupling — the profile that usually favors
`Xrm.WebApi`. It does NOT here, for a concrete, codebase-specific reason:

- **Criterion 1 (auth model)** — `Xrm.WebApi` requires a host Dataverse session. `ConversationWorkspace` /
  `ThreadList` are mount-agnostic (task 012): the SAME component mounts inside a PCF (host session present), the
  SpaarkeAi workspace widget (host session present), **and a standalone Vite code page** (`sprk_communicationconversationpage`,
  task 032 — no Dataverse host session; auth is MSAL-only). A pin write from this shared component MUST work
  identically in all three mounts (ADR-012: mount-agnostic, no `Xrm` import anywhere in this component tree). Only
  the BFF path is universally available across all three.
- **Existing overlap** — every OTHER write this module already performs (`startDirectThread`) and the sibling
  rename endpoint (task 004) both already go through the BFF for exactly this reason. A pin write via `Xrm.WebApi`
  would be the ONLY write in this component that silently breaks in the standalone code-page mount — an
  inconsistent, mount-dependent contract the codebase does not otherwise have.
- **Extension** — extended the existing `CommunicationThreadReadService`/`ThreadResolver`/`CommunicationEndpoints`
  trio (the SAME three files task 004's rename endpoint lives in) with one new endpoint + one new resolver method,
  rather than inventing a new service/abstraction.
- **Cost of doing nothing** — without a BFF write path, FR-24 has a field (task 040) and read exposure (this task)
  but pin/unpin literally cannot function in the standalone code-page mount — acceptance criterion 2 ("state
  persists... survives a full reload") fails there.

### Placement Justification (CLAUDE.md §10)

- **Existing** — `CommunicationEndpoints.cs` already owns `/api/communications/threads/{threadId}/rename` (task
  004) on the exact same `IThreadResolver`/`CommunicationThreadReadService` pair this task extends. No competing
  thread-mutation surface exists elsewhere in the BFF (`Grep` for `sprk_ispinned` before this task: zero hits).
- **Extension** — extended `IThreadResolver` with `SetPinnedAsync` (one new interface member, one new
  implementation method) and `CommunicationEndpoints` with one new `MapPatch` route + handler. Reused
  `CommunicationThreadReadService.CanCallerSeeThreadAsync` (already public, already used by rename) for the 403
  authorization check — zero new authorization code.
- **Cost of doing nothing** — see above (standalone code-page mount breaks; FR-24 acceptance criterion 2 fails).
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors (20 pre-existing warnings, unrelated).
- **Publish size**: `dotnet publish -c Release` compressed (tar.gz) = **~46 MB incl. PDBs / ~45 MB excl.** — at the
  project's stated ~46 MB baseline (root CLAUDE.md's most recent whole-repo baseline is ~49.63 MB incl. PDBs from a
  different project's task); **well under the 60 MB hard ceiling**, negligible delta (no new NuGet packages — two
  new small `.cs` model files + a handful of method additions). No escalation required.
- **CVE scan**: `dotnet list package --vulnerable --include-transitive` shows ONE pre-existing HIGH advisory set on
  `System.Security.Cryptography.Xml` 8.0.3 (5 GHSA entries) — **pre-existing on the baseline, not introduced by
  this task** (this task added zero package references). **0 new HIGH CVEs.**
- **Tests**: `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ThreadResolverTests.cs` (2 new unit tests) +
  `tests/integration/contract/Api/Communication/CommunicationSetThreadPinnedContractTests.cs` (4 new contract
  tests, closed set: 401/403/200-pin/200-unpin) satisfy the CLAUDE.md §10 bullet-6 test-update obligation.

## 4. Optimistic UI + rollback

`ConversationWorkspace.handleTogglePin`:
1. Reads the row's current `isPinned` from `allRows` (pre-toggle value captured before the state update, not
   inside the `setAllRows` updater — avoids a React 18 Strict-Mode double-invoke side-effect hazard).
2. Optimistically flips `allRows[threadId].isPinned` to the desired value immediately (synchronous `setAllRows`).
3. Fires `setThreadPinned(threadId, nextPinned, client)`.
4. On rejection (any non-2xx, including the 403 "caller cannot see thread" case, or a network failure): reverts
   `allRows[threadId].isPinned` to the captured pre-toggle value AND forwards the error through the same
   `onError` seam the list-load path already uses (so a host that surfaces list-load errors also surfaces pin
   failures, without a second error-reporting contract).

Verified by test: `rolls back the optimistic pin state when the PATCH fails` — asserts the immediate optimistic
flip, then the reverted `aria-pressed` after rejection, then `onError` was called.

## 5. Sort/mark + unread/word-filter preserved

`threadListRows` (the row-shape memo) now does `[...mapped].sort((a, b) => Number(!!b.isPinned) - Number(!!a.isPinned))`
— `Array.prototype.sort` is spec-guaranteed stable since ES2019 (Node 18/20, all evergreen browsers, jsdom), so
within the pinned group and within the unpinned group the PRE-EXISTING order is preserved byte-for-byte: server
`createdon desc` in all-mode, server order in record-mode. The sort runs AFTER `visibleRows` (the word-filter's
already-narrowed set), so a filtered-out thread never re-appears via the pin sort, and the unread-count map
(`unreadCounts`) is keyed by `threadId` and unaffected by row order. Verified by test:
`reflects the persisted pin state on load … and sorts the pinned thread to the top` (asserts DOM order via
`getAllByRole('listitem')`), plus the pre-existing word-filter tests (unchanged, still passing) confirm the filter
behavior itself was not touched.

**Deliberate non-scope-creep note**: default thread selection on load (`ConversationWorkspace`'s
`allRows[0]?.threadId` selection effect) still selects by the SERVER's natural order (createdon desc), NOT the
pin-sorted display order — the acceptance criteria only require the LIST's visual mark/sort, not a change to
which thread auto-opens. Not changing this avoided widening the task's surface into FR-01/FR-10 selection
semantics that task 041 does not own.

## 6. Build/test results

- Backend: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors. `dotnet test` scoped to `Communication` →
  **651 passed, 0 failed, 8 skipped** (skips are pre-existing/unrelated `InboundPipelineTests` /
  `CommunicationAccountServiceTests` / `CommunicationIntegrationTests`).
- Frontend: `npx tsc --noEmit -p tsconfig.json` (Spaarke.UI.Components) → **0 errors** (the "2 known pre-existing"
  errors cited in the task brief live in a DIFFERENT tsconfig scope — this package's own scoped check is clean).
  `npx jest --testPathPatterns="ConversationWorkspace"` → **19/19 passed** (14 pre-existing + 5 new pin tests,
  including the reload-persistence + null-unpinned-on-load test and the failure-rollback test). Full-package
  `npx jest` → 2264/2282 passed; the 18 failures across 9 suites are the documented pre-existing baseline
  failures (`WorkspaceShell`, `XrmDataverseClient`, `RichFilePreview`, `recordHeader`, `EntityCreationService`,
  `toolbarLaunchDefaults`, plus the known `ConversationView.forward`/`emailInFlow` Fluent-Dialog jsdom timing
  flakes) — **zero new failures** introduced by this task.
- `npx prettier --write` run on all 4 changed/created client files.

## 7. Step 9.5 quality gate — adversarial self-review + ADR-check

- **Pin persists + survives reload**: verified — `ThreadListItem.IsPinned` / `ThreadReadResult.IsPinned` are read
  fresh on every mount (no client cache), and the "reflects the persisted pin state on load" test simulates a
  fresh mount with server-supplied `isPinned:true`.
- **Null-as-unpinned**: handled at the BFF read layer (`TryBool(...) ?? false`), belt-and-braces `!!` on the
  client. No path where a pre-existing thread's Dataverse `null` reaches the UI as anything but "unpinned".
- **Unpin clears**: verified via the explicit "unpinning clears the pinned marker and PATCHes pinned:false" test
  + the backend `SetPinnedAsync_WithFalse_…` unit test (never relies on the Dataverse column default — always
  writes an explicit `false`).
- **Optimistic rollback on failure**: verified (see §4).
- **Pinned sorted/marked without breaking unread/word-filter**: verified (see §5); the pre-existing word-filter +
  unread-indicator tests were re-run unmodified and still pass.
- **ADR-012** (context-agnostic, no `Xrm`): confirmed — no `Xrm` import added anywhere; the persistence path is
  the SAME `authenticatedFetch`-only module every other write in this component tree already uses.
- **ADR-021** (Fluent v9 tokens, dark mode): confirmed — `PinRegular`/`PinFilled` (Fluent v9 icon set),
  `tokens.colorBrandForeground1` for the active-pin color, no hardcoded hex; dark-mode test unmodified and still
  passing (dark-mode render includes the new pin buttons without a snapshot regression).
- **ADR-028** (authenticatedFetch, no unauthenticated calls): confirmed — `setThreadPinned` uses the injected
  `client.authenticatedFetch` exactly like every sibling function in the module; the BFF endpoint requires auth
  (`RequireAuthorization()` on the route group) + the `CommunicationAuthorizationFilter`.
- **NFR-05** (keyboard + aria-pressed): confirmed — native `<Button>` (Tab-reachable, Enter/Space-activatable by
  the browser's native button semantics), `aria-pressed={isPinned}`, distinct `aria-label` ("Pin X" / "Unpin X").
- **NFR-06** (no 2nd thread-list/widget, identity retained): confirmed — `communications-list` / `communications`
  untouched; `ThreadList`/`ConversationWorkspace` extended in place, not forked.
- **§10/§11**: BFF Placement Justification stated above (§3); reuse-first — extended the existing rename-sibling
  endpoint trio rather than inventing a new service.
- **NEGATIVE — confirmed no archive/mute/tag control was added**: grepped the diff for `archive`/`mute`/`tag`
  outside doc comments — none found in the row/toggle UI; the "never renders an archive/mute/tag control" test
  asserts this at the DOM level (`aria-label`/text scan over every rendered button).
- **One Major found + fixed during self-review**: the FIRST draft of `CommunicationSetThreadPinnedContractTests`
  used `IClassFixture`-shared mocks (mirroring `CommunicationRenameThreadContractTests` literally). Running the
  full test class surfaced a genuine cross-test Moq Setup-bleed on the wildcard-matched
  `IImpersonatedCommunicationQuery` mock (last-`Setup()`-wins across test methods sharing one mock instance,
  order-sensitive) — the 403 test intermittently/deterministically observed a LATER test's "caller can see"
  configuration depending on xUnit's actual (not declaration-order) method execution order. **Fixed** by making
  each test method own a fresh `WebApplicationFactory` (no `IClassFixture`) — re-ran the full class 3× to confirm
  deterministic 4/4 passes. This is a latent fragility that also exists in the older
  `CommunicationRenameThreadContractTests` (same shared-mock shape) — it happens to pass today because of its
  particular declared/observed order, but is NOT structurally guaranteed. Flagged here rather than silently
  fixed elsewhere; not in this task's boundary to touch `CommunicationRenameThreadContractTests` (pre-existing
  file, task 004), so filed as a candidate for `notes/defer-issues.md` (see below) rather than edited in place.

No Critical/Major findings remained after the fix above. No ADR Conflict Resolution Protocol invocation was
needed — no ADR rule was violated or in tension.

## 8. Acceptance criteria — MET/NOT-MET

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Pin toggle on each row, reflects stored state on load | **MET** | `ThreadList.tsx` row render; "reflects the persisted pin state on load" test |
| 2 | Pinning writes the 040 field; survives a full reload | **MET** | `PATCH .../pin` → `sprk_ispinned`; contract test asserts the write payload; component test simulates fresh-mount read |
| 3 | Unpinning clears the field + marker | **MET** | `SetPinnedAsync(false)` unit test + "unpinning clears…" component test |
| 4 | Pinned threads visually marked/sorted to top; unread + word-filter unchanged | **MET** | stable sort in `threadListRows`; pre-existing filter/unread tests still green |
| 5 | Keyboard-operable, `aria-pressed` reflects state, light+dark | **MET** | native `<Button>`, `aria-pressed` assertions; dark-mode test unmodified and passing |
| 6 | Negative: no archive/mute/tag; no 2nd thread-list/widget; `communications-list` retained | **MET** | dedicated negative test; `communications-list`/`communications` strings untouched (not present in this task's diff) |

## 9. Escalations / deviations

- No escalation trigger fired (task-040 field was present; task-012 `ThreadList` was present).
- Deviation from the generic `task-execute` Step 10/11 flow: **did not** update `TASK-INDEX.md` or
  `current-task.md` — per this task's explicit Boundaries instruction ("Do NOT edit TASK-INDEX.md,
  current-task.md…"). The orchestrating session should apply that status flip.
- Filed (not fixed) a latent test-fragility note (§7) about `CommunicationRenameThreadContractTests`'s
  shared-mock/`IClassFixture` shape — out of this task's file-ownership boundary to edit.
