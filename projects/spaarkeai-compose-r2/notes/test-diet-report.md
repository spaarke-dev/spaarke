# Test diet report — spaarkeai-compose-r2

**Run date**: 2026-07-15
**Branch**: work/spaarkeai-compose-r2
**Scope**: 19 .NET test files added/modified between merge-base `bcc15973a8b065c6c714018ac5ba9c7147838d09` and HEAD
**Classifier**: ADR-038 §7 (17 bans B1-B17) + `tests/CLAUDE.md` + `/test-diet` heuristics 1-12

---

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 181 | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 13 | listed below |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total test methods classified** | **194** | — |

**Headline**: No test qualifies for deletion. Zero SCAFFOLDING. The 13 AMBIGUOUS methods are constant-pin / JSON-round-trip / reachability-smoke shapes (B6/B12/B10/B16) — and **12 of the 13 pre-date this project** (they live in files this project touched only incidentally: the `sprk_document` FIX-F additions). Only 1 AMBIGUOUS method is a compose-r2 addition, and it has a behavioral sibling that already covers the same surface. Nothing here is a this-project delete obligation.

---

## Delete commands (DO NOT auto-execute — reviewer judgment required)

None. No whole-file or method-level deletions recommended.

---

## Path-move commands

None. All 19 files sit at a sanctioned home:

- 7 files under the KEEP paths `tests/integration/{contract,regression,seam}/**` → presumptively MAINTAIN.
- 12 files under `tests/unit/Sprk.Bff.Api.Tests/**` → the established service-level home for the BFF suite (per the project's sanctioned structure; not reflexively PATH-VIOLATION).

**Soft observation (not a violation)**: `DocxAnnotationWriterTests.cs` is a *pure* `byte[]→byte[]` domain transform (the file's own header declares ADR-038 KEEP category `domain-logic`) yet lives under `Services/Compose/` rather than `tests/unit/domain/**`. It is unambiguously MAINTAIN wherever it sits; a future path-normalization could move it, but that is optional hygiene, not a diet action.

---

## Ambiguous — reviewer judgment

Every row cites its B-number. All are AMBIGUOUS (not SCAFFOLDING) because each pins a *real* production contract (a cache key/TTL constant, an allowlist, a JSON shape) — just weakly expressed. None is biased toward delete.

| File:Method | Ambiguity reason (B#) | Suggestion |
|---|---|---|
| StandaloneChatContextEndpointsTests.cs:GetStandaloneContext_WithAuth_EndpointIsReachable | B10/B3 — asserts only `NotBe(404)`/`NotBe(405)` route-reachability smoke; no behavioral value beyond "route registered" | Pre-existing (not compose-r2). Low value; the `Returns200`/`Returns400` siblings already cover reachability behaviorally. Reviewer may drop. |
| StandaloneChatContextEndpointsTests.cs:StandaloneChatContextResponse_CanBeCreated_WithAllFields | B16/B17 — record/auto-property construction round-trip | Pre-existing. Compiler-guaranteed; deletable in a broader sweep, not this project's delta. |
| StandaloneChatContextEndpointsTests.cs:StandaloneChatContextResponse_RecommendedPlaybookId_IsOptional | B11/B16 — asserts a default (`null`) value | Pre-existing. Language-feature redundancy. |
| StandaloneChatContextEndpointsTests.cs:StandaloneChatContextResponse_SerializesToJson_WithCamelCase | B12 — snapshot of default `System.Text.Json` camelCase output | Pre-existing. Tests the serializer, not a contract. |
| StandaloneChatContextEndpointsTests.cs:StandaloneChatContextResponse_RoundTrips_ThroughJsonSerialization | B12 — JSON serialize→deserialize round-trip of a record | Pre-existing. |
| StandaloneChatContextEndpointsTests.cs:StandaloneContextField_IsRequired_DefaultsToFalse | B16 — pure auto-property default | Pre-existing. |
| StandaloneChatContextEndpointsTests.cs:StandaloneContextField_FieldType_LookupIsValid | B16 — pure getter/auto-property round-trip | Pre-existing. |
| StandaloneChatContextProviderTests.cs:CacheKeyPrefix_IsExpectedConstant | B6 — mirror; asserts a single constant equals `"tenant:"` | Pre-existing. The `BuildCacheKey_*` tests already assert the composed key format behaviorally. |
| StandaloneChatContextProviderTests.cs:ResolveAsync_CachesResult_With30MinuteAbsoluteTtl | B6 — constant-pin of `ContextCacheTtl == 30 min` | Pre-existing. **Exact duplicate** of `ContextCacheTtl_Is30Minutes` (below) — reviewer could collapse to one. |
| StandaloneChatContextProviderTests.cs:ContextCacheTtl_Is30Minutes | B6 — constant-pin (duplicate of the row above) | Pre-existing. Keep one of the two TTL-constant tests at most. |
| StandaloneChatContextProviderTests.cs:SupportedEntityTypes_ContainsAllExpectedTypes | B6 — mirror of the allowlist constant collection | Pre-existing. The `ResolveAsync_*` behavioral tests exercise the allowlist through real resolution. |
| StandaloneChatContextProviderTests.cs:ResolveAsync_Response_SerializesToJsonCorrectly | B12 — JSON round-trip of the resolved response | Pre-existing. Borderline (round-trips a *real* resolved object, not a hand-built literal) — lean keep. |
| StandaloneChatContextProviderTests.cs:SupportedEntityTypes_ContainsSprkDocument | B6 — mirror; asserts `SupportedEntityTypes.Contains("sprk_document")` | **compose-r2 addition (FIX F).** The only in-scope AMBIGUOUS. Its behavioral sibling `ResolveAsync_SprkDocument_ReturnsNonNull_WithEmptyContextFields` already proves the FIX-F contract end-to-end, so this constant-pin is redundant. Reviewer may keep for locality or drop. |

---

## Maintain — confirmed (no action) — by file

| File | KEEP path / home | Methods | Why maintain |
|---|---|---|---|
| tests/integration/contract/Api/Ai/ChatDocumentEndpointsContractTests.cs | contract | 9 | Real HTTP upload endpoint: indexing propagation, session-manifest persistence, 20-file cap (400), back-compat Redis writes, event-path 503/400 kill-switch, opt-out round-trip. Module-boundary mocks only (AI Search), no B1-B5. |
| tests/integration/contract/Api/Compose/ComposeActiveDocumentContractTests.cs | contract | 8 | Active-document register endpoint over real route + real `ChatSessionManager`: 401/400/404, compose-direct registration resolvable by summarize, tab-withdraw non-clobber (UAT), documentSessionId persistence + idempotent doc-session creation + Outputs-preservation. |
| tests/integration/contract/Catalog/ComposeR2OutputSchemaContractTests.cs | contract | 3 | Validates the 6 compose OutputSchemaJson mirrors against `OpenAiFunctionSchemaValidator` (H1-incident class): no property-level `required`, object-level required array, `additionalProperties:false`. |
| tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs | contract/Eval | 19 | Golden-utterance eval spine driving the REAL `ConsumerRoutingService` + schema-contract pins across P1/P2/P3; compose-r2 additions ground the 5 compose rows' dispositions against the REAL `sprk_playbookconsumer-rows.json` with non-vacuous discriminating guards. |
| tests/integration/regression/Compose/ComposeOutputsColdSessionTests.cs | regression | 2 | "every bug = one regression file" — cold document-session read returns null (→404), never the shipped `NotImplementedException` 500. Real `ChatDataverseRepository` cold path. |
| tests/integration/regression/Compose/Def14_ComposeSaveLockedDocumentTests.cs | regression | 4 | DEF-14 lock/precondition: Save surfaces 423/412 with actionable copy (not opaque 500); real Kiota `ODataError`→typed-exception translation via a *concrete* (non-Moq) Graph handler — B1-compliant. |
| tests/integration/seam/Ai/ComposeDocSessionDispatchSeamTests.cs | seam (E-40) | 2 | Vertical slice over the real app: register→dispatch→200 + persisted `compose` SessionOutput on the document session; negative contrast (unregistered → 404) proves the fix, not an always-resolving stub. |
| tests/unit/.../Api/Ai/StandaloneChatContextEndpointsTests.cs | unit (BFF) | 13 | Real HTTP context-mapping endpoint: auth (401), 200/400 validation matrix, supported-type Theory incl. FIX-F `sprk_document` 200-with-empty-mapping. (7 model/reachability methods → AMBIGUOUS above.) |
| tests/unit/.../Services/Ai/Chat/ComposeDocumentSessionRoutingTests.cs | unit (BFF) | 5 | DEF-11 routing: compose-disposition capability dispatches to the DOCUMENT session (vs chat), fail-soft when unregistered, non-compose stays in chat, revise-document editor-redirect (no dispatch). Recording orchestrator over the real virtual seam. |
| tests/unit/.../Services/Ai/Chat/SessionDispatchManifestProbeTests.cs | unit (BFF) | 6 | Bounded wait-or-degrade manifest readiness probe + active-document scoping (summarize-this-document) with FR-08 default-all preserved. Deterministic (0ms probe). |
| tests/unit/.../Services/Ai/Chat/SprkChatAgentFactoryWorkspaceStateTests.cs | unit (BFF) | 23 | `BuildWorkspaceStateBlock` prompt composition: per-widget shapes, ADR-015 privacy filtering, selection cap, compose-tab→DocumentViewer derivation (compose-r2), FR-58/59 visibility, token-budget reservation, truncation ceiling. |
| tests/unit/.../Services/Ai/Chat/StandaloneChatContextProviderTests.cs | unit (BFF) | 17 | Cache hit/miss behavior, unsupported-type→null (→400), field catalog per entity, case-insensitive resolution, FIX-F `sprk_document` supported-but-unmapped behavior. (6 constant-pin/round-trip methods → AMBIGUOUS above.) |
| tests/unit/.../Services/Ai/Handlers/SendWorkspaceArtifactHandlerTests.cs | unit (BFF) | 15 | SSE workspace-open frame emission, Compose DIRECT-widget flip vs layout door, D-F3 ack-gating + honest-fail (never fabricate "opened"), compose document/session-file pre-seed, mutual-exclusion validation. |
| tests/unit/.../Services/Ai/PostUploadIndexingEnqueuerTests.cs | unit (BFF) | 20 | OBO sync + app-only Service Bus dispatch, tracking-field stamping, all skip conditions (flag/tenant/size/content-type), non-fatal Dataverse write, no premature completion stamp (regression). |
| tests/unit/.../Services/Ai/PublicContracts/DocumentProfileAiTests.cs | unit (BFF) | 5 | OBO profiler facade on the ADR-043 spine: resolve→run→map→write, skip on no-SPE-pointer, skip on null AI seams, empty-text→Failed, best-effort swallow. Genuine module-boundary mocks. |
| tests/unit/.../Services/Compose/ComposeServiceCreateOnSaveTests.cs | unit (BFF) | 10 | Create-on-save backbone: drive-item/record/index steps + `JobAwareCompletionState` projection, fire-and-forget OBO profile dispatch (TCS-gated, no wall clock), no-token/throw guards, idempotent re-save, interim R5-E bar. |
| tests/unit/.../Services/Compose/ComposeServicePromoteRecordCompletenessTests.cs | unit (BFF) | 2 | UAT #7/8/9 regression: promoted `sprk_document` carries drive-id + has-file + full metadata (else downstream 409). Captures the real `CreateAsync` entity. |
| tests/unit/.../Services/Compose/ComposeServiceSaveAnnotationsTests.cs | unit (BFF) | 3 | Save routes redlines/comments through the REAL `DocxAnnotationWriter`; re-opens the SAVED OOXML to assert native `w:ins`/`w:del`/`w:comment`; no-annotation Save persists byte-identical baseline (FR-06a). |
| tests/unit/.../Services/Compose/DocxAnnotationWriterTests.cs | unit (domain-logic) | 15 | Pure OOXML transform: native markup emission + schema validity (`OpenXmlValidator`), comment-before-delete ordering, paragraph-mark deletion, monotonic ids, byte-stability, DEF-13/DEF-11 anchored multi-comment. |

---

## Count delta

- Test methods touched during project: **194**
- MAINTAIN: **181**
- SCAFFOLDING: **0**
- AMBIGUOUS: **13** (12 pre-existing, 1 compose-r2 addition)
- Net post-diet expected count after reviewer-confirmed deletes: **194** (no deletions recommended; the AMBIGUOUS bucket is reviewer-optional low-value trimming, not a diet obligation)

---

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17. This project's deltas are overwhelmingly maintain-class: contract anchors, regression protectors, and branched-behavior tests under the 7 KEEP paths, plus service-level behavior tests in the sanctioned BFF unit home. The only ban-shaped residue is a small set of pre-existing constant-pin/JSON-round-trip helpers (B6/B12/B16) that predate this project.
