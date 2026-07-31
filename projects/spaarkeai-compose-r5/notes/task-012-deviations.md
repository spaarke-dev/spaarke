# Task 012 (G12 accept/reject revision, single-by-id) — Deviations & Findings

> Written per task 012 POML step 9. No scope change from the brief; decisions + findings recorded for
> downstream tasks (013 batch, 014 tables, 041 hardening) and the eventual G12 UAT.

## 1. `compose-ops-v2` schema bump — blast radius (7 test files updated)

Adding op types is backward-incompatible (a v1 engine can't deserialize the `acceptRevision`/`rejectRevision`
discriminators), so the schema version bumped `compose-ops-v1` → `compose-ops-v2` on BOTH ends
(`ComposeOperationSchema.Version` + client `COMPOSE_OPERATION_SCHEMA_VERSION`). Per the task-004 design §4.3 this
turns a shared-`spaarke-bff-dev` / shared-`sprk_spaarkeai` deploy-skew into a deterministic
`UnsupportedSchemaVersion` (400/409) instead of a confusing 500 — the desired fail-safe. **Deploy both ends
together** (task-042 concern). Seven test files hard-coded `compose-ops-v1` and were updated to v2 (server:
`ComposeOperationSchemaTests`, `ComposeFidelitySeamTests`, `ComposeImportedAnchorsSurviveSaveSeamTests`,
`ConcurrencySaveSeamTests`; client: `compose-operations.test.ts`, `useAiGenerateBookmark.test.tsx`). This is
required maintenance of the version pin, not scope creep.

## 2. `scope: All` (batch) intentionally deferred to task 013

The closed catalog carries the FULL op shape (`scope: Single | All`, `revisionId?`) so task 013 does NOT re-touch
the catalog. This task implements `Single` and refuses `All` with `StructuralOpNotYetImplemented` (a clean 422),
because the deterministic document-order interleave when reconciling one revision shifts a sibling's indices is a
task-013 design detail (task-004 §3.4). Refusing (never a silent partial apply) is the correct single-by-id
boundary.

## 3. The reader recovers ONLY run-level revisions → `RevisionNotFound` for anything else is complete, not a gap

`DocxAnnotationReader.ReadRevisions` recovers ONLY `InsertedRun` (`w:ins`) and `DeletedRun` (`w:del`) as
`ImportedRevision`s — it does NOT recover paragraph-mark revisions (`Inserted`/`Deleted` in `w:pPr/w:rPr`) or
`w:*Change` formatting revisions. So an `acceptRevision`/`rejectRevision` op's `revisionId` only EVER points at a
run-level `w:ins`/`w:del`. The engine handles exactly those (accept-ins unwrap keep run; accept-del remove run;
reject inverse). A `revisionId` that resolves to no run-level revision → `RevisionNotFound` (422) is therefore
correct and complete for what the client can address — not an unhandled case.

**Corollary for task 014 (tables):** the task-010/011 OpenXml gotcha (`w:*Change` nested prev-props deserialize as
`*Extended` types) does NOT apply to this task — accept/reject touch `w:ins`/`w:del` wrappers (`InsertedRun`/
`DeletedRun`), never a `*Change` element with a nested previous-properties child. Task 014's `w:tblPrChange`/
`w:trPrChange`/`w:tcPrChange` work still must verify that gotcha.

## 4. The imported-deletion "end-of-paragraph re-anchor" bug is in `resolveBlock`, NOT `applyDeletion`'s placement

The POML pointed at `applyImportedRevisions` :272 (`applyDeletion`). Empirically (reproduced across ~15 cases:
last/middle/nested/list/blockquote/heading/atom-trailing/hardbreak/empty paragraphs) the `applyDeletion`
`block.to - 1` end-of-paragraph placement is already CORRECT — it was not the defect. The real
boundary-spanning bug is upstream in `resolveBlock`: a deletion that spans a paragraph boundary (a whole
paragraph's content deleted across the para mark) carries **empty `anchorText`** — every run of that paragraph is
inside `w:del`, so the reader's settled-text anchor (`GetParagraphText`, direct-child `w:t` only) is `""`. With no
text to verify, `resolveBlock` used to trust the doc-order `paragraphHint` UNCONDITIONALLY; on a cross-Word-session
reload the fully-deleted paragraph is gone from the mammoth-flattened editor (paraId regenerated + unmatched), so
the hint index now lands on a DIFFERENT, already-identified paragraph and the struck text was appended at the END
of that unrelated paragraph (silent mis-anchor). **Fix:** for an empty-anchor revision whose `paraId` is stale,
refuse to trust a hint that is a *distinct identified* paragraph (its own present, different paraId) — return null
so it surfaces as a review placeholder rather than corrupting an unrelated paragraph (invariant I-7 "never guess").
The own-round-trip case (paraId still matches) resolves precisely via the primary path, unchanged.

## 5. Client interceptor: divert only a WHOLE-RANGE single revision; extend the save-log machinery; 3-level dedup

- **Divert scope (code-review finding 1):** a `ReplaceStep` delete is mapped to `rejectRevision`/`acceptRevision`
  ONLY when the entire deleted range is one uniform imported revision (`wholeRangeImportedRevision`). A range that
  MIXES an imported revision with plain text or a second revision falls through to normal classification — so no
  non-revision content is silently dropped. (A manual delete ACROSS a tracked change is the *other* ET-2 trigger,
  still guarded by the engine's pre-existing `TrackedChangeReconciliationUnsupported` — out of this task's
  accept/reject-then-save scope.) A `RemoveMarkStep` on an imported mark is inherently mark-scoped (the
  accept/reject-UI `unsetMark`), so no analogous guard is needed there.
- **Save-log machinery (task-011 deviation #5 pattern):** `classifyStep` emitting a new op is not enough — the
  `RebasedOperationLog` save path silently drops any op `buildAnchor`/`deriveOperation` don't recognize. Both were
  extended for `acceptRevision`/`rejectRevision` (block-anchored at the step position; returned as-captured with
  their durable paraId + native revisionId), mirroring the setBlockAttr/structural handling.
- **Dedup at 3 levels:** one imported revision = one native `w:id`, but its rendered span can split across adjacent
  text nodes → several steps in one accept transaction. Deduped in the plugin (`dedupeRevisionOps`), in
  `recordTransaction` (per-transaction), and in `serialize()` (cross-transaction, e.g. undo+redo re-accept) — the
  duplicate would otherwise `RevisionNotFound` on the second server apply.

## 6. Publish size + byte-diff (NFR-01 / NFR-03)

Release publish (excl PDBs, same Python zip @ compresslevel=9 A/B method as tasks 010/011): **45.18 MB** — delta
~0 vs task 011's 45.19 MB (pure C#/TS, zero new package, NFR-03 honored). Well under the 60 MB ceiling. Corpus
byte-diff **24/24** green (no regression). No NEW test failures beyond the documented baseline (5 Communication +
3 ArchTest — ADR-010 ×2 / ADR-007 — all pre-existing, none touching Compose; the ADR-013 Tier-1 NetArchTest on
`Services/Compose` purity PASSES). NFR-09: this HARDENS the accept/reject contract `analysis-hub-r1` depends on —
no regression to its reopen-restore / retirement parity (no open PR overlap; conflict-check clean).
