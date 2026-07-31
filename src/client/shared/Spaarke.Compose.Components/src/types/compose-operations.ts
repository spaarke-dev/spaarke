/**
 * Spaarke Compose — The shared, versioned OPERATION SCHEMA (FR-11)
 *
 * Project:  spaarkeai-compose-r4
 * Task:     003 — the spine contract both ends compile against
 *
 * This is the CLIENT half of the single operation contract that the client
 * (ProseMirror step → operation interceptor, task 020) and the server
 * (ComposeShadowPatchEngine, task 030) implement IDENTICALLY. It MIRRORS,
 * field-for-field, the server contract at
 *   src/server/api/Sprk.Bff.Api/Services/Compose/Operations/ComposeOperation.cs
 * A serialized {@link ComposeOperationLog} MUST round-trip client → server →
 * client without loss. Any change here is a breaking change to that file and
 * vice-versa.
 *
 * EXTENSION, NOT FORK (project §11): this module is a sibling of
 * `compose-contracts.ts` in the same `types/` folder; the operation types are
 * ALSO re-exported from `compose-contracts.ts` so the contracts module remains
 * the single client import surface (augment, do not duplicate). The barrel
 * `../index.ts` re-exports them for external consumers.
 *
 * SHAPE (Slate discriminated-union op set — notes/bridge-prior-art.md finding
 * #2): `{ type, anchor, properties }` with a `type` discriminator. We borrow the
 * SHAPE, not Slate's identity model — Slate's session-only `path` is replaced by
 * our DURABLE key `w14:paraId`.
 *
 * ANCHOR MODEL (design.md §0 D2 — REFINED 2026-07-22):
 *   `(paraId, runIndex, run-local-offset)`. `paraId` is the durable Word-native
 *   node id; `runIndex` + run-local `offset` is the fine anchor. NEVER run-*ids*
 *   (don't survive Word) and NEVER absolute editor positions (die across server
 *   round-trips). Run boundaries are re-derived on apply at patch time.
 *
 * STRUCTURAL-OP SECOND-PARAGRAPH REFERENCE (task-003 op-shape decision):
 *   split/merge/insert reference a second paragraph (`newParaId` / `targetParaId`).
 *   These live in the op-payload PROPERTIES slot (Slate's `properties`) and are
 *   themselves `w14:paraId`s — durable Word-native ids, NOT run-ids and NOT
 *   absolute positions — so they obey D2/I-3. See
 *   projects/spaarkeai-compose-r4/notes/task-003-operation-schema-decisions.md.
 *
 * Privacy: op `text` carries document content (Tier 3 per ADR-015) — carried in
 * the operation log to the Save/patch request only, never on an SSE frame /
 * PaneEventBus payload / log surface. paraId / offsets / marks are Tier 1.
 */

/**
 * The operation-schema contract version stamped on every {@link ComposeOperationLog}.
 * Mirrors the server constant `ComposeOperationSchema.Version`. Bump ONLY on a
 * backward-incompatible change to the op set / anchor / property shape; both ends
 * validate the version they receive against the one they compile against.
 */
export const COMPOSE_OPERATION_SCHEMA_VERSION = 'compose-ops-v2' as const;

/** The exact, CLOSED set of op-type discriminators (FR-11). Order is not significant. */
export const COMPOSE_OPERATION_TYPES = [
  'insertText',
  'deleteRange',
  'replaceRange',
  'setMark',
  'clearMark',
  'splitParagraph',
  'mergeParagraph',
  'insertParagraph',
  'deleteParagraph',
  'setBlockAttr',
  // R5 task 012 (G12 / FR-11) — imported-revision reconciliation (accept/reject by native w:id).
  'acceptRevision',
  'rejectRevision',
  // R5 task 014 (G4 / FR-04) — structural table edit (row/column add/remove, cell content, table props).
  'table',
] as const;

/** The op-type discriminator union (the `type` field of every {@link ComposeOperation}). */
export type ComposeOperationType = (typeof COMPOSE_OPERATION_TYPES)[number];

/**
 * A RUN-LOCAL point anchor within a paragraph: the `runIndex`-th run, at run-local
 * character `offset`. NEVER an absolute editor position. Both 0-based;
 * `offset === run text length` addresses the point just after the run's last char.
 */
export interface ComposeRunPoint {
  /** 0-based index of the run within its paragraph. */
  runIndex: number;
  /** Run-local character offset within that run (0-based). */
  offset: number;
  /**
   * ROBUST ANCHOR (task 055 — ADDITIVE, backward-compatible). The PARAGRAPH-RELATIVE character offset
   * (0-based) measured over the editor-visible run flatten (the `k` input to
   * `stepOperationInterceptor.runLocalPoint`). Mirrors the server `ComposeRunPoint.ParaOffset`. Every op the
   * interceptor emits now carries BOTH the legacy `(runIndex, offset)` AND this `paraOffset`; the server, when
   * `paraOffset` is present, resolves the real OOXML `(run, run-local-offset)` by walking the paragraph's runs
   * to `paraOffset` — stable across the TipTap↔OOXML run-merge boundary (TipTap merges same-format runs, so
   * `runIndex` can disagree with OOXML's fine-grained runs). Omitted ⇒ server falls back to `(runIndex, offset)`
   * exactly as before. Still a pure numeric offset — never a text/content match (I-7).
   */
  paraOffset?: number;
}

/**
 * A RUN-LOCAL range within a SINGLE paragraph, `start` (inclusive) → `end`
 * (exclusive). Cross-paragraph spans are NOT one range — they decompose into a
 * per-paragraph {@link DeleteRangeOperation} + a {@link MergeParagraphOperation}
 * so every range stays intra-paragraph (invariant I-4).
 */
export interface ComposeRunRange {
  /** Inclusive run-local start point. */
  start: ComposeRunPoint;
  /** Exclusive run-local end point. */
  end: ComposeRunPoint;
}

/**
 * The closed set of inline character-formatting marks (mirrors the server
 * `ComposeMarkType` enum + the existing `ComposeInlineRun` mark surface). PascalCase
 * literals match the server's `JsonStringEnumConverter` member-name serialization.
 */
export type ComposeMarkType = 'Bold' | 'Italic' | 'Underline' | 'Link';

/**
 * The closed set of paragraph-level (block) attributes a {@link SetBlockAttrOperation}
 * may set (mirrors the server `ComposeBlockAttr` enum). The op `value` is interpreted
 * per attribute:
 *  - `Alignment`   → a `ComposeAlignment` name (`Default`/`Left`/`Center`/`Right`/`Justify`)
 *  - `Style`       → a style name (`Normal`/`Heading1`..`Heading6`/`ListParagraph`)
 *  - `ListOrdered` → `"true"` (numbered) or `"false"` (bullet)
 *  - `ListLevel`   → a 0-based integer string (`"0"`..`"8"`)
 */
export type ComposeBlockAttr = 'Alignment' | 'Style' | 'ListOrdered' | 'ListLevel';

/** Where {@link InsertParagraphOperation} places the new paragraph relative to its reference. */
export type ComposeParagraphPosition = 'Before' | 'After';

/**
 * The scope of an {@link AcceptRevisionOperation} / {@link RejectRevisionOperation} (mirrors the server
 * `ComposeRevisionScope` enum): reconcile a `Single` imported revision (by its native OOXML `w:id`) or `All`
 * tracked revisions in the document (batch, document order). PascalCase literals match the server's
 * member-name serialization. R5 task 012 emits `Single`; `All` (batch) is task 013.
 */
export type ComposeRevisionScope = 'Single' | 'All';

/**
 * The closed set of STRUCTURAL table edits a {@link TableOperation} may express (mirrors the server
 * `ComposeTableOpKind` enum; G4 / FR-04, R5 task 014). PascalCase literals match the server member-name
 * serialization. Whole-table CREATE is intentionally NOT a kind — a brand-new table on a tracked baseline is a
 * whole-block author, out of this catalog's structural-edit scope (task-004 design §2).
 */
export type ComposeTableOpKind =
  | 'InsertRow'
  | 'DeleteRow'
  | 'InsertColumn'
  | 'DeleteColumn'
  | 'SetCellContent'
  | 'SetTableProps';

/**
 * The closed set of table-level properties a `SetTableProps` {@link TableOperation} may change (mirrors the
 * server `ComposeTableProp` enum). The op `value` is interpreted per member:
 *  - `Alignment` → `Left`/`Center`/`Right`
 *  - `Width`     → `"auto"` | `"pct:NN"` | `"dxa:NNNN"`
 *  - `Borders`   → `None`/`Single`/`Double`
 */
export type ComposeTableProp = 'Alignment' | 'Width' | 'Borders';

/** Fields common to every operation. Every op carries the durable `w14:paraId` coarse anchor. */
interface ComposeOperationBase {
  /** The durable `w14:paraId` (8-hex) of the paragraph this op is anchored to. Always present. */
  paraId: string;
}

/** `insertText` — insert `text` at run-local point `at`, optionally carrying inline `marks`. */
export interface InsertTextOperation extends ComposeOperationBase {
  type: 'insertText';
  /** Run-local insertion point. */
  at: ComposeRunPoint;
  /** The text to insert (Tier 3 — document content). */
  text: string;
  /** Inline marks to apply to the inserted text. Omit/empty = inherit run formatting. */
  marks?: ComposeMarkType[];
}

/** `deleteRange` — delete the intra-paragraph run-local `range`. */
export interface DeleteRangeOperation extends ComposeOperationBase {
  type: 'deleteRange';
  /** The run-local range to delete (intra-paragraph). */
  range: ComposeRunRange;
}

/** `replaceRange` — replace the intra-paragraph run-local `range` with `text` (optionally `marks`). */
export interface ReplaceRangeOperation extends ComposeOperationBase {
  type: 'replaceRange';
  /** The run-local range to replace (intra-paragraph). */
  range: ComposeRunRange;
  /** The replacement text — empty ⇒ pure delete (Tier 3 — document content). */
  text: string;
  /** Inline marks to apply to the replacement text. Omit/empty = inherit. */
  marks?: ComposeMarkType[];
}

/** `setMark` — apply the inline `mark` over the intra-paragraph run-local `range`. */
export interface SetMarkOperation extends ComposeOperationBase {
  type: 'setMark';
  /** The run-local range to mark. */
  range: ComposeRunRange;
  /** The mark to apply. */
  mark: ComposeMarkType;
  /**
   * G5 (FR-05, task 033) — the hyperlink target URL, REQUIRED when `mark === 'Link'` and ignored for
   * every other mark. Mirrors the server `SetMarkOperation.Href`. The engine wraps the anchored run range
   * in a `w:hyperlink` pointing at an external relationship with this target. Optional (backward-compatible
   * — a Bold/Italic/Underline setMark omits it).
   */
  href?: string;
}

/** `clearMark` — remove the inline `mark` over the intra-paragraph run-local `range`. */
export interface ClearMarkOperation extends ComposeOperationBase {
  type: 'clearMark';
  /** The run-local range to clear. */
  range: ComposeRunRange;
  /** The mark to remove. */
  mark: ComposeMarkType;
}

/**
 * `splitParagraph` — split `paraId` at run-local point `at`. Content before `at` stays
 * in `paraId`; content at/after moves into a NEW paragraph `newParaId` inserted
 * immediately after `paraId`. `newParaId` is a minted durable `w14:paraId`
 * (task-003 op-shape decision, D2-compliant — a property, not the anchor).
 */
export interface SplitParagraphOperation extends ComposeOperationBase {
  type: 'splitParagraph';
  /** The run-local split point. */
  at: ComposeRunPoint;
  /** The minted `w14:paraId` for the new (trailing) paragraph. */
  newParaId: string;
}

/**
 * `mergeParagraph` — append `paraId`'s content onto the end of `targetParaId` (its
 * predecessor) and remove `paraId`. `targetParaId` is the surviving paragraph — a
 * durable `w14:paraId` in the op properties slot (task-003 op-shape decision).
 */
export interface MergeParagraphOperation extends ComposeOperationBase {
  type: 'mergeParagraph';
  /** The surviving predecessor paragraph's `w14:paraId` that receives the content. */
  targetParaId: string;
}

/**
 * `insertParagraph` — insert a NEW empty paragraph `newParaId` at `position` relative
 * to reference paragraph `paraId`. Content is filled by subsequent `insertText` ops
 * targeting `newParaId`. Distinct from `splitParagraph` (which divides an existing
 * paragraph). `newParaId` is a minted durable `w14:paraId` (task-003 op-shape decision).
 */
export interface InsertParagraphOperation extends ComposeOperationBase {
  type: 'insertParagraph';
  /** The minted `w14:paraId` for the new paragraph. */
  newParaId: string;
  /** Whether the new paragraph goes before or after the reference paragraph. */
  position: ComposeParagraphPosition;
}

/** `deleteParagraph` — remove the entire paragraph `paraId` (runs + paragraph mark). */
export interface DeleteParagraphOperation extends ComposeOperationBase {
  type: 'deleteParagraph';
}

/**
 * `setBlockAttr` — set the paragraph-level `attr` to `value` on `paraId`. Paragraph-scoped,
 * anchored by paraId alone (no run offset). `value` is interpreted per {@link ComposeBlockAttr};
 * `null` clears the attribute back to its style default.
 */
export interface SetBlockAttrOperation extends ComposeOperationBase {
  type: 'setBlockAttr';
  /** Which block attribute to set. */
  attr: ComposeBlockAttr;
  /** The attribute value (interpreted per `attr`); `null` clears to style default. */
  value: string | null;
}

/**
 * `acceptRevision` — ACCEPT a pre-existing imported Word tracked change (native `w:ins`/`w:del`), reconciling
 * it into settled content so a subsequent Save no longer 422s with `TrackedChangeReconciliationUnsupported`
 * (ET-2 / FR-11). The revision is located by `revisionId` (its native `w:id` = `ImportedRevision.id`) within
 * `paraId` — NEVER by text/content match (I-7). `revisionId` is required for `Single`, null for `All`.
 */
export interface AcceptRevisionOperation extends ComposeOperationBase {
  type: 'acceptRevision';
  /** `Single` = one revision by `revisionId`; `All` = every tracked revision (task 013 batch). */
  scope: ComposeRevisionScope;
  /** The native OOXML `w:ins`/`w:del` `w:id` to accept (= `ImportedRevision.id`); `null` for `All`. */
  revisionId: string | null;
}

/**
 * `rejectRevision` — REJECT a pre-existing imported Word tracked change (native `w:ins`/`w:del`), the inverse
 * of {@link AcceptRevisionOperation}. Same shape (two discriminators, one per semantic action — the catalog's
 * `setMark`/`clearMark` convention); located by `revisionId` within `paraId`, never by text/content match (I-7).
 */
export interface RejectRevisionOperation extends ComposeOperationBase {
  type: 'rejectRevision';
  /** `Single` = one revision by `revisionId`; `All` = every tracked revision (task 013 batch). */
  scope: ComposeRevisionScope;
  /** The native OOXML `w:ins`/`w:del` `w:id` to reject (= `ImportedRevision.id`); `null` for `All`. */
  revisionId: string | null;
}

/**
 * `table` — a STRUCTURAL edit to a table on the imported/tracked path (mirrors the server `TableOperation`
 * record; G4 / FR-04, R5 task 014). Anchored by the base `paraId` = the `w14:paraId` of a paragraph INSIDE the
 * target table (canonical: first cell's first paragraph); the server resolves that paragraph O(1) then walks the
 * ancestry `w:p → w:tc → w:tr → w:tbl` — NEVER a text/content search (I-7 / NFR-02). `row`/`column` address the
 * cell within the resolved table (0-based).
 */
export interface TableOperation extends ComposeOperationBase {
  type: 'table';
  /** Which structural table edit to apply (closed set). */
  kind: ComposeTableOpKind;
  /** 0-based row index (InsertRow/DeleteRow/SetCellContent); `null` for column-only / table-prop ops. */
  row?: number | null;
  /** 0-based grid-column index (InsertColumn/DeleteColumn/SetCellContent); `null` for row-only / table-prop ops. */
  column?: number | null;
  /** For InsertRow/InsertColumn — insert the new row/column before/after `row`/`column`. */
  position?: ComposeParagraphPosition | null;
  /** Minted `w14:paraId`s for the NEW cells' paragraphs (InsertRow → per column L→R; InsertColumn → per row T→B). */
  newParaIds?: string[];
  /** For SetCellContent — the cell's new text content (Tier 3 — document content). */
  text?: string | null;
  /** For SetCellContent — optional inline marks on the set content. Omit/empty = inherit. */
  marks?: ComposeMarkType[];
  /** For SetTableProps — which table-level property to change. */
  tableProp?: ComposeTableProp | null;
  /** For SetTableProps — the property value, interpreted per `tableProp`; `null` clears to style default. */
  value?: string | null;
}

/**
 * The CLOSED discriminated union of exactly thirteen operations (FR-11). Narrow on `type`
 * before reading op-specific fields.
 */
export type ComposeOperation =
  | InsertTextOperation
  | DeleteRangeOperation
  | ReplaceRangeOperation
  | SetMarkOperation
  | ClearMarkOperation
  | SplitParagraphOperation
  | MergeParagraphOperation
  | InsertParagraphOperation
  | DeleteParagraphOperation
  | SetBlockAttrOperation
  | AcceptRevisionOperation
  | RejectRevisionOperation
  | TableOperation;

/**
 * The op-log ENVELOPE (FR-11): an ORDERED sequence of `operations` plus the
 * `schemaVersion` both ends validate against. Produced by the client capture path
 * (task 020), consumed by the server patch engine (task 030); round-trips
 * client → server → client without loss.
 */
export interface ComposeOperationLog {
  /** The operation-schema contract version (defaults to {@link COMPOSE_OPERATION_SCHEMA_VERSION}). */
  schemaVersion: string;
  /** The ordered operations to apply, in author (rebased) order. */
  operations: ComposeOperation[];
}

/**
 * A durable, `(paraId, run-local range)`-anchored comment (D2) — the CLIENT mirror of the server
 * `ComposeAnchoredComment` record (`Sprk.Bff.Api.Services.Compose.ComposeShadowPatchEngine`). Sent
 * in the Save request's `comments` field; `ComposeShadowPatchEngine.ApplyComment` bakes each one as a
 * native `w:comment` (ADR-049). Unlike the retired `DocxAnnotationInput` comment shape
 * (`useComposeWordShuttle.ts`, text-anchored via `targetText`), this anchors by
 * `(paraId, runIndex, run-local-offset)` — NO write-path text-search (I-7). Field-for-field mirror;
 * a change here is a breaking change to the server record and vice-versa.
 *
 * @see ai-advanced-capabilities-nda-r1 task 040 (comment-export wiring fix)
 * @see ../widgets/ComposeCommentThread.types.ts — `composeSessionCommentThreadsToAnchoredComments`,
 *      the mapper that resolves a comment thread's live `commentAnchor` mark span to this shape.
 */
export interface ComposeAnchoredComment {
  /** The `w14:paraId` of the paragraph the comment anchors to. */
  paraId: string;
  /** The run-local range the comment brackets (intra-paragraph). */
  range: ComposeRunRange;
  /** The comment body. */
  commentText: string;
  /** Comment author (surfaces as Word's attribution). */
  author: string;
  /** Optional author initials for the balloon; the server derives them from `author` when omitted. */
  initials?: string;
  /** ISO-8601 comment timestamp (serialized into the `w:date` attribute). */
  date: string;
}

/**
 * Runtime guard: `true` when `value` is a well-formed {@link ComposeOperation} — a known
 * closed-set `type` discriminator plus a non-empty `paraId`. This is the client half of
 * "both ends validate against the same schema": before applying an op log (esp. one built
 * from AI-returned JSON, design.md §5 AI path) the client validates each op with this guard,
 * mirroring the server's polymorphic-deserialization contract. Deep per-op field validation
 * (anchor presence per op) is layered by the capture/apply path (task 020/005).
 */
export function isComposeOperation(value: unknown): value is ComposeOperation {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const op = value as { type?: unknown; paraId?: unknown };
  return (
    typeof op.type === 'string' &&
    (COMPOSE_OPERATION_TYPES as readonly string[]).includes(op.type) &&
    typeof op.paraId === 'string' &&
    op.paraId.length > 0
  );
}

/**
 * Runtime guard: `true` when `value` is a well-formed {@link ComposeOperationLog} — a string
 * `schemaVersion` and an `operations` array whose every element passes {@link isComposeOperation}.
 */
export function isComposeOperationLog(value: unknown): value is ComposeOperationLog {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const log = value as { schemaVersion?: unknown; operations?: unknown };
  return (
    typeof log.schemaVersion === 'string' && Array.isArray(log.operations) && log.operations.every(isComposeOperation)
  );
}
