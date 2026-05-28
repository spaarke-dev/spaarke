# sprk_analysis Polymorphic Confirmation — Task 051 First-Step Blocker Resolution

> **Created**: 2026-05-28
> **Task**: 051 — D-P11 Observation mirror sync to sprk_analysis polymorphic
> **Author**: Claude Code (task-execute, FULL rigor)
> **Source**: `mcp__dataverse__describe_table("sprk_analysis")` against Spaarke Dev environment

---

## TL;DR

The POML's premise — that `sprk_analysis` has a "polymorphic source-type pattern" with a discriminator picklist — **is not present in the actual schema**. The entity is a single-shape AI analysis-run record with required FKs to `sprk_document` and `sprk_analysisaction`. No `sprk_sourcetype`, no `sprk_artifacttype` field exists.

D-56 superseded the "Snapshot via sprk_analysis polymorphic" pattern as deferred. D-60 retains the per-Observation mirror to `sprk_analysis` for the model-driven review surface but inherited the "polymorphic" framing from the superseded D-56 without verifying the schema.

**Resolution chosen for Phase 1**: Carry the discriminator in the existing `sprk_searchprofile NVARCHAR(100)` field as `"insights-observation@v1"`. Map Observation envelope into existing fields. Use `sprk_sessionid` as the idempotency dedup key.

---

## Actual `sprk_analysis` schema (from Dataverse MCP)

```
DESCRIBE TABLE sprk_analysis (
  -- System fields (createdby, modifiedby, ownerid, etc. omitted)
  sprk_analysisid GUID,                                            -- primary key
  sprk_actionid LOOKUP → sprk_analysisaction NOT NULL,             -- which action produced this analysis
  sprk_documentid LOOKUP → sprk_document NOT NULL,                 -- the source document
  sprk_name NVARCHAR(200) NOT NULL,                                -- primary display name
  sprk_analysisstatus CHOICE (Draft, In Progress, Completed, ...), -- workflow status
  sprk_chathistory MULTILINE TEXT,                                 -- chat-style interaction log
  sprk_completedon DATETIME,
  sprk_containerid NVARCHAR(100),                                  -- SPE container ref
  sprk_errormessage MULTILINE TEXT,
  sprk_finaloutput MULTILINE RICH TEXT,                            -- AI output (rich text)
  sprk_inputtokens INT,
  sprk_outputfileid LOOKUP → sprk_document,                        -- optional output artifact
  sprk_outputtokens INT,
  sprk_playbook LOOKUP → sprk_analysisplaybook,
  sprk_searchprofile NVARCHAR(100),                                -- ★ chosen as artifactType discriminator
  sprk_sessionid NVARCHAR(50),                                     -- ★ chosen as idempotency key
  sprk_startedon DATETIME,
  sprk_workingdocument MULTILINE TEXT,                             -- ★ chosen for verbatim quote
  statecode STATE,
  statuscode STATUS,
  ...
);
```

### Key findings

1. **No source-type picklist field** — no `sprk_sourcetype`, no `sprk_artifacttype`, no choice column that could serve as a typed discriminator.
2. **`sprk_actionid` is NOT NULL** — every row must point to an `sprk_analysisaction` row. Today's actions are real AI actions (`Contract Review`, `Lease Agreement Review`, `Extract Data`, etc. — see ACT-001 through ACT-025). None are "Insights Observation".
3. **`sprk_documentid` is NOT NULL** — every row must point to a `sprk_document` row. For Phase 1 Observations that always carry document evidence, this is satisfied by resolving `EvidenceRef.Ref` (e.g., `spe://drive/abc/item/xyz`) to a `sprk_document` GUID via `sprk_driveitemid` lookup.
4. **Text fields available**: `sprk_name` (200), `sprk_finaloutput` (rich text — Observation JSON envelope), `sprk_workingdocument` (multiline — verbatim quote), `sprk_chathistory` (multiline — producedBy/scope/asOf JSON), `sprk_searchprofile` (100 — discriminator), `sprk_sessionid` (50 — dedup key).

---

## Phase 1 mapping decisions

| `ObservationArtifact` field | `sprk_analysis` field | Notes |
|---|---|---|
| (computed display) | `sprk_name` | `"{predicate}: {value-summary} ({confidence:F2})"` truncated to 200 chars |
| `Predicate`, `Value`, `Confidence`, `Evidence`, `ProducedBy`, `Scope`, `TenantId`, `AsOf`, `Embedding`, `ValidFrom/To` | `sprk_finaloutput` | JSON-serialized full Observation envelope (rich text — Dataverse stores raw text safely) |
| `Evidence[0].Quote` | `sprk_workingdocument` | Verbatim quote from primary evidence ref (Layer 2 outcome extraction) |
| `ProducedBy`, `Scope`, `AsOf` (small JSON) | `sprk_chathistory` | Producer + scope + timestamp JSON for filterable views |
| `Id` (idempotency key, hashed to ≤50 chars) | `sprk_sessionid` | SHA-256 hex of `Id`, take first 50 chars to fit; pre-insert query by this field |
| (artifactType discriminator) | `sprk_searchprofile` | Fixed value `"insights-observation@v1"` — versioned because future Phase 1.5+ may emit `"insights-inference@v1"`, `"insights-precedent-mirror@v1"`, etc. |
| `Evidence[0].Ref` → resolve | `sprk_documentid` | Lookup `sprk_document` by `sprk_driveitemid` extracted from `spe://drive/{driveId}/item/{itemId}` URI |
| (configured action GUID) | `sprk_actionid` | Configured via `InsightsMirrorOptions.InsightsObservationActionId` — points to a new `sprk_analysisaction` row created as deployment prerequisite |
| Hardcoded `Completed` (2) | `sprk_analysisstatus` | Mirror rows always complete (read-only review records) |
| `AsOf` | `sprk_completedon`, `sprk_startedon` | Same timestamp (mirror is instantaneous) |

### Idempotency mechanism

`ObservationArtifact.Id` is the unit of idempotency (per `IObservationMirror` contract). Since `sprk_sessionid` is only 50 chars, we hash via SHA-256 hex and truncate to 50 chars (still has ~200 bits of collision resistance). Pre-insert: query `sprk_analysis` for any row with `sprk_sessionid = computed-hash`; if found, skip (no-op).

---

## Deployment prerequisites (BLOCKING for production use)

These must be created in target Dataverse environments **before** `DataverseObservationMirror` is enabled (i.e., before `InsightsMirrorOptions.EnableMirror = true` in that environment):

1. **New `sprk_analysisaction` row** representing the mirror semantic:
   - `sprk_actioncode`: `INS-OBS` (suggested)
   - `sprk_name`: `Insights Observation Mirror`
   - `sprk_systemprompt`: `(not used — mirror writes carry the Observation envelope directly, no LLM execution)`
   - `sprk_description`: `Read-only Dataverse projection of an emitted Insights Observation. Created as a side-effect of the universal ingest playbook (D-P7). Powers the Phase 1 D-P11 model-driven review surface. Do not edit.`
   - `sprk_tags`: `insights, observation, mirror, read-only, system-generated`

2. **Capture the new row's GUID** and set:
   - `Insights:Mirror:InsightsObservationActionId` in appsettings/Key Vault
   - Default appsettings.json carries `00000000-0000-0000-0000-000000000000` (Empty Guid) which causes `DataverseObservationMirror` to fall back to NoOp behavior with a warning log — this is the intended Phase 1 dev-safety default.

3. **(Optional but recommended)** Create a Dataverse model-driven view filtered to `sprk_searchprofile = "insights-observation@v1"` — this is task 052 (D-P11 review surface).

---

## Open question deferred to task 052

The model-driven view (task 052) will need to render the JSON-serialized envelope in `sprk_finaloutput` in a reviewer-friendly way. Options for task 052:
- (a) Rely on rich-text display + reviewer parses the JSON mentally
- (b) Add JavaScript form web resource that pretty-prints the JSON
- (c) Add denormalized scalar columns (`sprk_observation_subject`, `sprk_observation_confidence_decimal`, etc.) for view-column display

Recommend (c) for Phase 1.5+; Phase 1 ships (a) since the QA cohort is small and the JSON is human-readable.

---

## Note on `IObservationMirror` placement (architectural decision for task 051)

The interface currently lives in `Services/Ai/Insights/Mirror/` (Zone A). Per project CLAUDE.md §3.5.4, the forbidden-imports grep is `Services\.Ai\.Insights[^.P]` — meaning Zone B can import `Services.Ai.Insights.PublicContracts` (the `[^.P]` exception) but NOT `Services.Ai.Insights.Mirror`.

Since `DataverseObservationMirror` (the task 051 impl) lives in Zone B (`Services/Insights/Observations/`), it cannot import the interface from `Services/Ai/Insights/Mirror/`. Two resolutions:

**Chosen: (a) Move `IObservationMirror` to `Services/Ai/PublicContracts/IObservationMirror.cs`**.
- Consistent with `IInsightsAi` (already in PublicContracts as the canonical cross-zone facade)
- Minimal refactor (namespace move + import updates in IngestOrchestrator + NoOpObservationMirror + InsightsIngestModule)
- Avoids carving a Mirror-specific exception into the §3.5.4 grep — keeps the rule a single, simple pattern

Rejected: (b) Loosen the §3.5.4 grep to allow `Services.Ai.Insights.Mirror`.
- Would set a precedent for per-feature exceptions to the boundary rule
- The forbidden-imports list is a tight rule for a reason — every exception erodes it

Documented in task 051 commit message + the InsightsIngestModule XML doc.
