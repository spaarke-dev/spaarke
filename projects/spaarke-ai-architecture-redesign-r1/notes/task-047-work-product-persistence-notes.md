# Task 047 — Work-product record persistence (FR-P3-08) — Task Notes

> Date: 2026-07-06 · Wave W-P3-C (parallel with 045) · task-execute FULL rigor.
> Dataverse rows/GUIDs: [`task-047-dataverse-changes.md`](task-047-dataverse-changes.md).

## What landed

The `work_product` disposition leg of the task-021 `OutputRouter` — Binding-declared persistence of capability outputs to host Dataverse records, generalizing the widgets-r1 topic-registry pattern from "one `UpdateRecord` node per playbook" to a platform leg with **no per-capability persistence code**.

| Piece | Where |
|---|---|
| Persistence seam (043 email-sender shape) | `src/server/api/Sprk.Bff.Api/Services/Ai/WorkProductRecordPersister.cs` — `IWorkProductRecordPersister` + `TopicRegistryWorkProductPersister` + `WorkProductEnvelope` + `WorkProductPersistenceReceipt` |
| Router leg (store FIRST, persist AFTER, loud failures) | `OutputRouter.cs` — WorkProduct case + `PersistWorkProductAsync`; optional ctor dep mirrors the email sender |
| Dispatch envelope widened | `SessionDispatchOrchestrator.cs` — pre-run guard now admits Informational + WorkProduct; Overlay/Record/Notification still 422 `dispatch.disposition-not-supported`; Email deliberately stays non-dispatchable (043 decision) |
| DI | `AnalysisServicesModule.cs` — `IWorkProductRecordPersister` Scoped in the compound-AI-ON block (next to `IEmailDispositionSender`); consumes the UNCONDITIONAL `IDataverseUserClient` typed HttpClient |
| Envelope contract (NFR-06 pin) | `infra/dataverse/outputschemas/work-product-envelope-v1.schema.json` |
| Catalog | Binding `05618e5d-ab79-f111-ab0e-7ced8ddc4cc6` (chat-summarize/`matter-summary`, disposition Work Product, SUM-CHAT@v1) + registry `cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6` (`matter-summary` → `sprk_matter.sprk_mattersummary`) + NEW published memo column `sprk_matter.sprk_mattersummary` |
| Eval (NFR-06) | `golden-utterances.json` +GU-060 (text) +GU-061 (click), family `matter-summary`, activation P3/task 047 |

## The declaration contract (ADR-039 tension, resolved)

- **WHETHER** an output persists: the Binding row alone — `sprk_disposition = work_product` (the single routing surface; ADR-039).
- **WHERE** it persists: the capability's `sprk_aitopicregistry` row — `sprk_topicname` = the Binding's capability code (`ConsumerCode` when meaningful, else `ConsumerType`; `default` → type), row supplies `sprk_hostentity` + `sprk_targetfield`. This is the SAME role the registry already plays for the shipped insights topics (`matter-health` → `sprk_performancesummary`); it carries target-mapping DATA, not a routing decision — no config key, no code list, **no new manifest tables** (spec out-of-scope rule satisfied: declaration reuses FR-P0-03 Binding columns + existing registry columns).
- **WHICH record**: the session's `HostContext` (entity type validated against the registry's declared host entity via the shared `EntityTypeNormalizer` vocabulary; record id = `HostContext.EntityId`). No host context, or a mismatched host entity → loud `InvalidOperationException` AFTER the ledger store (entry stays addressable; never a silent skip).

## The persisted envelope (`work-product-envelope-v1`)

Derived VERBATIM from the stored ledger `SessionOutput` (ADR-040: the ledger is the source; the record copy is a projection), serialized as a JSON string into the registry-declared longtext column:

```json
{
  "schemaVersion": "1.0",
  "ledgerKey": "{bindingId}@t{n}",
  "bindingId": "…",
  "ucId": "UC-A-1",
  "turn": 3,
  "disposition": "work_product",
  "generatedAt": "2026-07-06T12:00:00Z",
  "sourceRefs": ["file-…"],
  "payload": { "…the schema-validated capability output, verbatim…" }
}
```

Required: all members except `sourceRefs` (omitted when none). `additionalProperties: false`. Generalizes the widgets-r1 FR-14 envelope (schemaVersion + generatedAt kept; topic-specific members → ledger identity + payload). Pinned at `infra/dataverse/outputschemas/work-product-envelope-v1.schema.json`; conformance + store-then-persist ordering asserted by `WorkProductEnvelopePersistenceContractTests`.

## ADR-040 ordering + idempotency (test-proven)

- **Store precedes persist**: `OutputRouterTests.RouteAsync_WorkProductDisposition_StoresEntryThenPersistsToHostRecord` (StoredCountAtPersist=1) + the contract round-trip's `events.Should().ContainInOrder("store", "patch")`. A persistence failure propagates AFTER the ledger write — entry stays addressable.
- **Idempotency per `{bindingId}@t{n}`**: single-field PATCH with `If-Match: *` (update-only — can never upsert-create); re-routing the same entry re-issues a byte-identical overwrite; `PostAsync` verified never called (`PersistAsync_RepeatedForSameEntry_IsIdempotent_IdenticalPatchNoCreates`). Across turns: last-write-wins on the target field — the same semantics the widgets-r1 node ships.
- **User-OBO**: every call (metadata reads, registry read, PATCH) through `IDataverseUserClient` (fail-closed; the user's own 403/404 surfaces — `PersistAsync_DataversePatchFails_ThrowsLoudCarryingUsersOwnError`).

## FR-P2-02 gating posture

The persistence write is router-performed (not tool-plane), so the Binding's `sprk_risk` is its declared gate surface; seeded `None` matching widgets-r1 + 041/042 precedent (see dataverse-changes note §2). Loop-invoked capability executions still pass the ONE gate machinery by declaration; flipping `sprk_risk` gates this leg with zero code change.

## ADR-040 inline size-cap — status: NOT in this task's POML (escalation)

TASK-INDEX task-021 row says "size-cap enforcement deferred to 047", but the 047 POML's steps/acceptance criteria do NOT prescribe the blob/SPE-pointer offload, and building an unprescribed storage path fails CLAUDE.md §11 (no concrete prescribed contract). What exists after 047: the >128 KB Warning observability (task 021) unchanged, plus work_product envelopes now ALSO durably persisted OUT of the session (host record). The inline-ledger pointer-swap remains unimplemented. **🔔 Operator ruling requested at gate 048**: land the offload as a P4 hardening item (FR-P4-06 window) or Track-B follow-up. `OutputRouter` remarks updated to state the real status (no stale "lands at P3" promise).

## Session-less record-context runs (E-2 adapter pointer, closed)

Task-024/044 comments pointed "record-context runs join the ledger at P3 FR-P3-08". Evaluated and NOT built: the ledger is session-scoped by design (ADR-040 rides the session store); session-less engine runs (insights endpoints, scheduler, app-only ingest) have no ledger carrier and their record persistence is already the playbook-node instance of the pattern (widgets-r1 `persistEnvelope`). FR-P3-08's generalization covers session-attached capabilities via the OutputRouter leg. `EngineOutputLedgerAdapter` comments + log text updated to the landed reality (no code-path change).

## Contract notes for 045/046 (client — NOT touched here, per wave boundary)

- The dispatch terminal chunk for a work_product Binding renders the stored payload exactly like informational (one AnalysisChunk vocabulary). Canonical §3.10.3 wants the client to render work_product as "Workspace-primary + Assistant 'see Workspace' line" — **046 (widget layer) may want the Binding disposition surfaced on the terminal chunk** to drive that. Server contract today: disposition is NOT on the wire chunk; it IS on the ledger entry (`SessionOutput.Disposition`) and the Binding row the client dispatched by id. No server change required for 048's UAT (the envelope lands on the record regardless of how the chat renders the summary).
- No client render change was needed for this task's acceptance; if 046 adds a disposition marker to the chunk, extend `AnalysisChunk` there (wire-additive).

## UAT additions for gate 048 (G-P3 browser)

1. Open the SpaarkeAi assistant ON A MATTER FORM (session must carry HostContext for the matter) on spaarkedev1, upload/attach a document.
2. Say: **"summarize this document and save the summary to the matter"** (GU-060). Expect: summary renders in chat AND the matter's **Matter Summary (AI Work Product)** field (`sprk_mattersummary`; add to a form section or check via Advanced Find/form editor) now holds the `work-product-envelope-v1` JSON — `ledgerKey`, `payload.tldr/summary/keywords/entities`, `generatedAt` populated.
3. Repeat the utterance → field OVERWRITTEN with a new envelope (higher `turn`, new `ledgerKey`); no duplicate rows anywhere.
4. Negative: in an assistant session NOT hosted on a record, invoke the same capability → honest error surfaces (no silent skip); the summary is still addressable in session (ledger).
5. Persisted-envelope visibility satisfies FR-P3-08 acceptance "observed in UAT".

## Verification summary (SHOWN in transcript)

- `dotnet build` (BFF + tests): 0 errors.
- Targeted suites: OutputRouter+persister 21/21; envelope+dispatch contract 17/17; **eval `Category=GoldenUtteranceEval` 35/35 green** (NFR-02 merge gate) after +2 cases.
- Full unit suite + NetArchTest + publish size: recorded in the task transcript (Step 5).
