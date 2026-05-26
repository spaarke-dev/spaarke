# BFF Placement-Justification Retroactive Memo — R3

> **Date**: 2026-05-26
> **Author**: spaarke-dev (R4 Phase 0 / F-1 light)
> **Purpose**: Retroactively answer CLAUDE.md §10 Placement Justification questions for R3's BFF additions. The §10 rule was added 2026-05-19 — AFTER R3 design.md was scoped — so R3 design did not include this section. This memo closes the audit-trail gap.
> **Scope**: Limited to R3's actual BFF additions: task 050 (extend `ChatEndpoints.cs` POST messages with `attachments[]` schema) + task 051 (BFF unit tests).
> **Mode**: LIGHT memo. Substantive design content already lives in `notes/spikes/001-fr07-attachments-payload.md`; this memo re-formats those decisions into the §10 template.

---

## R3 BFF additions inventory

Single feature added: extending the existing `POST /api/ai/chat/sessions/{sessionId}/messages` endpoint to accept an optional `attachments[]` payload (FR-07).

| Change | Where | Lines | Type |
|---|---|---|---|
| `ChatSendMessageRequest.Attachments?: List<ChatMessageAttachment>` | `Api/Ai/ChatEndpoints.cs` DTO | ~5 | DTO field |
| `ChatMessageAttachment` record | `Api/Ai/ChatEndpoints.cs` | ~8 | New DTO |
| `ValidateAttachments(...)` helper | `Api/Ai/ChatEndpoints.cs` | ~80 | Inline validation |
| `ComposeMessageWithAttachments(...)` helper | `Api/Ai/ChatEndpoints.cs` | ~25 | String composition |
| Telemetry fields (attachment count + char count) on existing log | `Api/Ai/ChatEndpoints.cs` | ~5 | Observability |
| 4 constants (MaxAttachmentsPerMessage=5, MaxAttachmentTextCharsPerFile=2.5M, MaxAttachmentTextCharsTotal=5M, AllowedAttachmentContentTypes) | `Api/Ai/ChatEndpoints.cs` | ~10 | Configuration |
| Unit tests | `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsAttachmentsTests.cs` | ~200 | Test code |
| **Total** | | **~333 LOC** | One existing endpoint extended; no new endpoints created |

### What was NOT added

- ❌ No new endpoint (extended existing chat endpoint)
- ❌ No new service
- ❌ No new DI registration / feature module
- ❌ No new NuGet package
- ❌ No new background work / IJobHandler
- ❌ No new direct CRUD→AI dependency
- ❌ No new HIGH-severity CVE in transitive graph

---

## §10 Decision Criteria Answers

### 1. Could this functionality live OUTSIDE the BFF?

**No.** The change extends a single field on an existing in-pipeline AI chat endpoint. Functionally bound to:
- Existing `ChatEndpoints.cs` request lifecycle (OBO auth, rate limiting, endpoint filters)
- Existing AI orchestration call surface (`agent.SendMessageAsync`)
- Existing single-LLM-call invariant (D-01) — attachments compose into the SAME single LLM call, not separate extraction calls

Splitting this into Azure Functions would break:
- Single-request lifecycle for SSE streaming
- Single-LLM-call invariant (D-01)
- The "extract → compose → send to LLM" sequence within the user-perceived <500ms TTFB window

**Placement decision**: BFF. Required.

### 2. ADR citations binding the design

- **ADR-001** (Minimal API and Workers): Endpoint stays Minimal API with `.MapPost`; not MVC; no new background work
- **ADR-008** (Endpoint filters): Inherited from existing endpoint — auth filter, rate-limit filter, OBO filter all apply; attachment validation is in-handler payload validation, complementary to those filters (per `ValidateAttachments` doc comment line 348-349)
- **ADR-010** (DI minimalism): No new DI registration; no new feature module
- **ADR-013** (AI architecture): The change extends an existing AI in-pipeline endpoint with additional context; falls cleanly inside the "AI synthesis/chat/orchestration in BFF" rule (latency + transactional coupling justified)

### 3. Publish-size impact

**Zero.** No new NuGet packages added. Bundle delta: 0 MB compressed.

R3 publish-size measurements through Round 13 confirm no BFF growth from this feature. The R3 SpaarkeAi web resource grew (frontend bundle), but BFF publish stayed stable.

### 4. Direct CRUD→AI dependency check

**N/A.** This IS AI code (in `Api/Ai/`). It does not introduce any CRUD→AI dependency. The 2026-05-20 BFF AI extraction assessment found 20 existing direct deps from CRUD code into AI internals — R3 task 050 does not add to that count.

### 5. Feature-module DI convention check

**Pass.** The change is to an existing endpoint registered through the existing AI feature module. No new `Add{Feature}Module()` extension created (correctly — not needed).

---

## CVE / package risk check

R3 task 050 added no new NuGet packages. No CVE risk introduced. Baseline `dotnet list package --vulnerable --include-transitive` status preserved.

---

## Endpoint-filter / auth verification

The extended endpoint inherits all existing filters from the chat endpoint pipeline:
- OBO auth filter
- Rate limiting per ADR-008
- Endpoint-level authorization

`ValidateAttachments` is in-handler payload validation (file type / size / count) — complementary to, not replacing, those filters.

R3 task 060 (auth audit) verified clean across all R3 file additions including ChatEndpoints.cs changes.

---

## Substantive design content (already documented elsewhere)

This memo intentionally re-formats existing decisions into the §10 template rather than duplicating them. The substantive design content lives at:

- **Spike memo**: [`notes/spikes/001-fr07-attachments-payload.md`](spikes/001-fr07-attachments-payload.md) — the FR-07 backend spike that decided Phase E REQUIRED + defined the contract
- **Task POML**: [`tasks/050-extend-chat-endpoint-attachments-payload.poml`](../tasks/050-extend-chat-endpoint-attachments-payload.poml)
- **Task POML**: [`tasks/051-attachments-payload-bff-tests.poml`](../tasks/051-attachments-payload-bff-tests.poml)
- **Auth audit**: [`notes/audit-auth-2026-05-20.md`](audit-auth-2026-05-20.md) — confirms ChatEndpoints.cs additions pass ADR-028 / NFR-08

---

## Conclusion

**R3's BFF additions (tasks 050 + 051) pass all CLAUDE.md §10 pre-merge criteria retroactively.** No remediation required.

**Going forward**: R4 and subsequent projects will include the Placement Justification section in `design.md` prospectively per §10 (rule applies from 2026-05-19 forward). This retroactive memo is a one-time close-out for R3, not a precedent for auditing every prior project.

---

*End of retroactive memo.*
