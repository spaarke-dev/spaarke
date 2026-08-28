# U-CB-2 — AI Search Index Vector Dimension Change (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that an upcoming Spaarke upgrade will change the vector dimension of one or more AI Search indexes and requires a full re-index of your document corpus.
> **Applies when**: A Spaarke release retires an embedding model or migrates to a new one with a different vector dimensionality (e.g. `text-embedding-3-large` 3072 → `text-embedding-3-small` 768, or 1536 → 3072). The affected AI Search index(es) are among the 7 canonical indexes per `scripts/ai-search/Deploy-AllIndexes.ps1`.
> **Owner**: Spaarke Platform Operations (release manager) + Spaarke AI Platform (embedding-model owner).
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel. No HTML, no branded styling. Operator adapts wording per channel norms.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-2

---

## 1. Summary

Spaarke is preparing an upgrade for your environment (`{customerName}` / `{environmentName}`) that **changes the vector dimensionality** of the following AI Search index/indexes:

- Index: `{indexName}` — Current: `{currentDimension}` dims (`{currentEmbeddingModel}`) → New: `{newDimension}` dims (`{newEmbeddingModel}`)
- (repeat per affected index)

**This is a breaking change (U-CB-2)**. AI Search does NOT permit vector dimension change on an existing index; the affected index will be **dropped and recreated**, then your document corpus will be **re-embedded and re-indexed from source**. Search results are unavailable for the affected indexes during the re-index window.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ALL of the following are true for release `{targetBffVersion}`:

- The release retires `{currentEmbeddingModel}` and migrates Spaarke to `{newEmbeddingModel}` (dim change from `{currentDimension}` to `{newDimension}`).
- Your environment's index inventory includes at least one of the affected indexes.
- The re-index cannot be applied in-place: AI Search requires the index to be recreated at the new dimensionality.

## 3. Customer impact

- **Search availability**: Queries against `{indexName}` will return **no results** (or degraded results, if Spaarke uses a shadow-index cutover strategy) during the re-index window.
- **User-facing surfaces affected**: Any Spaarke feature that queries `{indexName}` — including `{listAffectedFeatures}` (e.g. document semantic search, playbook grounding, insights engine widgets).
- **Cost**: One-time embedding-generation cost re-billed against your Azure OpenAI quota. Estimated re-embedding cost: `{estimatedEmbeddingCostUSD}` (based on `{documentCount}` documents at `{avgTokensPerDoc}` tokens). Ongoing per-query cost is unchanged.
- **Data integrity**: Source documents in SharePoint Embedded / Dataverse are untouched. Only the AI Search index (which is derived data) is rebuilt.

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| This notice sent | `{noticeSentDate}` |
| Customer sign-off deadline | `{signoffDeadline}` |
| Re-index window START | `{windowStart}` |
| Estimated re-index elapsed time | `{estimatedElapsedHours}` hours (based on `{documentCount}` documents; range: `{minHours}`–`{maxHours}` hours) |
| Re-index window END (target) | `{windowEnd}` |
| Post-cutover verification report | `{verificationReportDate}` |

Re-index elapsed time is **volume-dependent**. Rule of thumb: `{throughputDocsPerHour}` documents/hour per embedding model. Windows longer than 24 hours will be split into overnight sessions to minimise business-hours impact.

## 5. Required customer action

Before Spaarke can proceed with the re-index:

1. **Confirm the re-index window** in §4. If the window conflicts with a business-critical period (e.g. quarter-end reporting, board meeting), reply with an alternate window Spaarke can schedule.
2. **Notify end users** that `{listAffectedFeatures}` will return no/degraded results during the window. Spaarke can supply a suggested end-user notice on request.
3. **Provide sign-off** (§6) authorising the drop/recreate of `{indexName}` and the associated embedding cost.

## 6. Confirmation of receipt (required)

Please reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with:

> "`{customerName}` acknowledges receipt of U-CB-2 notice for environment `{environmentName}` dated `{noticeSentDate}`. We authorise the vector-dimension change and re-index of `{indexName}` in the `{windowStart}` → `{windowEnd}` window and accept the estimated re-embedding cost of `{estimatedEmbeddingCostUSD}`. Named authoriser: `{customerAuthoriserName}` (`{customerAuthoriserRole}`)."

Spaarke records this reply in the ProvisioningRun record for the audit trail. **No reply = no apply.**

## 7. Rollback semantics

The re-index itself is deterministic and idempotent — a failed run is retried, not rolled back. Rollback scenarios:

1. **Cutover shadow-index rollback**: if Spaarke used a shadow-index strategy (new index built alongside old before cutover), rollback is a query-endpoint swap back to the old index. Available within `{shadowRollbackWindowHours}` hours of cutover. After that, the old index is deleted.
2. **Corpus divergence recovery**: if source documents changed during the re-index window (uploads / deletions between window START and cutover), Spaarke's post-cutover verification report enumerates the divergence and re-runs incremental re-embedding to close the gap. No customer action required beyond acknowledgement.
3. **BFF rollback**: if the release must be rolled back for reasons other than U-CB-2 itself, the previous BFF version knows how to consume BOTH the old and new embedding models (Spaarke maintains backward-compat for one release cycle per ADR-020). Vector dim is compatible per the version-compat matrix.

Full rollback procedure: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → Rollback section.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
