# AIR2-075 — Legacy Workspace Tools Verdict

> **Task**: AIR2-075 (contingent on 001) · **Spec**: FR-D-06 · **Gate**: G-R2-D
> **Basis**: task 001's recorded r1 P4-close disposition of design §10 row 4
> (`projects/spaarke-ai-architecture-redesign-r2/notes/r1-p4-reconciliation.md`, "Row 4")

## Verdict summary (per-tool disposition)

| Item | Disposition | Basis |
|---|---|---|
| `GetWorkspaceTabContentHandler` (`get_workspace_tab_content`) | **RETIRE** | Named in r1 Track-B O-2 ("3 rows + 3 handlers"); grep confirms zero consumers outside its own file, its Dataverse seed row, and its unit test |
| `UpdateWorkspaceTabHandler` (`update_workspace_tab`) | **RETIRE** | Same O-2 cluster; zero consumers outside its own file/seed row/tests |
| `CloseWorkspaceTabHandler` (`close_workspace_tab`) | **RETIRE** | Same O-2 cluster; zero consumers outside its own file/seed row/tests |
| `SendWorkspaceArtifactHandler` — **legacy variants** (`widgetType` = `Summary`/`DocumentViewer`/`Dashboard`/`Table`) | **RETIRE** (the 4 legacy branches only) | O-2: "Send legacy variants" named explicitly for retirement; the handler's own current code already documents these as dead UI surface ("NOT visible in the current workspace UI — avoid them unless explicitly instructed") |
| `SendWorkspaceArtifactHandler` — **`Workspace` widgetType leg** (opens a named layout as a live tab) | **KEEP as-is** | O-2 explicit keep-list ("keep `send_workspace_artifact` `widgetType:'Workspace'` leg"); this leg does not call `IWorkspaceStateService` at all (uses SSE + `IUiActionAckCoordinator` + `WorkspaceLayoutService` instead) — structurally independent of the retiring cluster |
| `IWorkspaceStateService.GetTabsAsync` + its `WorkspaceStateEndpoints.GetState` (`GET /api/workspace/state`) consumer + `SprkChatAgentFactory`'s workspace-state prompt block | **KEEP as-is** | O-2 explicit keep-list ("keep ... `GetTabsAsync` prompt block ... `GET /api/workspace/state` restore") |
| `IWorkspaceStateService.UpsertTabAsync` / `.PinTabAsync` / `.CloseTabAsync` (the "write path") | **Becomes dead code once the above retire** — recommend deleting from the interface + implementation in the same PR | O-2 names "`WorkspaceStateService` write path" for retirement; grep confirms these three methods are called ONLY from the 3 handlers above + the 4 legacy `SendWorkspaceArtifactHandler` branches — no other caller exists |

## Basis (task 001 citation)

Per `r1-p4-reconciliation.md` Row 4: **"in-scope-FR — r1 P4 did NOT close this row"** (O-2 disposition), with the starting assumption handed to this task: *"proceed directly to its Step 1 reference sweep ... act on the O-2 keep-list as the known 'already-ruled-live' leg (`send_workspace_artifact`, `GetTabsAsync` prompt block, `GET /api/workspace/state`)."* This verdict follows that instruction exactly: the reference sweep below re-derives and confirms the O-2 keep/retire split against the current (post-r1-merge) codebase rather than re-litigating it.

The acceptance-criteria NEGATIVE branch ("if task 001 marked row 4 verified-closed, no code change is made") does NOT apply — task 001 marked row 4 **NOT** closed.

## Reference-sweep evidence (Step 1)

Grep across `src/` (excluding bin/obj) for `IWorkspaceStateService`, `WorkspaceStateService`, `WorkspaceStateEndpoints`, and the 3 handler class names:

**Production consumers found** (26 files total):
- `Services/Workspace/IWorkspaceStateService.cs`, `Services/Workspace/WorkspaceStateService.cs` — the service itself
- `Api/Workspace/WorkspaceStateEndpoints.cs` — `GET /api/workspace/state` (KEEP; calls `GetTabsAsync` only)
- `Services/Ai/Chat/SprkChatAgentFactory.cs` (line ~501-505) — calls `GetTabsAsync` to build the system-prompt workspace-state block (KEEP)
- `Services/Ai/Handlers/GetWorkspaceTabContentHandler.cs` — calls `GetTabsAsync` only (read-only tool) → **RETIRE**
- `Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs` — calls `GetTabsAsync` + `UpsertTabAsync` → **RETIRE**
- `Services/Ai/Handlers/CloseWorkspaceTabHandler.cs` — calls `GetTabsAsync` + `CloseTabAsync` → **RETIRE**
- `Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs` — legacy branches call `UpsertTabAsync`; the kept `Workspace` layout-tab branch (`ExecuteOpenWorkspaceTabAsync`) calls **none** of `IWorkspaceStateService`'s members (SSE + ack-coordinator + `WorkspaceLayoutService` + `IDataverseUserClient` only)
- `Infrastructure/DI/AnalysisServicesModule.cs` (line 275) — unconditional `services.AddScoped<IWorkspaceStateService, WorkspaceStateService>()` (task 051, R6 Pillar 6a)
- `Infrastructure/DI/EndpointMappingExtensions.cs` (line 244) — unconditional `app.MapWorkspaceStateEndpoints()`
- `Models/Workspace/WorkspaceTab.cs` — the DTO (no change needed; still used by the kept read path)
- Remaining files in the 26-file grep hit list (`AiChatModule.cs`, `PinnedContextRepository.cs`, `ChatAckEndpoints.cs`, `IUiActionAckCoordinator.cs`, `PaneEventTypes.ts`, `ManagePinnedContextHandler.cs`, `ContextSseEventDto.cs`, `ContextEventEmitter.cs`, TS-side `SprkChat/types.ts`, `WorkspaceLayoutWidget.tsx`, `WorkspacePane.tsx`, `useContextEventBridge.ts`, `AddToAssistantToggle.tsx`, `PinnedMemoryEndpoints.cs`) — matched the sweep's broad pattern set (mostly the unrelated pinned-memory feature or the SSE ack plumbing that the KEPT `Workspace` layout leg shares) — **none reference the 3 retiring handlers or the retiring `SendWorkspaceArtifactHandler` legacy branches**.

**Write-path caller confirmation** — grep for `UpsertTabAsync|PinTabAsync|CloseTabAsync` across `src/` returns exactly 6 files: the service + interface + model, `SendWorkspaceArtifactHandler.cs` (legacy branches), `CloseWorkspaceTabHandler.cs`, `UpdateWorkspaceTabHandler.cs`. **Zero other callers.** `PinTabAsync` in particular has no caller anywhere in `src/` — it is already fully orphaned today, hanging only off the service/interface definitions.

**Dataverse catalog rows** (`infra/dataverse/sprk_analysistool-*-row.json`) — all 3 retiring tools have live seed rows with no inactive/deprecated marker:
- `sprk_analysistool-get-workspace-tab-content-row.json` (`sprk_toolcode: GET-WORKSPACE-TAB-CONTENT`)
- `sprk_analysistool-update-workspace-tab-row.json` (`sprk_toolcode: UPDATE-WORKSPACE-TAB`)
- `sprk_analysistool-close-workspace-tab-row.json` (`sprk_toolcode: CLOSE-WORKSPACE-TAB`)

These rows are consumed by `scripts/Seed-TypedHandlers.ps1` (idempotent UPSERT by `sprk_handlerclass`/`sprk_toolcode`) — i.e., these 3 tools are **currently live in the production tool catalog**, not already dormant.

**Test surface impacted** (sizeable — this is the crux of the "large deletion" finding below):

| File | Lines | Scope |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/GetWorkspaceTabContentHandlerTests.cs` | 592 | Full delete candidate |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/UpdateWorkspaceTabHandlerTests.cs` | 231 | Full delete candidate |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/CloseWorkspaceTabHandlerTests.cs` | 191 | Full delete candidate |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/SendWorkspaceArtifactHandlerTests.cs` | 659 | **Partial** — 8 legacy-variant test methods (lines ~102–267: `ExecuteChatAsync_Succeeds_AndPersistsTab_OnHappyPath`, `ExecuteChatAsync_Fails_WhenWidgetTypeMissing`, `ValidateChat_Fails_WhenTitleMissing`, `ExecuteChatAsync_ForwardsTenantId_ToWorkspaceStateService`, `ValidateChat_Fails_WhenTenantIdMissing`, `ExecuteChatAsync_ReturnsError_WhenWorkspaceServiceThrows`, `ExecuteAsync_Playbook_ReturnsValidationError`) delete; the ~13 `*_WorkspaceLayout_*` methods (line ~270 on) are the KEPT leg's coverage and stay |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/WorkspaceStateServiceTests.cs` | 560 | **Partial** — `GetTabsAsync`/`UpsertTabAsync` read-path coverage stays for the read leg; `UpsertTabAsync`/`PinTabAsync`/`CloseTabAsync` write-path unit coverage becomes scaffolding for dead code if the interface is trimmed |
| `tests/integration/Spe.Integration.Tests/PhaseC/CrossPillarIntegrationTests.cs` | — | 29 occurrences of the retiring tool names/tool-codes — needs inspection + partial rewrite |
| `tests/integration/Spe.Integration.Tests/Workspace/ConflictResolutionTests.cs` | — | 14 occurrences — this almost certainly exercises `UpdateWorkspaceTabHandler`'s Q8 USER-WINS conflict semantics end-to-end (a **data-mutation**-shaped integration test) |
| `tests/integration/Spe.Integration.Tests/ToolFrameworkIntegrationTests.cs` | — | 2 occurrences |
| `tests/integration/contract/Api/Workspace/WorkspaceStateEndpointsContractTests.cs` | — | Tests `GET /api/workspace/state` only (the KEPT read endpoint) — **unaffected**, no change needed |

## Escalation — flagging rather than executing (per task constraints)

**This verdict stops short of performing the retirement.** Per this task's explicit operating constraints ("if the verdict implies DELETING code, grep-verify zero references first and include the evidence; if anything is ambiguous or the deletion is large, flag for the main session instead of deleting"), the retire-vs-keep *decision* itself is **not ambiguous** — it is a direct, evidenced re-application of r1's own O-2 audit ruling, re-confirmed against the current codebase (see sweep above). What makes this a flag-don't-delete case is **scale and blast radius**, not uncertainty:

- 3 full production handler classes (~600 + 700 + 400 LOC) to delete outright.
- 1 handler (`SendWorkspaceArtifactHandler`, 1,160 LOC) to surgically trim (remove `WorkspaceTab` construction, `DeserializeWidgetData`, `ResolveMatterContext`, the 4-variant JSON-schema/description text, `UpsertTabAsync` call) while preserving the `Workspace` layout-tab leg byte-for-byte.
- `IWorkspaceStateService`'s write surface (`UpsertTabAsync`, `PinTabAsync`, `CloseTabAsync`) becomes dead once the above retire — a judgment call on whether to trim the interface in the same PR (recommended, since NFR-13 retirement should not leave a dead write-path hanging off a live service) or defer.
- 3 Dataverse `sprk_analysistool` seed-row JSON files to deactivate/delete + the corresponding live Dataverse rows to retire via `scripts/Seed-TypedHandlers.ps1` (or a manual deactivation) — this is a **currently-live production tool catalog** change, not dead code cleanup.
- ~2,233 lines of unit test code across 5 files, of which 2 files need surgical (not wholesale) edits.
- 45 combined occurrences across 3 integration test files, including what is very likely a KEEP-protected conflict-resolution regression suite (`ConflictResolutionTests.cs`) requiring careful same-PR handling per ADR-038's "deletion requires same-PR replacement" rule if any KEEP-category scenario is deleted rather than genuinely retired behavior.

Retirement is, per NFR-13 and the task's own escalation trigger, "a one-way commitment." Given the combination of (a) a live production tool catalog change, (b) a large, multi-file, cross-project (unit + integration) test surface, and (c) an interface-shape decision (trim `IWorkspaceStateService` or not) that the POML doesn't pre-decide, this is handed to the main session to execute — either directly or as a dedicated follow-on task — rather than performed inline by this run.

### Recommended execution plan (for the main session / a follow-on task)

1. Delete `GetWorkspaceTabContentHandler.cs`, `UpdateWorkspaceTabHandler.cs`, `CloseWorkspaceTabHandler.cs` + their 3 unit test files.
2. Trim `SendWorkspaceArtifactHandler.cs`: remove the 4 legacy `widgetType` branches (`Summary`/`DocumentViewer`/`Dashboard`/`Table`) — the tab-construction path, `DeserializeWidgetData`, `ResolveMatterContext`, `SendWorkspaceArtifactPayload` (if unused by the kept leg), and the legacy-variant text in `Metadata.Description` + `SupportedWidgetTypes` (narrow to `{"Workspace"}`) — while leaving `ExecuteOpenWorkspaceTabAsync` and its supporting members untouched. Mirror the trim into `infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json` (`sprk_description`, `sprk_jsonschema`) per the FR-A-01 byte-equal mirror rule.
3. In `SendWorkspaceArtifactHandlerTests.cs`, delete the 8 legacy-variant test methods; keep the ~13 `*_WorkspaceLayout_*` methods.
4. Decide + execute on `IWorkspaceStateService`'s write surface: recommend deleting `UpsertTabAsync`/`PinTabAsync`/`CloseTabAsync` from the interface + `WorkspaceStateService` (grep in this sweep shows zero callers remain once step 1–2 land) and trimming `WorkspaceStateServiceTests.cs` to the `GetTabsAsync` read-path coverage only. `PinTabAsync` is already a zero-caller orphan today independent of this task.
5. Deactivate/delete the 3 Dataverse seed-row JSON files (`get-workspace-tab-content`, `update-workspace-tab`, `close-workspace-tab`) and run the retirement leg of `scripts/Seed-TypedHandlers.ps1` (or issue a Dataverse deactivation) against the live `sprk_analysistool` rows.
6. Inspect and update `CrossPillarIntegrationTests.cs` (29 hits), `ConflictResolutionTests.cs` (14 hits — treat as a probable data-mutation KEEP-path scenario; if the scenario itself represented real regression protection for USER-WINS semantics, that protection is retiring WITH the feature, which is legitimate — document as much rather than silently deleting), `ToolFrameworkIntegrationTests.cs` (2 hits).
7. Run `dotnet build` (0 errors) + the affected unit/integration suites green.
8. Measure BFF publish-size delta (expect a small negative delta — this is a net code deletion) per `.claude/constraints/bff-extensions.md` §A.3 and `scripts/Measure-BffPublishSize.ps1`; record against `notes/publish-size-baseline.json`.
9. Confirm no new HIGH CVE (no package changes expected — `dotnet list package --vulnerable --include-transitive` should be unchanged).

## Acceptance-criteria status (this run)

| Criterion | Status |
|---|---|
| Verdict cites task 001's recorded r1 P4-close disposition of row 4 | ✅ (see "Basis" above) |
| Each of the 3 tools + 4 artifact variants is dispositioned with recorded rationale | ✅ disposition assigned + evidenced (retire ×3 handlers, retire 4 legacy variants, keep `Workspace` leg, keep read path, trim write-path recommended) |
| Retired items pass grep-zero verification; catalog/seed mirrors updated | ⏳ **NOT executed this run** — pre-deletion grep evidence gathered (zero non-retiring-cluster consumers confirmed); the post-deletion grep-zero re-check + catalog/seed edits are deferred to the execution step above |
| Re-pointed tools consume state via a sanctioned facade | N/A — no tool is being re-pointed to a new facade; the kept `Workspace` leg already avoids `IWorkspaceStateService` entirely and the kept read path already consumes `IWorkspaceStateService` directly, which is the existing, unchanged, ADR-013-compliant pattern (workspace-state plumbing, not an AI-internal type) |
| NEGATIVE: if row 4 verified-closed, no code change / confirmation no-op | N/A — row 4 was NOT verified-closed; this branch does not apply |

## Deviation / escalation note

No code was modified, no tests were deleted, and no Dataverse rows were changed in this run. This is a deliberate deviation from the POML's Steps 2–4 (Disposition / Grep-zero / Verify), made per this task's explicit operating constraint to flag rather than execute a large, one-way, production-catalog-touching deletion. Build/test verification was not re-run because no files changed.
