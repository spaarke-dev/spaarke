/**
 * useComposeWordShuttle.ts — client callers for the Word round-trip shuttle (task 103, Cluster 3).
 *
 * Project: spaarkeai-compose-r2, task 103 (E2E-R4 Word-shuttle wiring).
 *
 * The push-annotations (050), pull-annotations (051), and check-changes (053) BFF endpoints were
 * all built + unit-green but had NO client caller (gaps 3.1 / 3.4 / the poll half of 3.5). This
 * module supplies the three thin fetch hooks that CONNECT them — mirroring `useComposeReanchor`'s
 * `@spaarke/auth` `authenticatedFetch` pattern exactly (the reanchor caller, task 054). It does NOT
 * rewrite the endpoints (they are correct); it wires them.
 *
 * Constraints honored (BINDING):
 *   - ADR-028: every fetch goes through `@spaarke/auth` `authenticatedFetch` — never a raw `Bearer`
 *     header assembled here. `authenticatedFetch` injects the token + tenant headers.
 *   - ADR-015 Tier-3: annotation body / textPattern are user content — sent to the BFF (the design
 *     intent) but NEVER logged to console/telemetry here.
 *   - ADR-030: no PaneEventBus signal is emitted here (the `compose_reanchor_ready` discriminant is
 *     not on the bus union and MUST NOT be added — task 104 froze the compose discriminant set).
 *
 * @see ./useComposeReanchor.ts — the reference caller this mirrors (task 054)
 * @see src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs — PushAnnotations / PullAnnotations / CheckDocumentChangesAsync
 */

import * as React from 'react';
import { useAuth } from '@spaarke/auth';

import type { AnchoredAnnotation, ImportedComment, ImportedRevision } from '../types/compose-contracts';
import type { PriorAnchorInput } from './ComposeReanchor.types';

// ---------------------------------------------------------------------------
// Shared options (mirror UseComposeReanchorOptions)
// ---------------------------------------------------------------------------

export interface UseComposeWordShuttleOptions {
  /** BFF base URL (no trailing slash), e.g. `https://bff.example`. */
  bffBaseUrl: string;
  /**
   * Test/injection escape hatch — bypasses `useAuth().authenticatedFetch` so a test can drive the
   * hook without an MSAL bootstrap. Production hosts must NOT set this.
   */
  fetchOverride?: typeof fetch;
}

// ---------------------------------------------------------------------------
// Wire types (client mirrors of the BFF contracts)
// ---------------------------------------------------------------------------

/**
 * Native Word track-change kind. Mirror of the BFF `TrackChangeKind` enum
 * (`CriticMarkupRenderer.cs`). The BFF enum carries NO `JsonStringEnumConverter`, so it
 * deserializes from the NUMERIC value on the wire — these constants match the enum's declaration
 * order (Insertion=0, Deletion=1, Comment=2). Keep in sync with the server enum.
 */
export const DocxTrackChangeKind = {
  Insertion: 0,
  Deletion: 1,
  Comment: 2,
} as const;

/** Client mirror of the BFF `DocxAnnotation` push payload entry. */
export interface DocxAnnotationInput {
  /** Native markup kind (numeric — see {@link DocxTrackChangeKind}). */
  kind: number;
  /** Existing document text the annotation targets (the anchored / deleted span). */
  targetText?: string | null;
  /** Inserted text (Insertion only). */
  newText?: string | null;
  /** Comment body (Comment only). */
  commentText?: string | null;
  /** Revision/comment author (Word attribution). Required by the BFF. */
  author: string;
  /** ISO-8601 timestamp (serialized into the `w:date` attribute). */
  date: string;
}

/**
 * Item 3 (UAT round-4): decide which redline tracked-changes to bake into a save.
 *
 * A freshly-MOUNTED document carries passively-materialized AI suggestions as redline marks (the
 * ledger-replay path renders them on mount, before the user touches anything). Sending those on a
 * ZERO-EDIT save asks the server to locate each target in the raw OOXML — which can miss on
 * intra-paragraph whitespace/tab drift and 422 ("a tracked change could not be located in the
 * document to save"). The rule: only persist redline annotations once the user has ENGAGED the
 * document (`editorIsDirty`). A clean save then round-trips the retained original byte-identical.
 * Accepted suggestions / edits make the editor dirty and persist via the paraId-keyed
 * `editedParagraphs` delta (FR-02), so no acted-upon change is lost by this gate.
 */
export function selectSaveRedlineAnnotations(opts: {
  editorIsDirty: boolean;
  hasRedlines: boolean;
  getRedlineAnnotations: () => DocxAnnotationInput[];
}): DocxAnnotationInput[] {
  if (!opts.editorIsDirty || !opts.hasRedlines) return [];
  return opts.getRedlineAnnotations();
}

// R8 UAT item 8: these two were `{ [key: string]: unknown }` placeholders — the endpoints were wired
// (task 103) but the only consumer summed `.length`, so the payload's real shape was never described.
// The Document Revision Report reads the fields, so they are typed now, and typed BY REUSE rather than
// re-declaration: the server's `RecoveredRevision`/`RecoveredComment` are exactly the `ImportedRevision`/
// `ImportedComment` vocabulary MINUS `paraId`, which the Load projection adds and this endpoint does not.
// Forking the shape here is explicitly forbidden by the contract comment on those types.

/** A native comment recovered from the current SPE document (mirror of BFF `RecoveredComment`). */
export type RecoveredComment = Omit<ImportedComment, 'paraId'>;

/** A native revision recovered from the current SPE document (mirror of BFF `RecoveredRevision`). */
export type RecoveredRevision = Omit<ImportedRevision, 'paraId'>;

/** BFF response for `POST /api/compose/document/{id}/pull-annotations` (FR-25). */
export interface PullAnnotationsResult {
  documentSpeId: string;
  driveId?: string | null;
  comments: RecoveredComment[];
  revisions: RecoveredRevision[];
  correlationId: string;
}

/** BFF response for `POST /api/compose/document/{id}/check-changes` (FR-26). */
export interface CheckChangesResult {
  documentSpeId: string;
  containerId: string;
  changed: boolean;
  deleted: boolean;
  eTag?: string | null;
  name?: string | null;
  correlationId: string;
}

// ---------------------------------------------------------------------------
// Mappers — Compose session state → the BFF wire shapes
// ---------------------------------------------------------------------------

/**
 * Maps the Compose session's anchored annotations to the {@link PriorAnchorInput} the reanchor
 * endpoint scores (task 054). `textPattern`/`paragraphHint` come from the annotation's anchor;
 * `preview` is a short, Tier-1-safe label the conflict UI shows. R3 FR-11 (task 012): `paraId`
 * carries the paragraph's `w14:paraId` so the server can re-anchor by it FIRST, falling back to the
 * fuzzy textPattern scorer only when it is absent (design §5.2). Omitted (undefined → not serialized)
 * for a legacy anchor that predates the field.
 */
export function anchoredAnnotationsToPriorAnchors(annotations: readonly AnchoredAnnotation[]): PriorAnchorInput[] {
  return annotations
    .filter(a => a.anchor?.textPattern)
    .map(a => ({
      id: a.id,
      type: a.type,
      textPattern: a.anchor.textPattern,
      paragraphHint: a.anchor.paragraphHint ?? -1,
      preview: a.body ? a.body.slice(0, 160) : null,
      paraId: a.anchor.paraId,
    }));
}

/**
 * Maps the Compose session's anchored annotations to the {@link DocxAnnotationInput}s the
 * push-annotations endpoint renders as native Word track-changes + comments (FR-24). Only
 * annotations that carry the fields the server's `DocxAnnotation.Validate()` requires are emitted
 * (a comment/deletion needs a non-empty `targetText`; an insertion needs `newText`) — the rest are
 * dropped so the push never 400s on an incomplete annotation.
 */
export function anchoredAnnotationsToDocxAnnotations(
  annotations: readonly AnchoredAnnotation[]
): DocxAnnotationInput[] {
  const result: DocxAnnotationInput[] = [];
  for (const a of annotations) {
    const targetText = a.anchor?.textPattern ?? '';
    const author = a.author || 'Spaarke Compose';
    const date = a.timestamp || new Date().toISOString();

    switch (a.type) {
      case 'insertion-suggestion':
        if (a.body) {
          result.push({ kind: DocxTrackChangeKind.Insertion, targetText, newText: a.body, author, date });
        }
        break;
      case 'deletion-suggestion':
        if (targetText) {
          result.push({ kind: DocxTrackChangeKind.Deletion, targetText, author, date });
        }
        break;
      case 'comment':
      case 'explanation':
      default:
        if (targetText && a.body) {
          result.push({ kind: DocxTrackChangeKind.Comment, targetText, commentText: a.body, author, date });
        }
        break;
    }
  }
  return result;
}

// ---------------------------------------------------------------------------
// Redline → Word bridge (UAT-R7 #2/#3): PENDING redline marks → DocxAnnotationInput[]
// ---------------------------------------------------------------------------

/** Minimal TipTap doc/text node shape for redline extraction (decoupled from `@tiptap/core`). */
interface RedlineJsonNode {
  type?: string;
  text?: string;
  content?: RedlineJsonNode[];
  marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
}

/** Accumulator for one pending redline (keyed by `ledgerRef`) while walking the doc. */
interface RedlineAccumulator {
  ledgerRef: string;
  /** Concatenated text carrying the `deletion` mark — the ORIGINAL (target_text). */
  deletionText: string;
  /** Concatenated text carrying the `insertion` mark — the REPLACEMENT (new_text). */
  insertionText: string;
}

/**
 * Bridge (UAT-R7 #2) — map the editor's PENDING redline marks into {@link DocxAnnotationInput}s so
 * SAVE renders them as NATIVE Word `w:ins`/`w:del` via `DocxAnnotationWriter` (the proven server
 * writer), instead of the client `tipTapToDocxBytes` flattening them to plain text.
 *
 * This is the previously-ABSENT producer the redline marks (`usePendingRedline`) never had — the
 * sibling of {@link anchoredAnnotationsToDocxAnnotations} (which already maps the COMMENT half). It
 * reads the redline text straight from the marks in the editor's JSON (the `insertion` / `deletion`
 * marks carry the content + a `ledgerRef` in their attrs), grouping by `ledgerRef`:
 *  - deletion-marked text → the original `target_text`;
 *  - insertion-marked text → the replacement `new_text`.
 *
 * Emission order per replace pair is Insertion BEFORE Deletion: the writer's anchor lookup counts
 * only live `w:t` runs (a struck run becomes `w:delText`), so the insertion must be placed against
 * the still-live original before the deletion converts it — otherwise the insertion's anchor would
 * be gone (TargetNotFound). A pure insertion (no deletion half) becomes a tracked append; a pure
 * deletion becomes a `w:del`.
 *
 * @param docJson the editor's `getJSON()` output (a TipTap doc tree)
 * @param author  Word revision attribution for the emitted changes
 * @param timestamp ISO-8601 date stamped into the `w:date` attribute
 */
export function redlineMarksToDocxAnnotations(
  docJson: unknown,
  author: string,
  timestamp: string
): DocxAnnotationInput[] {
  const result: DocxAnnotationInput[] = [];
  // The paraId-bearing block node types (mirror the server's per-`<w:p>` annotation search).
  const BLOCK_TYPES = new Set(['paragraph', 'heading']);

  // Fix #1 (UAT 2026-07-20): accumulate insertion/deletion text per ledgerRef WITHIN A SINGLE BLOCK, and
  // flush that block's annotations before moving on. Previously the accumulation was global per ledgerRef,
  // so a redline spanning >1 editor paragraph (e.g. when the AI target didn't resolve and the mark fell
  // back to the user's raw multi-paragraph selection) emitted ONE `targetText` that concatenated two
  // `<w:p>` bodies. The server's `DocxAnnotationWriter.LocateTarget` searches ONE paragraph at a time, so
  // a cross-paragraph target is present in no single paragraph → TargetNotFound → 422. Splitting per block
  // makes every emitted `targetText` live inside a single paragraph (single-paragraph redlines are
  // unchanged — identical output). Block order = document order, preserving the apply sequence.
  const flushBlock = (byRef: Map<string, RedlineAccumulator>, order: string[]): void => {
    for (const ref of order) {
      const acc = byRef.get(ref)!;
      const targetText = acc.deletionText.length > 0 ? acc.deletionText : undefined;
      // Insertion FIRST (anchored to the still-live original), then Deletion — see JSDoc.
      if (acc.insertionText.length > 0) {
        result.push({
          kind: DocxTrackChangeKind.Insertion,
          targetText: targetText ?? null,
          newText: acc.insertionText,
          author,
          date: timestamp,
        });
      }
      if (acc.deletionText.length > 0) {
        result.push({
          kind: DocxTrackChangeKind.Deletion,
          targetText: acc.deletionText,
          author,
          date: timestamp,
        });
      }
    }
  };

  // Collect the redline text of a SINGLE block's inline content into block-scoped accumulators.
  const collectBlockInlines = (
    node: RedlineJsonNode | null | undefined,
    byRef: Map<string, RedlineAccumulator>,
    order: string[]
  ): void => {
    if (!node) return;
    if (node.type === 'text' && typeof node.text === 'string' && node.text.length > 0 && node.marks) {
      for (const mark of node.marks) {
        if (mark.type !== 'insertion' && mark.type !== 'deletion') continue;
        const ledgerRef = typeof mark.attrs?.ledgerRef === 'string' ? mark.attrs.ledgerRef : '';
        if (!ledgerRef) continue;
        // FR-24 (task 050): an IMPORTED existing Word revision (`imported:` prefix) already lives in the
        // retained-original baseline the dirty save deltas onto — it MUST NOT be re-emitted or the save
        // double-writes it. Kept in sync with IMPORTED_LEDGER_PREFIX in importedRevisions.ts.
        if (ledgerRef.startsWith('imported:')) continue;
        let acc = byRef.get(ledgerRef);
        if (!acc) {
          acc = { ledgerRef, deletionText: '', insertionText: '' };
          byRef.set(ledgerRef, acc);
          order.push(ledgerRef);
        }
        if (mark.type === 'insertion') acc.insertionText += node.text;
        else acc.deletionText += node.text;
      }
    }
    if (node.content) for (const child of node.content) collectBlockInlines(child, byRef, order);
  };

  // Walk the doc to each block (descending through doc/table/row/cell containers); each block gets its
  // OWN accumulators, flushed before the next block so no annotation ever spans a paragraph.
  const walkBlocks = (node: RedlineJsonNode | null | undefined): void => {
    if (!node) return;
    if (typeof node.type === 'string' && BLOCK_TYPES.has(node.type)) {
      const byRef = new Map<string, RedlineAccumulator>();
      const order: string[] = [];
      collectBlockInlines(node, byRef, order);
      flushBlock(byRef, order);
      return; // a block's inline content is fully handled — do not descend further into it
    }
    if (node.content) for (const child of node.content) walkBlocks(child);
  };
  walkBlocks(docJson as RedlineJsonNode);

  return result;
}

// ---------------------------------------------------------------------------
// Hook: pull-annotations (gap 3.4)
// ---------------------------------------------------------------------------

export interface PullAnnotationsArgs {
  documentSpeId: string;
  driveId: string;
  tenantId: string;
}

export interface UseComposePullAnnotationsResult {
  pulling: boolean;
  error: string | null;
  /** POSTs pull-annotations; resolves with the current native comments + revisions. */
  pull: (args: PullAnnotationsArgs) => Promise<PullAnnotationsResult>;
}

/**
 * FR-25 (gap 3.4) — pull-annotations client caller. Parses the CURRENT SPE bytes for native
 * `w:comment`/`w:ins`/`w:del` so the return-from-Word flow can surface what Word added.
 */
export function useComposePullAnnotations(options: UseComposeWordShuttleOptions): UseComposePullAnnotationsResult {
  const { bffBaseUrl, fetchOverride } = options;
  const { authenticatedFetch } = useAuth();
  const doFetch = fetchOverride ?? authenticatedFetch;

  const [pulling, setPulling] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const pull = React.useCallback(
    async (args: PullAnnotationsArgs): Promise<PullAnnotationsResult> => {
      setPulling(true);
      setError(null);
      try {
        const url = `${bffBaseUrl}/api/compose/document/${encodeURIComponent(args.documentSpeId)}/pull-annotations`;
        const response = await doFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ driveId: args.driveId, tenantId: args.tenantId }),
        });
        if (!response.ok) {
          const message = `Could not read the document's Word annotations (status ${response.status}).`;
          setError(message);
          throw new Error(message);
        }
        return (await response.json()) as PullAnnotationsResult;
      } finally {
        setPulling(false);
      }
    },
    [bffBaseUrl, doFetch]
  );

  return { pulling, error, pull };
}

// ---------------------------------------------------------------------------
// Hook: check-changes poll (poll half of gap 3.5)
// ---------------------------------------------------------------------------

export interface CheckChangesArgs {
  documentSpeId: string;
  /**
   * SPE container key. The Load path's SPE `driveId` is a valid key — the BFF's
   * `ResolveDriveIdAsync` returns a `b!` drive id unchanged and keys its Redis delta/etag state by
   * that value consistently with the subscription origin call (task 103, gap 3.2).
   */
  containerId: string;
}

export interface UseComposeCheckChangesResult {
  checking: boolean;
  error: string | null;
  /** POSTs check-changes (the poll fallback); resolves with whether the document changed. */
  checkChanges: (args: CheckChangesArgs) => Promise<CheckChangesResult>;
}

/**
 * FR-26 (poll half of gap 3.5) — check-changes client caller. The poll-on-focus fallback that does
 * NOT need the webhook secrets (owner task 056 / DEF-03); it drives the SAME Redis-backed
 * delta/etag substrate the webhook receiver uses.
 */
export function useComposeCheckChanges(options: UseComposeWordShuttleOptions): UseComposeCheckChangesResult {
  const { bffBaseUrl, fetchOverride } = options;
  const { authenticatedFetch } = useAuth();
  const doFetch = fetchOverride ?? authenticatedFetch;

  const [checking, setChecking] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const checkChanges = React.useCallback(
    async (args: CheckChangesArgs): Promise<CheckChangesResult> => {
      setChecking(true);
      setError(null);
      try {
        const url = `${bffBaseUrl}/api/compose/document/${encodeURIComponent(args.documentSpeId)}/check-changes`;
        const response = await doFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ containerId: args.containerId }),
        });
        if (!response.ok) {
          const message = `Could not check for document changes (status ${response.status}).`;
          setError(message);
          throw new Error(message);
        }
        return (await response.json()) as CheckChangesResult;
      } finally {
        setChecking(false);
      }
    },
    [bffBaseUrl, doFetch]
  );

  return { checking, error, checkChanges };
}
