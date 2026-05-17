# Phase 1 Integration Test Results — FR-01 through FR-12

> **Project**: spaarke-ai-platform-unification-r1
> **Test Date**: 2026-05-16
> **Environment**: https://spaarkedev1.crm.dynamics.com (dev)
> **BFF**: https://spe-api-dev-67e2xz.azurewebsites.net
> **Tester**: ___

---

## Pre-Test Setup Checklist

- [ ] `sprk_spaarkeai` web resource visible in Dataverse solution
- [ ] Code Page added to site map / navigation (manual step)
- [ ] BFF `/healthz` returns 200
- [ ] BFF standalone endpoint returns 401 (auth required): `GET /api/ai/chat/context-mappings/standalone?entityType=sprk_matter&entityId=00000000-0000-0000-0000-000000000000`
- [ ] Dataverse environment variables configured (BFF URL, MSAL client ID, OAuth scope)

---

## FR-01: Three-Pane Layout

**Requirement**: Three-pane layout renders with chat (left, always visible), output (center, dynamic), and source (right, collapsible).

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 1.1 Layout renders | Open SpaarkeAi via nav | Three panes visible: chat left, output center, source right | | |
| 1.2 Left splitter | Drag left splitter | Chat pane resizes, output pane adjusts | | |
| 1.3 Right splitter | Drag right splitter | Source pane resizes, output pane adjusts | | |
| 1.4 Source collapse | Click source collapse toggle | Source pane collapses to strip, center expands | | |
| 1.5 Source expand | Click collapsed source strip | Source pane re-expands to previous width | | |
| 1.6 Minimum widths | Drag splitters to extremes | Panes stop at minimum widths, don't disappear | | |

---

## FR-02: Entity Context from URL Parameters

**Requirement**: `StandaloneAiContext` resolves entity context from URL parameters.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 2.1 Matter context | Open with `?matterId=<valid-guid>` | Entity context shows matter, scoped playbooks load | | |
| 2.2 No context | Open without parameters | Loads with no entity context (global mode) | | |
| 2.3 Invalid GUID | Open with `?matterId=not-a-guid` | Graceful handling, no crash | | |

---

## FR-03: BFF Standalone Context Endpoint

**Requirement**: BFF `StandaloneChatContextProvider` returns playbooks, tools, quick actions scoped to entity.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 3.1 sprk_matter | Auth'd GET `/api/ai/chat/context-mappings/standalone?entityType=sprk_matter&entityId=<guid>` | 200 with context fields for matter | | |
| 3.2 contact | Same with `entityType=contact` | 200 with context fields for contact | | |
| 3.3 unsupported entity | `entityType=sprk_nonexistent` | 400 with supported types list | | |
| 3.4 invalid GUID | `entityId=not-a-guid` | 400 with validation error | | |

---

## FR-04: Existing SprkChat Capabilities

**Requirement**: All existing SprkChat capabilities work in standalone mode.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 4.1 Chat loads | Open Code Page | SprkChat renders in left pane, playbook selector visible | | |
| 4.2 Send message | Type and send a message | Streaming response appears | | |
| 4.3 Playbook switch | Change playbook via selector | Context switches, new playbook active | | |
| 4.4 Tool execution | Trigger a tool (e.g., document search) | Tool executes, results appear in chat | | |
| 4.5 Slash commands | Type `/` | Command menu appears | | |
| 4.6 File upload | Upload a document | Upload completes, AI processes | | |

---

## FR-05: Output Pane Widgets

**Requirement**: Output pane renders purpose-built widgets from component registry based on `output_pane` SSE events.

**STATUS: PARTIAL — see Code Gaps section below**

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 5.1 Empty state | Open Code Page, no messages sent | "AI Output Pane" empty state with icon | | |
| 5.2 Loading state | Send a message | Loading spinner/progress bar appears in output pane | | |
| 5.3 Widget renders | AI response includes output data | Purpose-built widget renders (not markdown) | | Depends on BFF emitting output_pane SSE events |

---

## FR-06: Source Pane Widgets

**Requirement**: Source pane renders reference material based on `source_pane` SSE events.

**STATUS: PARTIAL — see Code Gaps section below**

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 6.1 Empty state | Open Code Page, no sources loaded | Source pane shows empty/collapsed state | | |
| 6.2 Document loads | AI loads a reference document | Document viewer renders in source pane | | Depends on BFF emitting source_pane SSE events |

---

## FR-07: Cross-Pane Linking

**Requirement**: Clicking a citation in output pane navigates source pane to that reference.

**STATUS: CODE COMPLETE, NEEDS SSE WIRING**

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 7.1 Citation click | Click citation in output widget | Source pane scrolls to cited section | | Depends on source_highlight SSE events |

---

## FR-08: Chat History

**Requirement**: Chat history panel shows previous sessions with search.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 8.1 History panel | Toggle chat history panel | Session list appears | | |
| 8.2 Search | Type in search box | Sessions filter by title/preview | | |
| 8.3 Resume session | Click a previous session | Chat loads with that session's history | | |

---

## FR-09: Launch Points

**Requirement**: Launch points work from workspace, entity form, deep-link, and M365 handoff.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 9.1 Workspace nav | Click SpaarkeAi in main nav | Code Page opens full-page | | Manual nav setup required |
| 9.2 Entity form button | Open a matter form, click AI button | Code Page opens as dialog with matter context | | Ribbon button setup required |
| 9.3 Deep-link | Navigate to sprk_spaarkeai with data param | Code Page opens with entity context | | |
| 9.4 M365 handoff | (Deferred) | (Deferred — M365 integration separate project) | N/A | |

---

## FR-10: @spaarke/ai-context Library

**Requirement**: `useChatSession`, `useChatContextMapping`, `useChatPlaybooks` work from shared library.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 10.1 Import works | Code Page loads without import errors | No console errors related to ai-context | | |
| 10.2 Hooks functional | Chat session creates successfully | Session ID persisted, API calls work | | |

---

## FR-11: @spaarke/ai-outputs Library

**Requirement**: Widget registries resolve correct component for each output type.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 11.1 Import works | Code Page loads without import errors | No console errors related to ai-outputs | | |
| 11.2 Registry resolves | Output widget renders | Widget component loaded from registry | | |

---

## FR-12: AnalysisWorkspace Regression

**Requirement**: AnalysisWorkspace works identically after refactor to import from `@spaarke/ai-context`.

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| 12.1 Analysis opens | Open an analysis record | AnalysisWorkspace loads normally | | |
| 12.2 Chat works | Send a message in analysis chat | Streaming response, tools work | | |
| 12.3 Playbook loads | Playbook selector shows options | Correct playbooks for analysis type | | |
| 12.4 No console errors | Check browser console | No new errors related to ai-context imports | | |

---

## Dark Mode (NFR-04)

| Test | Steps | Expected | Pass/Fail | Notes |
|------|-------|----------|-----------|-------|
| DM-1 Toggle theme | Switch Dataverse to dark mode | All panes adapt colors correctly | | |
| DM-2 No hard-coded colors | Inspect widget styles | All colors use Fluent v9 tokens | | |

---

## Code & Azure Gaps Identified

### Gap 1: SSE Event Wiring (CRITICAL for FR-05, FR-06, FR-07)

**Problem**: The BFF SSE event types (`output_pane`, `source_pane`, `source_highlight`) were defined (task 030) and the frontend panels listen for them, but **no existing BFF tool or playbook node emits these events**. The existing tools emit text tokens, citations, and suggestions — not the new pane-control events.

**Impact**: Output pane shows loading state but never renders purpose-built widgets. Source pane stays empty.

**Fix needed**: Update existing BFF tool handlers (or create new middleware) to emit `ChatSseEventFactory.CreateOutputPaneEvent()` and `CreateSourcePaneEvent()` when tool results contain structured data. This is a BFF-side change — the frontend is ready.

**Estimated effort**: 4-6 hours (new task needed)

### Gap 2: OutputPanel SSE Event Discrimination

**Problem**: The `OutputPanel` currently defaults all streaming operations to `OutputWidgetType.AnalysisEditor`. The comment on line 261 says: *"Full SSE event parsing (widgetType discrimination) happens in task 041+."* This wasn't completed.

**Impact**: Even if the BFF emits `output_pane` events, the OutputPanel wouldn't parse the `widgetType` from the SSE payload to render the correct widget.

**Fix needed**: Wire `output_pane` SSE events from SprkChat's streaming callbacks into OutputPanel's slot management with proper `widgetType` discrimination.

**Estimated effort**: 2-3 hours (frontend change)

### Gap 3: Bing Grounding Connection (blocks FR-16)

**Problem**: No Bing Search resource exists in the Azure subscription. The Foundry agent was created without Bing Grounding. `LegalResearchTools` exist in the BFF but have no Bing connection to call.

**Impact**: Legal research queries (`/research`) won't return Bing-grounded results.

**Fix needed**: Create Bing Search resource in Azure Portal, create Bing connection in Foundry project, update agent with `bing_grounding` tool.

**Estimated effort**: 30 min (Azure Portal)

### Gap 4: AgentServiceClient Endpoint Format

**Problem**: The `AgentServiceOptions__Endpoint` was set to the full ML API path (`https://westus2.api.azureml.ms/agents/v1.0/subscriptions/.../workspaces/...`). Need to verify the `AgentServiceClient` uses this correctly — it was coded to use `Azure.AI.Projects` SDK which may expect a different endpoint format.

**Impact**: Agent Service calls may fail at runtime if endpoint format doesn't match SDK expectations.

**Fix needed**: Test an actual Agent Service call or inspect `AgentServiceClient` to verify endpoint usage.

**Estimated effort**: 1 hour (investigation + possible config fix)

### Gap 5: Deploy-AllWebResources.ps1 Entry

**Problem**: `Deploy-AllWebResources.ps1` has no entry for `sprk_spaarkeai`. Task 055 used `Deploy-WebResourceInline.ps1` as a workaround.

**Impact**: Future deployments need manual intervention or script update.

**Fix needed**: Add `sprk_spaarkeai` entry to `Deploy-AllWebResources.ps1`.

**Estimated effort**: 15 min

---

## Summary

| FR | Status | Blocker |
|----|--------|---------|
| FR-01 | Ready to test | None |
| FR-02 | Ready to test | None |
| FR-03 | Ready to test | None |
| FR-04 | Ready to test | None (SprkChat integration verified in code) |
| FR-05 | Blocked | Gap 1 (SSE events not emitted) + Gap 2 (event discrimination) |
| FR-06 | Blocked | Gap 1 (SSE events not emitted) |
| FR-07 | Blocked | Gap 1 (SSE events not emitted) |
| FR-08 | Ready to test | None |
| FR-09 | Ready to test | Manual nav/ribbon setup required |
| FR-10 | Ready to test | None |
| FR-11 | Partially testable | Registry loads but no live widgets without Gap 1 fix |
| FR-12 | Ready to test | None |
| DM | Ready to test | None |

**Verdict**: 8 of 12 FRs are testable now. FR-05/06/07 are blocked by SSE event wiring (Gaps 1+2). These gaps represent ~6-9 hours of additional work to complete Phase 1 fully.
