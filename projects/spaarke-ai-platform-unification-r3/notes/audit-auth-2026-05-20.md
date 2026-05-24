# Auth Compliance Audit — R3 Project (Task 060)

| Field | Value |
|---|---|
| **Date** | 2026-05-20 |
| **Auditor** | claude-code task 060 |
| **Scope** | All files added/modified across Phases A/B/C/D/E (waves 0–4, tasks 010–051) on branch `work/spaarke-ai-platform-unification-r3` |
| **Standards** | FR-23 (function-based auth contract), NFR-08 (zero token-snapshot patterns), ADR-028 INV-1..INV-8 |
| **Verdict** | **CLEAN — Phase G unblocked** |

---

## 1. Files Audited

Enumeration via `git diff --name-only master..HEAD -- 'src/' 'tests/'` (excluding `package-lock.json`) → **46 source files**:

### Phase A (foundations — tasks 010, 011, 013)
- `src/client/shared/Spaarke.UI.Components/package.json`
- `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/PaneHeader.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/index.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/__tests__/PaneHeader.test.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/index.ts`
- `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts`
- `src/solutions/SpaarkeAi/src/telemetry/__tests__/errorTelemetry.test.ts`
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`
- `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspaceTabManager.test.ts`

### Phase B (Assistant pane — tasks 020–026)
- `src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx`
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`
- `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatInput.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/index.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/index.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/SprkChat.test.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/SprkChat.attachments.test.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/useChatFileAttachment.test.ts`

### Phase C (Workspace pane — tasks 030–035)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceHomeTab.tsx`
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx`
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx`
- `src/solutions/WorkspaceLayoutWizard/src/App.tsx`
- `src/solutions/WorkspaceLayoutWizard/src/main.tsx`
- `src/solutions/WorkspaceLayoutWizard/src/steps/TemplateStep.tsx`
- `src/solutions/WorkspaceLayoutWizard/src/steps/__tests__/TemplateStep.test.tsx`
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts`
- `src/solutions/LegalWorkspace/src/sections/index.ts`
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx`
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts`
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/useDailyBriefing.ts`

### Phase D (Context pane — tasks 040–045)
- `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/index.ts`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailComposeWidget.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/MeetingScheduleWidget.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateProjectWizardWidget.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/FindSimilarWizardWidget.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/AssignWorkWizardLauncher.ts`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/AssignWorkWizardLauncher.test.ts`

### Phase E (BFF — tasks 050–051)
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsAttachmentsTests.cs`

---

## 2. Audit Results

### Check (a) — `accessToken` / `access_token` as prop or state

**Command**: `xargs grep -Hn -E 'accessToken|access_token'` over the 46 enumerated files.

**Total hits**: 11. **All hits classified**: documentation/comment text — zero are actual props, state, or function parameters.

| File | Line | Context | Verdict |
|---|---|---|---|
| `Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` | 50 | JSDoc: "No `accessToken` props or state" | PASS (doc) |
| `SprkChat.tsx` | 245 | JSDoc: "function-based props — NOT a snapshotted `accessToken: string`" | PASS (doc) |
| `SprkChat/__tests__/SprkChat.test.tsx` | 37 | Comment: "not a snapshotted accessToken string" | PASS (doc) |
| `SprkChat/types.ts` | 433 | JSDoc: "replaces the old `accessToken: string` prop" | PASS (doc) |
| `conversation/ConversationPane.tsx` | 38, 910 | JSDoc explaining migration from `accessToken: string` | PASS (doc) |
| `conversation/HistoryOverlay.tsx` | 22, 92 | JSDoc: "NO accessToken prop" | PASS (doc) |
| `workspace/WorkspaceHomeTab.tsx` | 35, 236 | JSDoc: "no `accessToken` snapshots", "no `accessToken` is propagated" | PASS (doc) |
| `workspace/WorkspacePaneMenu.tsx` | 40 | JSDoc: "no `accessToken` props" | PASS (doc) |

**Verdict: PASS** — zero actual `accessToken` props/state/parameters in R3-touched code. Every match is descriptive prose documenting ADR-028 compliance.

### Check (a-2) — Bearer header literals in source (`Authorization\s*:\s*['"]Bearer`)

**Command**: same xargs against the 46 files.

**Total hits**: 2. **Both** are in `__tests__/*` files providing the *expected* `Authorization` header that mocked `authenticatedFetch` would have attached:

| File | Line | Context |
|---|---|---|
| `SprkChat/__tests__/SprkChat.attachments.test.tsx` | 60 | Test mock — `Authorization: 'Bearer test-access-token'` (asserting `authenticatedFetch` was called with the expected header) |
| `SprkChat/__tests__/SprkChat.test.tsx` | 44 | Same pattern |

**Verdict: PASS** — no Bearer header literals in production source; test-mock occurrences are required to assert the contract.

### Check (b) — Retired patterns (`tokenBridge`, `BridgeStrategy`, `XrmStrategy`, `__SPAARKE_BFF_TOKEN__`, `publishToken`, `bffAuthProvider`, `TokenSnapshot`, `provideAuthBridge`, `getAuthBridge`)

**Command**: `xargs grep -Hn -E 'tokenBridge|BridgeStrategy|TokenBridge|provideAuthBridge|getAuthBridge|TokenSnapshot|XrmStrategy|__SPAARKE_BFF_TOKEN__|publishToken|bffAuthProvider'`

**Total hits**: 0.

**Verdict: PASS** — no references to retired Auth v1 cascade symbols in any R3 file.

### Check (c) — Raw `fetch(` calls bypassing `authenticatedFetch`

**Command**: `xargs grep -Hn -E '\bfetch\('` over the 46 files.

**Total hits**: 4 — 2 in source code, 2 in JSDoc/comments.

| File | Line | Context | Verdict |
|---|---|---|---|
| `SprkChat/types.ts` | 392 | JSDoc: "opened via `fetch()` + `ReadableStream`" | PASS (doc) |
| `SprkChat/types.ts` | 440 | JSDoc: "use `authenticatedFetch` directly because they need the raw `fetch()`+`ReadableStream`" | PASS (doc) |
| `SprkChat.tsx` | 1098 | `await fetch(approveUrl, ...)` — plan approval SSE stream | PASS (D-AUTH-7) |
| `SprkChat.tsx` | 1682 | `await fetch(refineUrl, ...)` — refine SSE stream | PASS (D-AUTH-7) |

**Both fetch() call sites at lines 1098 and 1682** are **legitimate D-AUTH-7 exceptions** per ADR-028:

> *Limited D-AUTH-7 exceptions: SSE (EventSource ReadableStream), XHR uploads, Dataverse Web API direct calls (BFF-scoped wrapper would route wrong host), External SPA out-of-scope. Each carries `// Auth v2 (D-AUTH-7):` justification comment.*

Both sites:
- Are **SSE streaming endpoints** (plan-approve and refine) — require `ReadableStream` body access, which `authenticatedFetch` cannot expose.
- Carry the **required `// Auth v2 (D-AUTH-7):`** justification comment immediately above the `fetch(` call.
- Use **per-request `getAccessToken()`** (no token snapshot — fresh acquire per stream open).

`git blame` confirms both `fetch(` lines and their surrounding `// Auth v2 (D-AUTH-7):` comments were authored by the **predecessor auth-v2 project** (commit `918b08307`, 2026-05-19). No R3 commit modified these lines. R3 (commits `5696fd5e`, `c72b4579`, `fa980a71`) touched other parts of SprkChat.tsx (toolbar restructure, attachments wiring) but left the D-AUTH-7 sites untouched.

**Verdict: PASS** — both fetch() sites are blessed D-AUTH-7 exceptions.

### Check (d) — `authenticatedFetch` adoption at new BFF call sites

Verified positive adoption (sample of import + usage hits across R3-new code):

| File | Imports | Usage |
|---|---|---|
| `WelcomePanel.tsx` | `import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";` line 41 | `await authenticatedFetch(url, ...)` line 215 |
| `HistoryOverlay.tsx` | `import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";` line 67 | `await authenticatedFetch(url, ...)` line 331 |
| `WorkspaceHomeTab.tsx` | `import { authenticatedFetch, buildBffApiUrl } from "@spaarke/auth";` line 48 | `await authenticatedFetch(url)` line 270 |
| `ConversationPane.tsx` | passes `authenticatedFetch` prop into `<SprkChat />` and `<HistoryOverlay />` lines 774, 916 | (consumer threads function through) |
| `useDailyBriefing.ts` | `import { authenticatedFetch } from "../../services/authInit";` line 28 | `await authenticatedFetch(...)` line 292 |
| `SprkChat.tsx` | function-based contract — receives `authenticatedFetch` as prop (line 316), threads to hooks (lines 342, 351, 374, 394) | one-shot BFF calls via `authenticatedFetch` (line 1394 `await authenticatedFetch(persistUrl, ...)`) |

All non-streaming BFF calls in R3-new code use `authenticatedFetch`. SSE streams use D-AUTH-7 raw fetch + `getAccessToken()` per the contract.

**Verdict: PASS** — `authenticatedFetch` is the only path for one-shot BFF calls.

### Check (e) — BFF endpoint filter chain preservation (Phase E task 050)

`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` line 76–88:

```csharp
group.MapPost("/sessions/{sessionId}/messages", SendMessageAsync)
    .AddAiAuthorizationFilter()
    .RequireRateLimiting("ai-stream")
    .WithName("SendChatMessage")
    ...
```

The `.AddAiAuthorizationFilter().RequireRateLimiting("ai-stream")` chain is intact. The handler-level FR-07 attachment validation runs after filter authorization succeeds (documented in comment lines 72–75: *"These filters fire BEFORE the handler runs"*). The refine endpoint (line 91–93) maintains the same chain.

**Verdict: PASS** — endpoint filter chain preserved on the message + refine routes.

---

## 3. INV-1..INV-8 Cross-Check

R3 added no new MSAL instantiation, no new `PublicClientApplication`, no new authority configuration, and no new auth provider. All BFF calls flow through the shared `@spaarke/auth` provider established by the predecessor auth-v2 project. The bundling-reality invariant (INV-8) is satisfied by R3's rebuild cadence (Wave 4 `feat(r3)` commits include rebuilt `Spaarke.UI.Components`, `Spaarke.AI.Widgets`, and Vite bundle outputs).

| Invariant | Status in R3 changes |
|---|---|
| INV-1 `cacheLocation: 'localStorage'` | N/A (R3 does not configure MSAL) |
| INV-2 `storeAuthStateInCookie: true` | N/A (R3 does not configure MSAL) |
| INV-3 tenant-specific authority | N/A (R3 does not configure MSAL) |
| INV-4 tenant resolution chain | N/A (R3 does not resolve tenant) |
| INV-5 UPN as loginHint | N/A (R3 does not call MSAL directly) |
| INV-6 prefer omitting `authority` | N/A (R3 does not configure MSAL) |
| INV-7 shared `PublicClientApplication` | PRESERVED — R3 consumes `useAuth()` / `authenticatedFetch` from `@spaarke/auth`; no `new PublicClientApplication(...)` outside library |
| INV-8 Bundling Reality | OBSERVED — Wave commits rebuilt all consumers (`Spaarke.UI.Components`, `Spaarke.AI.Widgets`, Vite outputs) |

---

## 4. Roll-up

- Files audited: **46**
- Total grep matches inspected: **17** (11 accessToken; 2 Bearer; 0 retired; 4 fetch including comments)
- Actual violations: **0**
- Remediation taken: **None required**
- D-AUTH-7 exceptions: **2** (both pre-existing, both compliant with the justification-comment requirement)

## 5. Verdict

### **CLEAN**

**No violations found.** All new code authored in R3 Phases A/B/C/D/E adheres to:
- FR-23 (function-based auth contract — `authenticatedFetch` + `getAccessToken()` via `@spaarke/auth`)
- NFR-08 (zero token-snapshot patterns)
- ADR-028 INV-1..INV-8 (Spaarke Auth v2)

### **Phase G unblocked**

This audit is the FR-23 / NFR-08 gate authorizing Phase G deployment (task 070 and successors).

---

## 6. Remediation Tasks

**None.** No follow-up remediation tasks required.

---

*End of audit memo.*
