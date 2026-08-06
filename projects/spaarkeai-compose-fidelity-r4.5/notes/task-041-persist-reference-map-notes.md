# Task 041 — WS-4: persist the paraId → legal-number map into the R4 session ledger (FR-17)

> Written by the task 041 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are
> owned by the main session and NOT touched here.

## Summary

Completes FR-17's "BOTH stores" owner clarification. Task 040 already put the per-paragraph
reference set (`computedNumber`/`numberingLevel`/`listPath`/`headingLevel`) on
`ComposeDocxProjection.ParaIdMap[]` — the projection-payload half. This task adds the
session-ledger half: `ComposeService.LoadAsync` now persists the SAME `paraId → {...}` map onto
`ChatSession.ReferenceMap`, using R4's EXISTING three-tier session stack (ADR-040) — no new store.

**Escalation check: did NOT fire.** Step 1 (re-grep) confirmed the existing session already
carries two MUTABLE, wholesale-replaced Compose-domain collections in exactly this shape
(`AnchoredAnnotations`, `DefinedTermsTracking`, plus `ActiveDocument`) riding the same Redis
hot / Cosmos warm tiers (Dataverse cold tier intentionally excluded — same convention). The
reference map is a third instance of the identical pattern; no new store was ever needed.

## Ledger store reused

`ChatSession` (Redis hot, `ChatSessionManager`/`ITenantCache`) + `StoredSession` (Cosmos warm,
`ISessionPersistenceService`) — the SAME 3-tier stack `AnchoredAnnotations`/`DefinedTermsTracking`/
`ActiveDocument` already ride. Dataverse cold tier is NOT touched (matches those siblings — cold
tier carries only session metadata/audit, never these mutable UI-adjacent collections).

## Design

- **`ChatSession.ReferenceMap`** (`src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs`) —
  new `IReadOnlyList<ParaReferenceMapEntry>?` property, MUTABLE (wholesale-replaced, not
  append-only ADR-040 ledger entries), governed identically to `AnchoredAnnotations`/
  `ActiveDocument` (ADR-015 Tier 3, tenant + document scoped, null = "none yet").
- **`ParaReferenceMapEntry`** — new sibling record in `Models/Ai/Chat` (NOT `Services/Compose`):
  `(ParaId, ComputedNumber, NumberingLevel, ListPath, HeadingLevel)`. Defined in `Models/Ai/Chat`
  rather than reusing `ParaIdMapEntry` (`Services/Compose/ParaIdPreParser.cs`) directly because
  `Services/Compose` already depends on `Models/Ai/Chat` (via `ChatSession`); the reverse
  dependency does not exist and was not introduced.
- **`StoredSession.ReferenceMap`** — new `List<ParaReferenceMapEntry>` field (Cosmos warm-tier
  mirror), same "store the domain record directly, no parallel Stored* shape" reuse rationale as
  `AnchoredAnnotations`/`ActiveDocument`.
- **`ChatSessionManager`** — `MapChatSessionToStoredSession`/`MapStoredSessionToChatSession` now
  carry `ReferenceMap` through the warm tier (null ⇄ empty-list convention, matching siblings).
- **`ComposeService.LoadAsync`** — after the session is resumed/created (existing FR-29/FR-33
  binding logic, unchanged), builds the reference map from the SAME `paraIdMap` the response
  already returns (`BuildReferenceMap` — pure 1:1 field carry, no recomputation) and persists it
  via the EXISTING `_sessions.UpdateSessionCacheAsync(...)` call (the same method
  `SaveComposeAnnotationsAsync` already uses). Reassigned on EVERY load from the freshest
  `Build()` output: unchanged paragraphs keep the SAME entry because R4's
  `ComposeBaselineParaIdStamper`/`AnnotationReanchorService` keep a paragraph's physical
  `w14:paraId` stable across edits; new/split paragraphs simply appear as new map entries.

No numbering logic changed; `BuildReferenceMap` is a pure projection over task 040's already-
computed `ParaIdMapEntry` fields.

## Tests added

`tests/integration/seam/Compose/ComposeReferenceMapSessionLedgerSeamTests.cs` (KEEP path, ADR-038;
reuses the existing `ComposeFidelitySeamFixture` — real `ComposeService`/`ChatSessionManager`
wiring, SPE/Dataverse/indexing module-boundary mocks only, per root CLAUDE.md §11):

- `Load_PersistsReferenceMapOntoSessionLedger_ResolvableWithoutRecomputeDivergence` — loads
  `heading-style-numbering.docx` through the real `GET /api/compose/documents/{id}` route, then
  reads `ChatSession.ReferenceMap` via a FRESH `ChatSessionManager.GetSessionAsync` call (not a
  second projection rebuild) and asserts every entry matches the Load response's own `ParaIdMap`
  byte-for-byte (computedNumber/numberingLevel/listPath/headingLevel) — the "resolves without a
  recompute divergence" acceptance criterion.
- `Load_OnEditedReloadOfSameSession_KeepsStableNumbersForUnchangedParagraphsAndPersistsNewEntryForInserted`
  — loads the same corpus doc, appends ONE new plain paragraph to the Load-time bytes (via OpenXml
  SDK — every original paragraph's physical `w14:paraId` untouched), reloads under the SAME
  `sessionId` (resumed), and asserts: (a) every unchanged `paraId` keeps its exact persisted
  `computedNumber`/`numberingLevel`/`listPath`/`headingLevel` across the edit + reload round trip,
  and (b) the newly-minted `paraId` for the inserted paragraph appears as a NEW persisted ledger
  entry (never a silent drop).

No `Mock<HttpMessageHandler>`/DI-registration/ctor-null test added (ADR-038 bans).
`ChatSessionManager` is NOT mocked — the fixture's real in-memory-fallback registration
(`Redis:Enabled=false`) is used, matching production's own read/write contract exactly.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` (Debug) — **0 errors** (23 pre-existing warnings,
  unchanged set). `git diff --stat -- '*.csproj'` — empty (no `.csproj` change).
- `dotnet test --filter "FullyQualifiedName~Compose"` — **707 passed / 0 skipped / 0 failed**
  (040's baseline was 705; +2 = this task's two new seam tests).
- `dotnet test --filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"`
  — **32 passed**, unchanged — this task did not touch numbering computation.
- `Spaarke.ArchTests` `ADR013_ComposeFacadeTests` (Tier-1 purity guard) — **2/2 passed**.
  `ADR007_GraphIsolationTests` has the SAME pre-existing, unrelated failure task 040 documented (5
  types in `Services.Communication`/`Infrastructure.Errors`/`Api.Office.Errors` — none in
  `Services.Compose`, none touched by this task). Not in scope; flagged for visibility only.
- Publish size (BFF Hygiene §10): compressed **47.52 MB** (`Compress-Archive`, same method as
  030/031/032/040) vs 040's post-task **47.52 MB** → **delta +0.00 MB**. No new package; pure
  additive record fields + one persistence call on the existing `ChatSessionManager` API. Well
  under the ≤60 MB ceiling and the ~49.63 MB baseline.

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: `ChatSession` already carries two MUTABLE, wholesale-replaced session-scoped
  Compose-domain collections riding the SAME Redis-hot/Cosmos-warm stack
  (`AnchoredAnnotations`/`DefinedTermsTracking`, task 060/FR-29) plus a third
  (`ActiveDocument`, task 113) — grep confirmed no `ReferenceMap`/paraId-number persistence
  existed anywhere on the session before this task.
- **Extension**: Yes — a new field on the EXISTING `ChatSession` record, persisted through the
  EXISTING `ChatSessionManager.UpdateSessionCacheAsync` write-through (Redis + fire-and-forget
  Cosmos) and restored through the EXISTING `GetSessionAsync` read path. No new cache resource
  key, no new Cosmos container, no new Dataverse table, no new endpoint.
- **Cost-of-doing-nothing**: without a session-ledger copy, a consumer that reads session state
  directly (task 042's citation resolver, or any future capability resolving `paraId → number`
  outside a full Load/projection-rebuild call) would have no server-side source for the map except
  re-running `ComposeDocxProjectionBuilder.Build()` against the current bytes — duplicating work
  the projection already did, and diverging from FR-17's explicit "BOTH stores" requirement
  (owner clarification, project CLAUDE.md "WS-4 store").
- `Services/Compose/` stays pure `byte[]`-in/projection-out (ADR-007/013): `BuildReferenceMap` is
  a private static 1:1 field carry from `ParaIdMapEntry` (already in scope) to
  `ParaReferenceMapEntry` (a `Models.Ai.Chat` type `Services/Compose` already depends on via
  `ChatSession`) — no `Microsoft.Graph`, no AI-internal type, no new computation.
  `ADR013_ComposeFacadeTests` (Tier-1 NetArchTest) verified green (2/2).
- **`/conflict-check`** must be run by the MAIN SESSION before the PR (subagent does not
  commit/PR): `Services/Compose/` + `Models/Ai/Chat/ChatSession.cs` +
  `Services/Ai/Chat/ChatSessionManager.cs` overlap `spaarkeai-compose-r1/r2/r3/r4` and
  `spaarke-ai-architecture-redesign-r2` (owner of `Services/Ai/` broadly). This task's
  cross-cutting-visible surface is the additive `ChatSession.ReferenceMap` property (default
  null, non-breaking for every existing construction site) and the additive
  `StoredSession.ReferenceMap` field (default empty list).

## Files changed

- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs` — new `ReferenceMap` property on
  `ChatSession` (mutable, wholesale-replace) + new `ParaReferenceMapEntry` record.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/StoredSession.cs` — new `ReferenceMap` list
  field (Cosmos warm-tier mirror), additive/default-empty.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` —
  `MapChatSessionToStoredSession`/`MapStoredSessionToChatSession` carry `ReferenceMap` through
  the warm tier (null ⇄ empty-list convention).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` — `LoadAsync` builds +
  persists the reference map via the existing session-cache write-through; new private static
  `BuildReferenceMap` helper (pure `ParaIdMapEntry` → `ParaReferenceMapEntry` carry).
- `tests/integration/seam/Compose/ComposeReferenceMapSessionLedgerSeamTests.cs` — new seam file,
  2 `[Fact]`s (KEEP path, no banned test shapes).

## Note for 042 (citation resolver)

`ChatSession.ReferenceMap` now carries the SAME `paraId → {computedNumber, numberingLevel,
listPath, headingLevel}` data the projection payload (`ComposeDocxProjection.ParaIdMap`, task
040) carries — read from either depending on whether a full projection is already in hand (use
the payload) or only the session is (use `ReferenceMap` via `ChatSessionManager.GetSessionAsync`,
no projection rebuild needed). Both are populated from the identical `BuildReferenceMap`/task-040
data in the SAME `LoadAsync` call — never independently computed, so they cannot diverge.
