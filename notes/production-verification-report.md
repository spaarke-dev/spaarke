# Task 123 — Production Verification Report

**Status**: READY FOR EXECUTION
**Date Prepared**: 2026-05-17
**Dependencies**: Tasks 121 (BFF API deployed) + 122 (SpaarkeAi web resource deployed)

---

## Overview

End-to-end verification of the R2 AI Platform Unification deployment. Each check must be executed against the deployed environment and results recorded in the tables below.

**Environment**: _______________
**BFF API URL**: _______________
**Dataverse URL**: _______________
**Cosmos Account**: _______________
**Tester**: _______________
**Date**: _______________

---

## 1. Safety Pipeline Verification

The safety pipeline (Content Safety + Prompt Shields + Groundedness Detection) must be active on all AI endpoints.

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 1.1 | Send normal chat message | Response returned without safety flags | | |
| 1.2 | Send prompt injection attempt (e.g., "Ignore all previous instructions and...") | Blocked or flagged by Prompt Shield; response contains safety warning or is rejected | | |
| 1.3 | Send message with indirect attack in document context | Flagged by document attack detection in Prompt Shields | | |
| 1.4 | Verify groundedness check on RAG response | Response includes groundedness score; ungrounded claims flagged | | |
| 1.5 | Check safety telemetry in App Insights | Custom events for `PromptShieldResult`, `GroundednessResult` visible in App Insights | | |
| 1.6 | Verify Content Safety resource connectivity | BFF startup logs show successful Content Safety client initialization | | |

---

## 2. Capability Routing Verification

Layer 1/2/3 capability routing must correctly classify and route requests.

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 2.1 | Layer 1 (simple lookup) query | Routed to direct Dataverse query, low latency (<2s) | | |
| 2.2 | Layer 2 (analysis) query | Routed to analysis pipeline, SSE streaming with progress events | | |
| 2.3 | Layer 3 (agentic/multi-step) query | Routed to orchestrated pipeline with plan approval flow | | |
| 2.4 | Routing telemetry in App Insights | Custom dimensions include `capabilityLayer`, `routingDecision`, `modelUsed` | | |
| 2.5 | Playbook-based routing | Correct playbook selected based on entity context and user intent | | |
| 2.6 | Fallback routing | Unknown intent gracefully handled with appropriate user message | | |

---

## 3. Cosmos DB Write-Through Verification

All AI interactions must persist to Cosmos DB containers.

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 3.1 | Session created in `sessions` container | New document with `tenantId` partition key, session metadata | | |
| 3.2 | Prompts stored in `prompts` container | Each message/response pair creates a document | | |
| 3.3 | Audit record in `audit` container | AI action audit trail document with timestamp, userId, action type | | |
| 3.4 | Memory snapshot in `memory` container | Context/preferences persisted after session interaction | | |
| 3.5 | Feedback stored in `feedback` container | Submit feedback via UI; verify document in Cosmos Data Explorer | | |
| 3.6 | Partition key isolation | Documents scoped to correct `tenantId`; cross-tenant query returns empty | | |
| 3.7 | TTL enforcement | Verify `sessions`, `prompts`, `memory` have `ttl` field set (7776000s) | | |
| 3.8 | Audit immutability | `audit` documents have `ttl: -1` (no expiry) | | |

### Data Explorer Verification Procedure

```
Azure Portal > Cosmos DB > spaarke-cosmos-{env} > Data Explorer

1. Select database: spaarke-ai
2. For each container (sessions, prompts, audit, memory, feedback):
   a. Click "New SQL Query"
   b. Run: SELECT TOP 5 * FROM c ORDER BY c._ts DESC
   c. Verify documents exist and have correct structure
   d. Check partitionKey matches /tenantId
```

---

## 4. Session Restore Verification

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 4.1 | Create session with multi-turn conversation | Session persisted after each turn | | |
| 4.2 | Close browser and reopen SpaarkeAi | Session list shows previous sessions | | |
| 4.3 | Restore session | Full conversation history, widgets, and context recovered | | |
| 4.4 | Continue conversation in restored session | New messages append correctly; context maintained | | |
| 4.5 | Restore session from different browser/machine | Same session accessible (via Cosmos persistence) | | |
| 4.6 | Session with expired TTL | Graceful handling — "Session not found" message, no crash | | |

---

## 5. Widget Rendering Verification

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 5.1 | Findings widget | Findings list renders with severity indicators | | |
| 5.2 | Risk assessment widget | Risk matrix or score card displays correctly | | |
| 5.3 | Safety/confidence indicators | Confidence score and safety badge visible on AI responses | | |
| 5.4 | Feedback widget | Thumbs up/down + text feedback form functional | | |
| 5.5 | Document diff widget | Before/after comparison renders for document edits | | |
| 5.6 | Citation markers | Inline citations link to source documents | | |
| 5.7 | Widget serialization (persist/restore) | Widgets survive session restore — same state, same data | | |
| 5.8 | Widget error boundary | Widget failure isolated — other widgets and chat unaffected | | |

---

## 6. Cross-Pane Integration Verification

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 6.1 | Source selection updates chat context | Selecting a document in LeftPane injects context into ChatPanel | | |
| 6.2 | Chat citation links to source | Clicking citation in chat highlights/scrolls to source in LeftPane | | |
| 6.3 | Analysis output renders in OutputPanel | Completed analysis populates right pane with structured output | | |
| 6.4 | Embedded wizard launches from chat | Wizard-type interactions open inline without leaving workspace | | |
| 6.5 | Stage lifecycle transitions | Multi-stage analysis progresses through stages with UI updates in all panes | | |
| 6.6 | Pane resize persistence | Resized pane widths maintained across interactions | | |

---

## 7. Audit Log Verification

| # | Check | Expected Result | Actual Result | Pass/Fail |
|---|---|---|---|---|
| 7.1 | AI action logged | Every AI request creates an audit record | | |
| 7.2 | User identity captured | `userId`, `tenantId` fields populated correctly | | |
| 7.3 | Timestamp accuracy | `_ts` and custom timestamp fields reflect actual execution time | | |
| 7.4 | Action type classification | Audit records distinguish between chat, analysis, feedback actions | | |
| 7.5 | Data access scope recorded | Which entities/documents were accessed during the AI action | | |
| 7.6 | Safety decisions logged | Prompt shield and groundedness results recorded in audit | | |
| 7.7 | No PII in audit logs | Verify audit records do not contain raw user prompts (if PII policy applies) | | |

---

## 8. Performance Baseline (Optional)

| # | Metric | Target | Actual | Pass/Fail |
|---|---|---|---|---|
| 8.1 | Health check response time | < 200ms | | |
| 8.2 | Session create latency | < 500ms | | |
| 8.3 | First SSE token latency | < 2s | | |
| 8.4 | Session restore latency | < 1s | | |
| 8.5 | Widget render time | < 500ms | | |
| 8.6 | SpaarkeAi page load time | < 3s | | |

---

## Sign-Off

| Role | Name | Date | Signature |
|---|---|---|---|
| Developer | | | |
| QA Tester | | | |
| Product Owner | | | |
| Operations | | | |

### Overall Result

- [ ] **PASS** — All checks passed. R2 AI Platform Unification is production-ready.
- [ ] **CONDITIONAL PASS** — Minor issues noted (list below). Acceptable for production with known limitations.
- [ ] **FAIL** — Critical issues found. Do not proceed to production.

### Issues Found

| # | Severity | Description | Resolution |
|---|---|---|---|
| | | | |

### Notes

_Additional observations, performance notes, or recommendations for follow-up:_
