# Task 011 — Resolver Behavior Contract (R-7 baseline)

> **Purpose**: Lock the CURRENT `IncomingAssociationResolver` behavior BEFORE refactoring it into
> the Association Engine (R-7). Every item here MUST be preserved at the Dataverse-write level.
> This note is the reference for 012–014 (rung content) + 017 (direction-symmetry/per-rung tests).

## Current entry point (pre-011)

`IncomingAssociationResolver.ResolveAsync(Guid communicationId, string mailboxEmail, string graphMessageId, Microsoft.Graph.Models.Message graphMessage, CommunicationAccount? account, CancellationToken ct)`

- Called from `IncomingCommunicationProcessor` Step 4.5 (inbound only), non-fatal (NFR-06).
- `account` param is currently **UNUSED** (no mailbox-context rung; comment says "no default-matter fallback").

## Rung cascade (FIRST match wins → Resolved; none → Pending Review)

| Order | Rung | Input used | Dataverse writes on match |
|---|---|---|---|
| 1 | **Thread** (`TryResolveByThreadAsync`) | In-Reply-To header (fetched via a SEPARATE Graph call `GetInReplyToHeaderAsync` using mailbox+graphMessageId) → parent lookup by `GetCommunicationByInternetMessageIdAsync(inReplyTo)` ?? `GetCommunicationByGraphMessageIdAsync(inReplyTo)` | Copies from parent, IF present: `sprk_regardingmatter`, `sprk_regardingorganization`, `sprk_regardingperson` (exactly these 3) |
| 2 | **Sender** (`TryResolveBySenderAsync`) | `graphMessage.From`; domain (skip `CommonEmailProviders`) | `sprk_regardingperson`→contact (by email); `sprk_regardingorganization`→**sprk_organization** (by domain); `sprk_regardingaccount`→**account** (by domain). Org + account each to its OWN lookup (task 004 fix). Can set 1–3 fields. |
| 3 | **Subject pattern** (`TryResolveBySubjectPatternAsync`) | `graphMessage.Subject` regex: `MAT-\d+`, `Matter #\d+`, `SPRK-\d+`, `[MATTER:\d+]` → `QueryMatterByReferenceNumberAsync(n)` ?? `QueryMatterByReferenceNumberAsync("MAT-"+n)` | `sprk_regardingmatter`→sprk_matter |

- No match from any rung → `sprk_associationstatus = 100000001` (Pending Review), NO regarding fields, NO resolver fields.
- Any match → `sprk_associationstatus = 100000000` (Resolved) + ADR-024 resolver fields populated.

## ADR-024 write path (PRESERVE VERBATIM)

`ApplyAssociationAsync` → sets `sprk_associationstatus`; if Resolved, `PopulateResolverFieldsAsync`:
- Picks the **primary** regarding entity by `RegardingFieldPriority` order:
  `matter, project, invoice, servicerequest, workassignment, event, budget, analysis, organization, account, person`.
- Writes 4 denormalized fields: `sprk_regardingrecordid` (lowercase GUID), `sprk_regardingrecordname`
  (from `EntityReference.Name` or retrieve via `GetPrimaryNameField`), `sprk_regardingrecordurl`
  (`/main.aspx?pagetype=entityrecord&etn={etn}&id={id}`), `sprk_regardingrecordtype`
  (lookup to `sprk_recordtype_ref`, cached via `_recordTypeRefCache`).
- All writes via a single `_genericEntityService.UpdateAsync("sprk_communication", id, fields, ct)`.

## R-7 preservation contract (the assertions that MUST stay identical)

From `IncomingAssociationResolverTests` (7 tests — the baseline):
1. Thread match copies parent `sprk_regardingmatter` + `sprk_regardingorganization`, status Resolved.
2. Sender match → `sprk_regardingperson` = contact id, status Resolved.
3. Sender skips common providers (gmail.com → `QueryAccountByDomainAsync("gmail.com")` never called).
4. Subject pattern (4 regex variants) → `sprk_regardingmatter` = matter id, status Resolved.
5. No match → status Pending Review (100000001), no regarding fields.
6. Priority cascade: thread wins over sender (sender query never called when thread matches).
7. Sender domain match writes org→`sprk_organization` AND account→`account`, separate lookups, no cross-stuffing (task-004 regression).

## R-7 SCOPING NOTE (how preservation is measured post-refactor)

The refactor **changes the input representation** (`Microsoft.Graph.Message` → `NormalizedMessage`) —
that is the whole point of FR-09 (envelope-only engine). Therefore the pre-refactor tests, which call
the old Graph-typed signature, cannot remain byte-identical **in their arrange**. R-7 is preserved at the
**observable Dataverse-write level**: the migrated characterization tests build an equivalent
`NormalizedMessage` and assert the **identical** writes (fields, ids, status, cascade order, provider-skip,
no cross-stuffing) — every `Verify(UpdateAsync ...)` assertion is carried over verbatim. A behavior change
would show up as a changed assertion; there are none.

Secondary boundary change (behavior-preserving): the In-Reply-To header is now read from the envelope
(populated by the boundary normalizer from `message.InternetMessageHeaders`) instead of a second Graph
call inside the thread rung. Same value, one fewer Graph round-trip. The engine loses its
`IGraphClientFactory` dependency.

## 011 scope boundary (what 011 does NOT do)

- Does NOT add new rung/match logic (012 rungs 0–1, 013 rung 2, 014 rung 3 own that).
- Does NOT run the engine over OUTBOUND. Outbound association stays `CommunicationService.MapAssociationFields`
  (client-supplied). Running the engine on outbound needs direction-aware rungs + would overwrite
  client associations — deferred to the direction-symmetry work (012/013/015/017).
- Does NOT change `sprk_associationstatus` semantics (still binary Resolved/PendingReview). Confidence→status
  + auto-file ≥0.85 is task 015.
- Does NOT collapse the inbound inline RAG/analysis sequence into `EnrichAsync` (that broader consolidation
  is tracked but not required by FR-09 acceptance criteria; kept out to keep the R-7 diff clean).
