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

import type { AnchoredAnnotation } from '../types/compose-contracts';
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

/** A native comment recovered from the current SPE document (mirror of BFF `RecoveredComment`). */
export interface RecoveredComment {
  [key: string]: unknown;
}

/** A native revision recovered from the current SPE document (mirror of BFF `RecoveredRevision`). */
export interface RecoveredRevision {
  [key: string]: unknown;
}

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
 * `preview` is a short, Tier-1-safe label the conflict UI shows.
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
// Hook: push-annotations (gap 3.1)
// ---------------------------------------------------------------------------

export interface PushAnnotationsArgs {
  /** SPE drive-item id of the Compose document. */
  documentSpeId: string;
  /** SPE drive id. */
  driveId: string;
  /** Tenant id (multi-tenant scoping). */
  tenantId: string;
  /** Load-time ETag for optimistic concurrency (If-Match). */
  ifMatch: string;
  /** The accepted annotations to render as native track-changes + comments. */
  annotations: DocxAnnotationInput[];
}

export interface UseComposePushAnnotationsResult {
  /** True while a push is in flight. */
  pushing: boolean;
  /** A user-safe error message when the last push failed (null otherwise). */
  error: string | null;
  /** POSTs the accepted annotations into the .docx via push-annotations; resolves on 200. */
  push: (args: PushAnnotationsArgs) => Promise<void>;
}

/**
 * FR-24 (gap 3.1) — "Push to Word" client caller. POSTs the session's accepted annotations to the
 * existing push-annotations endpoint so they become native Word track-changes + comments in SPE.
 */
export function useComposePushAnnotations(options: UseComposeWordShuttleOptions): UseComposePushAnnotationsResult {
  const { bffBaseUrl, fetchOverride } = options;
  const { authenticatedFetch } = useAuth();
  const doFetch = fetchOverride ?? authenticatedFetch;

  const [pushing, setPushing] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const push = React.useCallback(
    async (args: PushAnnotationsArgs): Promise<void> => {
      setPushing(true);
      setError(null);
      try {
        const url = `${bffBaseUrl}/api/compose/document/${encodeURIComponent(args.documentSpeId)}/push-annotations`;
        const response = await doFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            driveId: args.driveId,
            tenantId: args.tenantId,
            ifMatch: args.ifMatch,
            annotations: args.annotations,
          }),
        });
        if (!response.ok) {
          // ADR-019: a single user-safe line; no raw server detail surfaced.
          const message =
            response.status === 412 || response.status === 423
              ? 'Could not push to Word — the document changed or is open in Word. Reload and try again.'
              : `Could not push annotations to Word (status ${response.status}).`;
          setError(message);
          throw new Error(message);
        }
      } finally {
        setPushing(false);
      }
    },
    [bffBaseUrl, doFetch]
  );

  return { pushing, error, push };
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
