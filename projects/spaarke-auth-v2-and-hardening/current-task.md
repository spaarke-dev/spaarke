# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: Phase C in flight — Task 043 (remove /debug/* endpoints) executing in parallel with 044-048.
> **Active Phase**: C — task 043 in-progress (FULL rigor sub-agent)
> **Last Updated**: 2026-05-19 (task 043 dispatch)

## Task 043 — Remove /debug/* endpoints (in-progress)

**Rigor**: FULL · **Tags**: auth, server, security
**Endpoints discovered**: 11 routes across 3 files
- `Infrastructure/DI/DebugEndpointExtensions.cs` — 9 routes (delete file entirely)
- `Infrastructure/DI/EndpointMappingExtensions.cs` — `/debug/token` (delete block) + `MapDebugEndpoints()` registration (narrow Edit)
- `Api/Ai/VisualizationEndpoints.cs` — `/api/ai/visualization/debug/{documentId}` (delete block, already env-gated to Development)
- `/status` endpoint references debug routes in its response (update string array)
**Files modified (in-progress)**: DebugEndpointExtensions.cs (DELETE), EndpointMappingExtensions.cs, VisualizationEndpoints.cs

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Phase B 11/11 ✅ + 3 standalone JS bug fixes + 1 deferred B-addendum task (031). All deployed + hash-verified on spaarkedev1 + spaarke-demo. User regression-tested Workspace + Document operations + email + Create Matter on Dev — all PASS. |
| **Next Action** | `continue` → Phase C task 040 (rotate AzureAd__ClientSecret + AgentToken__ClientSecret; convert App Service config to Key Vault references; coordinate App Service restart). Phase C is server-only — NO MSAL browser regression required (OBO smoke check instead). |

## How to Resume

```
continue
```

## Phase B FINAL — what shipped

| Wave | Scope | Surfaces | Commit |
|---|---|---|---|
| Pre | 020 AiSessionProvider | 1 shared package | `6b6106f6` |
| 1 | 021-023, 028, 030 | SpaarkeAi + panes + SprkChat + 6 PCFs + buildBffApiUrl dedup | `918b0830` |
| 2 | 024-027, 029 | PlaybookBuilder, DocRelViewer, AnalysisWorkspace, SemanticSearch (build blocked), bffDataServiceAdapter docs | `1cbb299b` |
| 3 | 031 (LegalWorkspace — sprk_corporateworkspace) | 8 files modified, 7 DELETED scaffolding files | _this commit_ |
| 4 | 7 surfaces: SmartTodo (migration) + DocumentUploadWizard (migration) + 5 rebuilds (FindSimilar, PlaybookLibrary, SpeAdmin, WorkspaceLayout, Reporting) | _this commit_ | |
| 5 | 7 wizard rebuilds (CreateEvent/Matter/Project/Todo/WorkAssignment + DailyBriefing + SummarizeFiles) + sprk_communication_send.js bug fix | _this commit_ | |

Plus 3 standalone JS web resource bug fixes (deployed): `sprk_subgrid_parent_rollup.js` (JSON parse), `sprk_DocumentOperations.js` (defaultvalue env-var fallback), `sprk_communication_send.js` (same defaultvalue fix).

Plus 6 PCF version bumps + redeploys (all to both envs).

## Deploy state

| Env | PCFs (6) | Code Pages (13) | Standalone JS (3) |
|---|---|---|---|
| spaarkedev1 | ✅ All v.X+1 bumped + imported + published | ✅ Deployed via Deploy-WebResourceInline.ps1 | ✅ Deployed |
| spaarke-demo | ✅ Same ZIPs imported + published | ✅ Same HTMLs deployed | ✅ Deployed |

Hash-verify: every Code Page + JS deploy SHA-256 MATCH on both envs.

## User regression on Dev — PASS

- SpaarkeAi + 3 Wave 1+2 Code Pages: no popup, console clean (errors traced to non-Phase-B legacy JS)
- Workspace (LegalWorkspace, post-Wave-3): no popup ✅
- Document preview + open: works
- Document upload wizard: works
- Create Matter: matter created (N:N link bug → deferred to task 031)
- Create Event + send email: email sent

## D-AUTH-7 exception sites (canonical list for ESLint allowlist task 070-area)

Sites that legitimately use raw `Authorization: Bearer ${token}` because the wrapper APIs can't handle XHR/SSE/Dataverse-direct/third-party-SDK:

| Class | File | Reason |
|---|---|---|
| SSE | `Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` (×2) | EventSource ReadableStream |
| XHR | `Spaarke.UI.Components/src/components/SprkChat/SprkChatUploadZone.tsx` (×2) | XHR uploads — `authenticatedFetch` can't wrap XHR |
| SSE | `Spaarke.UI.Components/src/hooks/useSseStream.ts` | Main SSE fetch entry |
| SSE | `code-pages/PlaybookBuilder/src/services/aiPlaybookService.ts` | Mirror SprkChat SSE pattern |
| Dataverse | `code-pages/PlaybookBuilder/src/services/dataverseClient.ts` | Dataverse Web API call (`authenticatedFetch` is BFF-scoped) |
| SSE | `code-pages/AnalysisWorkspace/src/services/analysisApi.ts::executeAnalysis` | SSE |
| Out-of-scope | `client/external-spa/src/auth/bff-client.ts::executeFetch` | B2B portal uses sessionStorage; `@spaarke/auth` uses localStorage |
| Third-party SDK | `Reporting/src/components/ReportViewer.tsx` | Power BI `IReportEmbedConfiguration.accessToken` is SDK contract |

## Memory entries saved during Phase B (5)

1. `feedback-dual-env-deploys` — deploy to BOTH envs serially; bump versions from `max(both)`
2. `feedback-name-collision-in-consumer-authinit` — same-name sync-import + async-export silent failure
3. `feedback-proactive-parallel-dispatch` — batch parallel-safe sub-agents per wave
4. `feedback-third-party-sdk-accesstoken-is-ok` — Power BI / MSAL / etc. literal token strings OK at SDK boundaries
5. `project-auth-v2-baseline-msal-bug` — original bug history (pre-existing memory; refreshed during Phase A)

## Carryovers (logged, non-blocking)

### Pre-existing wizard payload bugs (deferred to task 031 per user)
- CreateMatter: N:N association 400 (URL/body correct per schema; likely permissions OR duplicate-association). Matter create itself succeeds.
- CreateProject: createRecord 400 "Error in query syntax" — likely `sprk_issecure` field not deployed OR wrong entity-set names in `@odata.bind`.
- CreateWorkAssignment: same class of payload-malformation bug.
- Investigation report in 031-fix-wizard-payload-bugs.poml `<notes>`. User to capture payload logs (console.info already in code at projectService.ts:327 + workAssignmentService.ts:452) before fix.

### Other carryovers (logged earlier; not Phase B blockers)
1. **SemanticSearch Code Page** still blocked by pre-existing `@lexical/react` `.prod.mjs` webpack resolution. Cannot rebuild/redeploy until that's fixed.
2. **eslint missing devDep** in 4 PCF package.json files (UDG + DRV have it post-Phase-B; SDV/SSC/RDC/EPM used `--no-save` ephemeral). Worth normalizing in a small follow-up.
3. **Deploy-SpaarkeAi.ps1 CREATE branch is buggy** (had to use Deploy-WebResourceInline.ps1 for spaarke-demo first-time create of sprk_spaarkeai).
4. **Duplicate** `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` (older, unused — dedup candidate).
5. **`Spaarke.UI.Components/src/services/document-upload/`** + `useAiSummary.ts` still build raw Bearer headers in implementation (Wave 2 task 029 noted, deferred).
6. **SpeDocumentViewer** residual dead `accessToken: string` props in hooks (not in active data flow).
7. **`@spaarke/ui-components` jest tests can't run** — React 16 peer vs `@testing-library/react@14` mismatch (pre-existing).
8. **DocumentUploadWizard `bffTokenProvider`** prop still wired through wizard tree for Send Email / Work on Analysis paths (out of scope for line 686 fix).
9. **PlaybookBuilder/services/authService.ts** deprecated, zero consumers — safe to delete in cleanup PR.
10. **SpaarkeAi blob: ERR_FILE_NOT_FOUND** (minor; vite-plugin-singlefile sourcemap/chunk reference). Investigate if it becomes user-visible.

## 🚨 CRITICAL CARRYOVER (still applies for future consumer additions)

Any NEW consumer wiring `tenantId: getRuntimeTenantId()` into `initAuth({...})` MUST use import alias to avoid name collision with locally-exported async `getTenantId`. See memory `feedback_name_collision_in_consumer_authinit` + `project_auth_v2_baseline_msal_bug`. ALL 9 Phase B consumers that needed it now use the alias correctly (SpaarkeAi, LegalWorkspace, AnalysisWorkspace, etc. — confirmed by sub-agents).

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅ + 2 hotfix commits (`811e98b5`, `48788c1f`)
- Phase B: **11/11 ✅** + B-addendum task 031 deferred
- Overall: 23/49 tasks (47%) — Phase C/D/E/F remaining
- Library versions: `@spaarke/auth@2.0.0`, `@spaarke/ui-components@2.0.0`, `@spaarke/ai-widgets@0.1.0` (consumed-as-source)

## Next: Phase C (server hardening 040-049) — 10 tasks, ~19h

Tasks (mostly parallel-safe within phase):
- 040 (No-parallel, deploy): Rotate AzureAd__ClientSecret + AgentToken__ClientSecret; convert App Service config to Key Vault references; coordinate App Service restart
- 041, 042 (C-Parallel-1, depend on 040): Migrate Graph + Dataverse from ClientSecretCredential to DefaultAzureCredential (managed identity)
- 043-048 (C-Parallel-2): Remove /debug/* endpoints; HMAC webhook validation; named API key scheme; PostConfigure idempotency; tenant template fix; audit log enrichment
- 049 (C-Parallel-2, depends on 045): Rate limiting on anonymous + API key endpoints

**Per project CLAUDE.md risk-tier rule**: Phase C is server-only — MSAL browser regression test is NOT required (do an OBO smoke check against the deployed BFF instead). Manual MSAL test IS only required after auth-affecting client changes (Phase B done, Phase D's CSP/CAE work will need it).

Phase C task 040 is the gating step (rotates secrets + Key Vault — needs App Service restart). Then C-Parallel-1 (managed identity migrations) can dispatch in parallel.
