# Task 002 — FR-18: Enrich single-thread read DTO (Direction + sender identity + thread Name)

> **Status**: Implementation complete (main session finalizes TASK-INDEX + Step 9.5 gates)
> **Rigor**: FULL · **Scope**: projection-only over the existing impersonated read path · **Schema change**: none

---

## What changed

Enriched the existing `ThreadMessageDto` and populated `ThreadReadResult.Name` on the single-thread read — all
by adding **projected columns only** to the SAME impersonated query (`IImpersonatedCommunicationQuery`) + shared
`ICommunicationAccessFilter`. No second query, no directory lookup, no membership-union, no new component/package/worker.

### Files
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs` — added 3 DTO fields + doc.
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` — added column constants,
  extended the `select` lists (`ReadThreadAsync` + `QueryVisibleMessagesAsync`), extended `ParsedMessage`/`ParseMessageRow`
  + `BuildDto`, added a single bounded impersonated `ReadThreadNameAsync` projection, folded the `ReadThreadAsync`
  inline DTO construction onto the shared `BuildDto`.
- Tests: `CommunicationThreadReadServiceTests.cs`, `CommunicationByRegardingReadTests.cs`, `CommunicationFilteredQueryTests.cs`.

---

## DTO contract for the UI phases (FR-02/18)

### `ThreadMessageDto` — new fields (added after `From`)

| Field | Type | Source column | Notes |
|---|---|---|---|
| `Direction` | `int?` | `sprk_direction` (choice) | **Incoming = 100000000**, **Outgoing = 100000001**; `null` when unset |
| `SentBy` | `Guid?` | `_sprk_sentby_value` (systemuser lookup) | sender's Dataverse `systemuserid`; drives mine-right/others-left; `null` when unset |
| `SentByName` | `string?` | `sprk_sentbyname` (text, denormalized) | sender display name; `null` when unset |

Full post-enrichment record shape (order):
`MessageId, Body, BodyFormat, CommunicationType, From, Direction, SentBy, SentByName, SentAt, CreatedOn, InReplyTo, Privilege, Attachments`

Wire is serialized by property name — **use field names, not positions**.

### `ThreadReadResult.Name`
- `string?` from the thread's `sprk_name`. Now **populated on `ReadThreadAsync`** (previously always `null`).
- Read via a single **impersonated** projection on `sprk_communicationthread` by id — a caller who cannot see the
  thread record gets `null` (fail closed, no existence leak). By-regarding read already populated `Name` (unchanged).

### Uniform across all three read paths
`ReadThreadAsync`, `ReadByRegardingAsync`, and `QueryCommunicationsAsync` all project the enriched fields (the latter
two share `QueryVisibleMessagesAsync`/`BuildDto`). No client-contract/endpoint change (ADR-028).

### No-over-disclosure invariant (NFR-01)
The enriched fields are built by `BuildDto` from the **visible** (impersonated + access-filtered) set only — the same
list `Body`/`From` come from. A row the caller may not see contributes NONE of `Direction`/`SentBy`/`SentByName`.
Covered by `ReadThreadAsync_RowExcludedByAccessLayer_ContributesNoSenderIdentityToOutput` + the per-row isolation test.

---

## Escalation
**None.** The impersonated row already carries `sprk_sentbyname` (display name) and `_sprk_sentby_value` (systemuserid) —
exactly what the bubble layer needs for alignment + label. No resolved display name/avatar requiring a second lookup;
the escalation trigger did not fire. (Avatar, if later needed, is resolvable client-side from the `SentBy` systemuserid.)

---

## Placement Justification (cite `.claude/constraints/bff-extensions.md`)
- **Existing**: enriches the existing `ThreadMessageDto`/`ThreadReadResult` on the existing `CommunicationThreadReadService`
  read path — no overlap with any other component.
- **Extension**: this IS an extension of existing models + the existing impersonated query; no new service/interface/
  endpoint/DI registration/package/Dataverse column introduced (reuses `sprk_direction`, `sprk_sentby`, `sprk_sentbyname`).
- **Cost-of-doing-nothing**: without the projected fields the R3 Teams-style bubble UI cannot derive mine-right/others-left
  alignment from sender identity (only free-text `From`) nor render the thread label inline — FR-02/FR-18 fail.
- No `<justification>` element required (per POML `<notes>`): this extends existing types, adds no new model type.

### Query-count note (NFR-07)
`ReadThreadAsync` now issues at most **three** bounded O(1) impersonated queries (message page + bulk attachments +
one thread-name projection) — still no per-row fan-out; the no-fan-out invariant NFR-07 protects is preserved. Class
doc comment updated accordingly.

---

## Verification
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors** (19 pre-existing warnings).
- **Tests**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Services.Communication"` →
  **547 passed, 0 failed, 5 pre-existing skips**. Includes updated 001 baseline flips (Name populated; DTO shape now
  carries Direction/SentBy/SentByName), the new negative over-disclosure test, per-row isolation, and by-regarding +
  filtered-query uniform-contract tests.
- **Publish size**: **47.08 MB compressed** (`Compress-Archive -CompressionLevel Optimal`), ceiling ≤60 MB. Delta vs
  ~46 MB baseline ≈ +1 MB is measurement-method variance (this change is projection-only: 3 string/int/guid columns +
  one query method — negligible binary impact). Under the +5 MB single-task escalation threshold.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → **0 NEW HIGH**. Only the pre-existing HIGH on
  `System.Security.Cryptography.Xml 8.0.3` (not introduced by this task).
- **`git diff --name-only`**: 2 source files + 3 test files (this notes file untracked-until-committed).
