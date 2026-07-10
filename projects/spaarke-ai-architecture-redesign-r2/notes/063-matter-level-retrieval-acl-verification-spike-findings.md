# Task 063 — Matter-Level Retrieval ACL Verification Spike — Findings

> **Status**: READ-ONLY spike complete. **Verdict: IDENTIFIES-GAP** (escalates per pre-declared security path — see §6).
> **Date**: 2026-07-09
> **Scope**: FR-B-14 — is matter-level access enforced at RETRIEVAL time in the AI Search filter, or only at conversation-history sanitization?

---

## 1. Summary

The narrow question the spike anchor posed — *"does `RagService.cs:1238` unconditionally append `PrivilegeFilterBuilder` at retrieval, distinct from and in addition to `ConversationHistorySanitizer`'s history-stripping control?"* — is **CONFIRMED TRUE**. The code-level mechanism described in `plan.md` §3 is real and correctly wired.

However, tracing the control one layer deeper (the data that feeds the filter, and the authorization boundary around which matter a request is scoped to) surfaces a **genuine ethical-wall gap** that is more serious than the binary the spike was scoped to check:

1. **The AD-group-based privilege filter is unconditionally applied, but the data it filters on is never populated.** No ingestion code path in this repository ever sets `KnowledgeDocument.PrivilegeGroupIds` to a real Azure AD group ID. Every document indexed today has an empty `privilege_group_ids` collection, which the filter's own `not privilege_group_ids/any()` clause always treats as "public." The group-based half of the retrieval ACL is live code over dead data — it cannot currently discriminate between any two users.
2. **The only retrieval-time control that DOES discriminate between matters — the `parentEntityType`/`parentEntityId` filter — is populated directly from a client-supplied value with no BFF-side authorization check** that the calling user is entitled to view that specific matter record. The one endpoint filter that performs resource-level AI authorization (`AiAuthorizationFilter`) does not inspect this value at all, and neither the chat endpoint handler nor the session manager perform an equivalent check.

Net effect: for the AI-chat RAG retrieval path, matter-level ethical-wall enforcement today rests entirely on trusting the client to supply a matter ID the user is legitimately viewing — there is no independent BFF-side verification. This is a real gap, not a documentation nuance.

---

## 2. Evidence — Part A: the anchor claim (CONFIRMED)

**`RagService.cs:1238-1242`** (knowledge-index path, i.e. non-session-scoped RAG retrieval — the customer-corpus/document grounding path used by AI chat):

```csharp
// Privilege filter — ALWAYS applied (AIPU2-027 security requirement).
// Ensures only documents the user's groups are authorised to view are returned.
// Null userGroupIds means system/background call: treat as public-only.
var privilegeFilter = PrivilegeFilterBuilder.BuildFilter(userGroupIds ?? Array.Empty<string>());
filters.Add(privilegeFilter);
```

This is unconditional — there is no `if` guarding it in the `else` branch (`RagService.cs:1158-1243`, the knowledge-index path). It is ANDed with every other filter (`ParentEntityType`/`ParentEntityId`, tags, knowledge source, etc.) via `string.Join(" and ", filters)` at `RagService.cs:1245`.

**Contrast — `RagService.cs:1152-1157`**: under **session-scoped routing** (the `spaarke-session-files` index, used when `options.SessionId` is set), the privilege filter is explicitly **SKIPPED** — the schema doesn't declare the column, and isolation is enforced instead by the `sessionId eq '...'` clause (comment cites "the chat session owner already passed authorization to upload the file"). This is a documented, intentional exception for session-uploaded files, not the customer-corpus knowledge path the spike anchor concerns.

**`ConversationHistorySanitizer.cs`** (`Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs`) is a **separate, additional** control: on a matter pivot within one chat session, it strips retrieved document *passages already in the LLM's conversation history* (`StripRetrievedContent`, lines 55-146), replacing them with a privacy placeholder. It operates on history, not on what a NEW retrieval call is allowed to return. `notes/privilege-leakage-test-report.md` (2026-05-17, pre-existing) documents this as "Defence Layer 2" alongside the retrieval-layer filter as "Defence Layer 1" (§5, lines 345-349) — corroborating that these are two distinct, both-present controls, not a single control masquerading as two.

**Conclusion on Part A**: the spike anchor's claim is accurate — retrieval-time filtering is a real, unconditionally-invoked, separate mechanism from history sanitization. If the spike had stopped here, the verdict would be CONFIRMS-control as anticipated.

---

## 3. Evidence — Part B: what the retrieval-time filter actually filters on (the gap)

### B.1 — `PrivilegeGroupIds` is never populated with real data

`PrivilegeFilterBuilder.cs` (lines 43-67) builds an OData filter against the AI Search field `privilege_group_ids`. Its own doc comment (lines 17-20) states the fail-closed behavior: **a user with NO groups gets only documents where `privilege_group_ids` is empty** (public documents). The mirror image is equally true and is the crux of the gap: **a document with an empty `privilege_group_ids` is returned to EVERY user**, regardless of that user's groups, because the filter always ORs in `not privilege_group_ids/any()`.

Searching the entire `src/` tree for every place `PrivilegeGroupIds` is touched:

| Location | What it does |
|---|---|
| `Models/Ai/KnowledgeDocument.cs:260` | Declares the property with default `= new List<string>()` (empty) |
| `Services/Ai/FileIndexingService.cs:308-331` | **The production entry point that builds every `KnowledgeDocument` for the knowledge-index (customer-corpus) ingestion path.** Sets `Id`, `TenantId`, `DocumentId`, `SpeFileId`, `FileName`, `FileType`, `Content`, `ChunkIndex`, `ChunkCount`, `KnowledgeSourceId`, `KnowledgeSourceName`, `Metadata`, `Tags`, `CreatedAt`, `UpdatedAt`, `ParentEntityType`, `ParentEntityId`, `ParentEntityName` — **`PrivilegeGroupIds` is never assigned**, so every indexed chunk carries the model default (empty list). |
| `Services/Ai/RagIndexingPipeline.cs:475` | Explicitly nulls it out — but only for the session-files write path (a different schema); does not set it for the knowledge-index path either. |
| `Services/Jobs/RecordSyncJob.cs:121, 545` | A *different* index (`spaarke-records-index`, `RecordSearchDocument`, used by `RecordSearchService` for Dataverse record entity-resolution search — not the AI-chat RAG grounding path) also declares a `PrivilegeGroupIds` property and also only ever sets it to `new List<string>()` (empty). Confirmed out of scope for this spike's specific anchor (`RagService.cs:1238`), noted here because it shows the same "field exists, never populated" pattern recurs on a second surface. |

There is **no code path anywhere in this repository** that tags a `KnowledgeDocument` with an actual Azure AD security-group ID representing a matter's ethical wall / conflict wall at ingestion time. `IPrivilegeGroupResolver` / `PrivilegeGroupResolver.cs` correctly resolves what groups the *querying user* belongs to (via JWT claims or Graph `/me/memberOf`) — that half of the mechanism works. But the *document* side of the comparison is always empty, so the AND of "user's groups" against "document's groups" is structurally unable to ever produce a non-public restriction today.

**Practical consequence**: `PrivilegeFilterBuilder.BuildFilter(...)` is real, unconditional, well-tested code (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Security/PrivilegeFilterBuilderTests.cs`, `PrivilegeAwareRagServiceTests.cs`) — but it is exercised only against synthetic test data. In production, it currently degrades to a no-op AND clause for every query, because `not privilege_group_ids/any()` is true for 100% of indexed documents.

### B.2 — The one filter clause that DOES vary by matter has no authorization check behind it

The only retrieval-time predicate that actually differs per-matter today is the entity-scope filter:

```csharp
// RagService.cs:1231-1236
if (!string.IsNullOrEmpty(options.ParentEntityType) && !string.IsNullOrEmpty(options.ParentEntityId))
{
    filters.Add($"parentEntityType eq '{EscapeFilterValue(options.ParentEntityType)}'");
    filters.Add($"parentEntityId eq '{EscapeFilterValue(options.ParentEntityId)}'");
}
```

`options.ParentEntityType` / `ParentEntityId` are populated directly from `ChatHostContext.EntityType` / `EntityId` (`Services/Ai/Chat/PlaybookChatContextProvider.cs:323-324` and `:502-503`), and `ChatHostContext` is a value the **client supplies verbatim** in the session-create/send-message request bodies (`Models/Ai/Chat/ChatHostContext.cs`; `ChatCreateSessionRequest` / `ChatSendMessageRequest` in `Api/Ai/ChatEndpoints.cs`).

Tracing the authorization chain for this value:

- `ChatEndpoints.CreateSessionAsync` (`Api/Ai/ChatEndpoints.cs:323-355`) validates only that a `tid` (tenant) claim is present (`ExtractTenantId`, lines 330-337). It never checks whether the calling user has read/access rights to `request.HostContext.EntityId`.
- `ChatSessionManager.CreateSessionAsync` (`Services/Ai/Chat/ChatSessionManager.cs:101-130`) persists the `HostContext` onto the session record as-is (line 119) with no matter-access check.
- `ChatHostContext.IsValid()` (`Models/Ai/Chat/ChatHostContext.cs:84-87`) only checks that `EntityType`/`EntityId` are non-blank and that `EntityType` is one of the known enum values (`matter`, `project`, `invoice`, `account`, `contact`) — a **format** check, not an **authorization** check.
- The one endpoint filter in this codebase that performs resource-level AI authorization, `AiAuthorizationFilter` (`Api/Filters/AiAuthorizationFilter.cs`), is attached to the chat routes via `.AddAiAuthorizationFilter()` (confirmed on the session/message routes in `ChatEndpoints.cs`). Its `ExtractDocumentIds` helper (lines 124-147) only recognizes `DocumentAnalysisRequest.DocumentId` or a bare `Guid` argument. Neither `ChatCreateSessionRequest` nor `ChatSendMessageRequest` present a `Guid` in that shape, so `ExtractDocumentIds` returns an empty list and the filter takes the explicit early-exit path: *"If no document IDs are present (e.g. session-scoped endpoints where the session ID acts as the authorization scope), pass through to the next filter — the endpoint handler performs its own tenant/session ownership checks"* (`AiAuthorizationFilter.cs:71-78`). Per the trace above, **neither the endpoint handler nor the session manager actually perform that check** for the matter ID.

**Practical consequence**: the BFF has no server-side verification, anywhere in the chat/RAG request path, that the authenticated user calling `/api/ai/chat/sessions` (or `/messages`) with `HostContext.EntityId = <matter guid>` is actually authorized to view that matter. If the surrounding Dataverse/UI layer that launches the chat panel always enforces this correctly (i.e., a user can never even navigate to open a chat panel against a matter they lack access to), there is no live exploit path through the UI. But the BFF endpoint itself does not independently enforce it — a client that could reach the API directly (a modified client, a replayed/tampered request, a future integration) could request any matter's document content by supplying its `EntityId`, and the BFF would return that matter's indexed chunks with no rejection.

---

## 4. Contrast: retrieval vs. history-sanitization (the spike's original framing)

| Layer | Mechanism | Discriminates by matter today? |
|---|---|---|
| Retrieval — AD-group privilege filter | `PrivilegeFilterBuilder` @ `RagService.cs:1238` | **No** — unconditionally invoked, but `privilege_group_ids` is never populated on any indexed document, so it is structurally a no-op (always resolves to "public") |
| Retrieval — entity-scope filter | `parentEntityType`/`parentEntityId` @ `RagService.cs:1231-1236` | **Yes, but unauthorized** — actually restricts results to one matter, but the matter ID is client-supplied with no BFF-side check that the caller may view that matter |
| Conversation history | `ConversationHistorySanitizer` @ `Safety/CrossMatter/ConversationHistorySanitizer.cs` | Yes — strips previously-retrieved passages from LLM context on a detected matter pivot, but this is a within-session hygiene control, not an access-control gate on a NEW retrieval |

So the answer to the spike's literal question ("only at sanitization, or also at retrieval?") is: **retrieval-time code exists and is exercised on every query** — the anchor's mechanism-level claim holds. But **the retrieval-time code does not currently enforce a matter-level ethical wall in practice**, because (a) its AD-group half has no real data behind it, and (b) its matter-ID half has no authorization check behind it. Functionally, today's live matter isolation for AI retrieval is closer to "whatever the client asked for, unchecked" than "an ethical wall."

---

## 5. Constraints honored

- **READ-ONLY**: no production code was modified. This finding note is the only artifact produced by this spike.
- All evidence is cited with file:line references to the actual current code, verified by direct reads (not assumption from the anchor / plan.md description).
- No fix is attempted in-scope, per the constraint and the escalation trigger below.

---

## 6. 🔔 Security Escalation (per CLAUDE.md §6 + this task's pre-declared escalation path)

**Trigger fired**: the spike's escalation clause reads *"If the spike finds matter walls are enforced ONLY at history-sanitization and NOT at retrieval... this is security-sensitive... invoke the pre-declared escalation: file it as its own security project with the evidence; do not remediate within core r2 scope."*

The literal binary (retrieval-enforcement-absent vs. present) resolved to "present" — but the deeper trace shows the retrieval-time enforcement that IS present cannot currently discriminate between matters or users in a security-meaningful way (§3 above). This is the same class of risk the escalation clause anticipates (a live ethical-wall gap at the retrieval layer), just discovered one layer deeper than the anchor's framing anticipated. Per CLAUDE.md §6 ("Human Escalation Triggers... Security-sensitive code") and this task's own pre-declared path, this is surfaced rather than fixed in-scope.

- **ADR in question**: none directly — this is a data-flow/authorization gap, not an ADR conflict. Closest governing references: AIPU2-027 (privilege filter security requirement, cited in `PrivilegeFilterBuilder.cs` and `RagService.cs:1239`) and ADR-015 (AI data governance).
- **Finding**: (1) `KnowledgeDocument.PrivilegeGroupIds` is never populated with real Azure AD group IDs anywhere in the ingestion pipeline (`FileIndexingService.cs:308-331`), making the AD-group half of the retrieval-time privilege filter a structural no-op against production data. (2) The `parentEntityType`/`parentEntityId` matter-scope filter is populated from an unauthenticated, unauthorized, client-supplied `ChatHostContext.EntityId` with no BFF-side check anywhere in the request path (`ChatEndpoints.cs`, `ChatSessionManager.cs`, `AiAuthorizationFilter.cs`) that the calling user may access that specific matter.
- **Severity/scope**: potential cross-matter document content disclosure via the AI chat/RAG retrieval path, contingent on whether upstream UI/session provisioning reliably prevents a user from ever supplying an unauthorized matter ID. Recommend treating as a genuine gap requiring its own scoped remediation project rather than assuming the UI-layer mitigation is sufficient.
- **Recommended next step (for the human / a follow-on project, NOT this spike)**: (a) decide the intended design — either (i) populate `PrivilegeGroupIds` at ingestion from the matter's actual Dataverse-derived AD security group(s) and add a BFF-side authorization check on `HostContext.EntityId` before honoring it as a search scope, or (ii) if per-record Dataverse-layer or SPE-container-permission enforcement is intended to be the actual control and the AI Search `privilege_group_ids` field is legacy/aspirational, retire or clearly document it as such; (b) add integration coverage (ADR-038 KEEP path: `tests/integration/auth/**` or `tests/integration/tenant/**`) that a user without matter access cannot retrieve that matter's chunks through the chat API.
- **Do NOT fix under this task** — per the pre-declared constraint, this spike stops at the finding.

---

## 7. Acceptance criteria status

| Criterion | Status |
|---|---|
| Written finding states whether matter walls are enforced at RETRIEVAL time with file:line evidence | ✅ done — §2, §3 |
| Finding contrasts retrieval-time vs. history-sanitization enforcement, concludes CONFIRMS-control or IDENTIFIES-GAP | ✅ done — §4; verdict **IDENTIFIES-GAP** |
| No production code changed | ✅ confirmed — read-only spike, no edits made |
| NEGATIVE: if a gap is found, escalated as a separate security project, not fixed in-scope | ✅ done — §6; no remediation attempted here |

Nothing in this task requires main-session build/test verification (no code was touched); the main session should treat §6 as the actionable output and decide whether/how to open a follow-on security project.
