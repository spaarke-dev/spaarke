# E2E Verification Checklist — SprkChat Workspace Companion

> **Task**: 080 — Integration Tests & Manual E2E Verification
> **Created**: 2026-03-16
> **Status**: Ready for manual verification

This checklist covers the manual end-to-end scenarios for the compound intent detection
and plan approval flow (tasks 071–073).

---

## Prerequisites

- [ ] BFF API deployed to dev environment (`spe-api-dev-67e2xz.azurewebsites.net`)
- [ ] Redis cache enabled and reachable
- [ ] SprkChat PCF control deployed to `spaarkedev1.crm.dynamics.com`
- [ ] At least one playbook configured with `write_back` capability
- [ ] A document loaded in Analysis Workspace with an active session

---

## Scenario 1: Single-Tool Intent (No Plan Preview)

**Goal**: Verify single-tool requests stream immediately without triggering a plan.

Steps:
1. Open Analysis Workspace with a document loaded
2. Open SprkChat side pane
3. Send a single-tool message (e.g., "Search for relevant clauses about indemnification")
4. Verify streaming tokens appear immediately (no plan preview card)
5. Verify the assistant response is complete

Expected: No `plan_preview` SSE event; direct streaming response.

- [ ] Tokens stream without delay
- [ ] No plan card appears in the UI
- [ ] Response is coherent

---

## Scenario 2: Compound Intent Detection (Plan Preview)

**Goal**: Verify that messages triggering 2+ tools emit a `plan_preview` event.

Steps:
1. Open Analysis Workspace with a document loaded
2. Open SprkChat side pane
3. Send a multi-step message (e.g., "Search for indemnification clauses and add a summary to the working document")
4. Verify a plan preview card appears in the chat UI (not streaming tokens)
5. Verify the plan shows individual steps

Expected: `plan_preview` SSE event emitted; execution halted; plan card shows steps.

- [ ] Plan card appears in the UI
- [ ] Plan steps are shown (e.g., Step 1: search, Step 2: write-back)
- [ ] No tool execution has occurred yet
- [ ] "Approve" button is visible

---

## Scenario 3: Plan Approval and Execution

**Goal**: Verify approving a plan executes all steps and streams results.

Steps:
1. Continue from Scenario 2 (plan preview is showing)
2. Click "Approve" in the plan card
3. Verify `plan_step_start` events are emitted per step
4. Verify `token` events stream per step
5. Verify `plan_step_complete` events are emitted per step
6. Verify `done` event at the end

Expected: Steps execute in sequence; tokens stream per step; session history preserved.

- [ ] Each step starts and completes in order
- [ ] Tokens stream during each step
- [ ] `done` event closes the stream
- [ ] Chat history shows the assistant's summarized response
- [ ] Working document updated (if write_back step included)

---

## Scenario 4: Plan Double-Click Protection

**Goal**: Verify a plan cannot be approved twice (409 Conflict).

Steps:
1. Trigger a plan preview (Scenario 2)
2. Approve the plan once (Scenario 3)
3. Attempt to click Approve again (e.g., double-click simulation via dev tools)
4. Verify the second approval returns a 409 response

Expected: Second approval returns `{ error: "Plan no longer available..." }` with 409 status.

- [ ] Second POST returns 409 Conflict
- [ ] UI shows a user-friendly error message
- [ ] No duplicate execution occurs

---

## Scenario 5: WriteBackToWorkingDocument (SPE Safety)

**Goal**: Verify write-back goes to Dataverse only, never to SPE/SharePoint.

Steps:
1. Open Analysis Workspace with `write_back` capability enabled in the playbook
2. Send: "Add a summary of the key risks to my working document"
3. Approve the plan if a plan preview appears
4. Open the working document in the editor

Expected: The working document in Dataverse is updated; no SharePoint files are modified.

- [ ] Working document content updated in Dataverse
- [ ] No `PUT /drive/items/...` calls in network trace (no SPE write)
- [ ] SharePoint document remains unmodified

---

## Scenario 6: Plan Expiry (30-minute TTL)

**Goal**: Verify a plan that is not approved within 30 minutes returns 404/409.

Steps:
1. Trigger a plan preview
2. Wait >30 minutes (or simulate TTL expiry via Redis flush)
3. Click "Approve"
4. Verify the response indicates the plan is no longer available

Expected: 409 Conflict with message about plan expiry.

- [ ] 409 returned after TTL expiry
- [ ] UI shows appropriate expired message

---

## Scenario 7: Session Not Found (Auth Boundary)

**Goal**: Verify that approving a plan for a non-existent or foreign session returns 404.

Steps:
1. Construct a POST request to `/api/ai/chat/sessions/{randomGuid}/plan/approve`
2. Include valid Bearer token and `X-Tenant-Id` header
3. Use a `planId` that matches a real plan

Expected: 404 Not Found with message "Session ... not found."

- [ ] 404 returned for non-existent session
- [ ] Tenant isolation enforced (plan from tenant A cannot be approved in tenant B context)

---

## Automated Test Coverage Summary

| Test Class | Tests | Coverage |
|------------|-------|----------|
| `AgentMiddlewareTests` | ~20 | Middleware pipeline, FakeAgent, TrackingAgent |
| `SprkChatAgentTests` | ~12 | System prompt, history, tools, streaming |
| `SprkChatAgentFactoryTests` | ~4 | Context loading, new instance per call |
| `WorkingDocumentToolsTests` | ~25 | EditWorkingDocument, AppendSection, WriteBackToWorkingDocument |
| `StreamingWriteIntegrationTests` | ~8 | Full write pipeline, capability gating |
| `ChatSessionPlanEndpointTests` | ~5 | HTTP endpoint registration, auth, 404/409 |
| `PendingPlanManagerTests` | ~8 | Store/get/delete, TTL, tenant isolation |
| `AnalysisChatContextEndpointsTests` | ~6 | Context-mappings endpoint |

**Total**: All tests pass as of 2026-03-16 (`dotnet test` → 0 failures, 4297 passed)

---

## Notes

- Redis is disabled in test WebApplicationFactory (`Redis:Enabled = false`)
- `CustomWebAppFactory` uses `FakeAuthHandler` which injects `oid` claim but NOT `tid`
- All plan endpoint tests use `X-Tenant-Id` header as fallback per `ExtractTenantId()` logic
- `PendingPlanManager` tests use `MemoryDistributedCache` as an in-process substitute for Redis
