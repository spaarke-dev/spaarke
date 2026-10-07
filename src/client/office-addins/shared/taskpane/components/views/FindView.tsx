import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Body1,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DocumentSearchRegular } from '@fluentui/react-icons';
import { apiClient, ApiClientError, authService } from '@shared/services';
import { cleanGuid } from '@spaarke/ui-components/guid';
import { useAnnounce } from '../../hooks/useAnnounce';
import { useDocumentProfile } from '../../hooks/useDocumentProfile';
import { deriveRecordSearchSeed, useFindRecordMatches } from '../../hooks/useFindRecordMatches';
import type { DocumentIdentityState } from '../../services/documentIdentityService';
import { openRecord } from '../../services/openRecordLauncher';
import { FindResultsList, type DocumentsResultState, type FindResultNode } from '../FindResultsList';

/**
 * FindView — the Find tab's real three-state gate (spaarkeai-word-add-in-r1 task 033, FR-16b).
 *
 * Replaces task 015's static placeholder body at the SAME mount point (`currentTab === 'find'` in
 * `App.tsx`) — no new view, no new tab wiring (task 015's `notes/015-tab-shell-decisions.md` §4).
 *
 * **The three spec states, driven by (a) whether task 013 resolved a `sprk_document` and (b) the
 * tri-state `sprk_searchindexed` BIT:**
 *
 * | State | Shows |
 * |---|---|
 * | No `sprk_document` | "Save this document to Spaarke…" + a control that switches to Save |
 * | Resolved, not indexed (`sprk_searchindexed` false or null — not distinguished) | "This document isn't indexed yet." + Run Index |
 * | Resolved, indexed (`sprk_searchindexed` true) | Similar documents (task 034) and matching records (task 077) |
 *
 * **The identity outcomes task 013 can produce are richer than "exists or not".**
 * `documentIdentityService.resolveDocumentIdentity` returns one of `resolved` / `new` / `conflict` /
 * `indeterminate` / `denied` / `error`. Only `new` (and `undefined` — identity does not apply, i.e.
 * Outlook; see the `resolveFindState` remarks below) maps to the save-prompt state. `conflict`,
 * `indeterminate`, `denied` and `error` each get their OWN honest Find state — none of them are ever
 * treated as "new", mirroring task 024's `SaveModeSection` precedent for the exact same identity union.
 *
 * **Run Index** calls the EXISTING `POST /api/ai/rag/send-to-index` (no new endpoint — plan.md §3).
 * `TenantId` comes from `authService.getAccount()?.tenantId` — MSAL's `AccountInfo.tenantId`, already
 * populated from the token's `tid` claim by `@spaarke/auth`; nothing is hand-parsed or hard-coded.
 * A 200 response carrying `ChunksIndexed = 0` (or a per-document `Success: false`) is treated as a
 * FAILURE, never as success (`.claude/patterns/ai/indexing-pipeline.md`).
 *
 * **`sprk_searchindexed` is read, never written, by this view** — it comes from `useDocumentProfile`
 * (task 021's `GET /api/v1/documents/{id}` read, extended by task 033 to also expose it; see that
 * hook's header comment for the §11 "extend, don't add" reasoning). After Run Index completes, the
 * view calls that hook's `refetch()` to re-poll — `RagIndexingJobHandler`/`RagEndpoints.SendToIndex`
 * are the only writers of the field, never the add-in.
 *
 * Fluent UI v9 + Griffel `makeStyles` + semantic tokens only (ADR-021); `useAnnounce` announces every
 * state transition, including entering/leaving the Run Index in-progress state (NFR-11).
 */

// ─────────────────────────────────────────────────────────────────────────────────────────────
// State derivation (pure — independently unit-tested)
// ─────────────────────────────────────────────────────────────────────────────────────────────

export type FindState =
  | { kind: 'checking' }
  | { kind: 'no-document' }
  | { kind: 'identity-conflict' }
  | { kind: 'identity-indeterminate'; reason: 'unavailable' | 'system_failure' }
  | { kind: 'identity-denied' }
  | { kind: 'identity-error'; message: string }
  | { kind: 'loading-index-status' }
  | { kind: 'index-status-error'; message: string }
  | { kind: 'not-indexed'; documentId: string }
  | { kind: 'indexed'; documentId: string };

/**
 * Pure derivation of the Find state from task 013's identity outcome and task 021/033's
 * `sprk_searchindexed` read. No network, no React — independently unit-testable (mirrors
 * `SaveModeSection.resolveSaveMode`'s precedent for the same `DocumentIdentityState` union).
 *
 * `documentIdentity === undefined` means identity resolution does not apply on this host (Outlook —
 * `hostAdapter.getCapabilities().canGetDocumentUrl` is always false there; see
 * `documentIdentityService.ts` and `App.tsx`'s `DocumentIdentityState` doc). There is no Outlook-side
 * equivalent of task 013's URL-based resolution, so on its own it is treated the SAME as `'new'` —
 * consistent with `SaveModeSection.resolveSaveMode`'s `identity === undefined → view: 'none'`
 * (defaults to "we don't know of an existing record"). See
 * `projects/spaarkeai-word-add-in-r1/notes/033-find-view-index-gating-decisions.md` for that reasoning.
 *
 * ⚠️ **Task 077 CLOSED the gap task 033 recorded here.** This comment used to state that "no client
 * flow threads a just-completed save's documentId back into `App.savedContext` for either host today".
 * That was true when task 033 was written and **task 036 / FR-15 made it false**: `SaveView.onComplete`
 * (`App.tsx`) now writes `savedContext.documentId` on BOTH hosts. So `undefined`/`new` is only the
 * save-prompt state when NO save has completed either — see the `savedDocumentId` parameter below.
 * Left as a corrected note rather than deleted, so a reader who remembers the old limitation can see
 * that it was closed rather than overlooked.
 */
export function resolveFindState(
  documentIdentity: DocumentIdentityState | undefined,
  searchIndexed: boolean | null | undefined,
  indexStatusErrorMessage: string | undefined,
  savedDocumentId?: string | undefined
): FindState {
  if (documentIdentity === 'checking') {
    return { kind: 'checking' };
  }

  // Task 077 gaps (b) + (c). `undefined` (identity does not apply — Outlook) and `new` (no existing
  // record was found) are the two outcomes that mean "we do not know of a `sprk_document` for this
  // item". A COMPLETED SAVE answers exactly that question, so when one has handed us an id, use it
  // rather than showing the save prompt for a document that is already saved.
  //
  // This fixes BOTH gaps with one branch, which is why they are one change:
  //   (c) Word — a document saved during this pane session becomes findable without reopening.
  //   (b) Outlook — `canGetDocumentUrl` is always false there (an email has no document URL), so
  //       `documentIdentity` is permanently `undefined` and the Find tab could NEVER leave state 1.
  //       A completed save is the only place Outlook ever learns a documentId, so this is the only
  //       thing that makes its Find tab reachable at all. No Graph call, no manifest change.
  //
  // Deliberately NOT applied to `conflict` / `indeterminate` / `denied` / `error`: those are honest
  // refusals reporting that something is WRONG with identity resolution, and papering over them with
  // a save id would hide a real defect. Only the two "we don't know" outcomes are overridden — the
  // same principle as this view's existing rule that none of those four is ever treated as `new`.
  if (documentIdentity === undefined || documentIdentity.kind === 'new') {
    if (savedDocumentId === undefined) {
      return { kind: 'no-document' };
    }
    if (indexStatusErrorMessage) {
      return { kind: 'index-status-error', message: indexStatusErrorMessage };
    }
    if (searchIndexed === undefined) {
      return { kind: 'loading-index-status' };
    }
    return searchIndexed
      ? { kind: 'indexed', documentId: savedDocumentId }
      : { kind: 'not-indexed', documentId: savedDocumentId };
  }

  switch (documentIdentity.kind) {
    case 'conflict':
      return { kind: 'identity-conflict' };
    case 'indeterminate':
      return { kind: 'identity-indeterminate', reason: documentIdentity.reason };
    case 'denied':
      return { kind: 'identity-denied' };
    case 'error':
      return { kind: 'identity-error', message: documentIdentity.message };
    case 'resolved': {
      if (indexStatusErrorMessage) {
        return { kind: 'index-status-error', message: indexStatusErrorMessage };
      }
      if (searchIndexed === undefined) {
        return { kind: 'loading-index-status' };
      }
      // false and null are deliberately NOT distinguished (spec FR-16 table).
      return searchIndexed
        ? { kind: 'indexed', documentId: documentIdentity.documentId }
        : { kind: 'not-indexed', documentId: documentIdentity.documentId };
    }
  }
}

/**
 * The noun for the pane's item. Only the states Outlook can reach use it — `checking`, `conflict`,
 * `indeterminate`, `denied` and `error` arise solely from Word's URL-based identity resolution, where
 * "document" is exactly right.
 */
export type FindItemNoun = 'document' | 'email';

function announceMessageFor(state: FindState, noun: FindItemNoun): string {
  switch (state.kind) {
    case 'checking':
      return 'Checking whether this document is in Spaarke…';
    case 'no-document':
      return `This ${noun} is not yet saved to Spaarke.`;
    case 'identity-conflict':
      return 'This document has a conflicting Spaarke record and cannot be checked for indexing.';
    case 'identity-indeterminate':
      return "Couldn't determine this document's Spaarke status right now.";
    case 'identity-denied':
      return "You don't have access to this document's Spaarke record.";
    case 'identity-error':
      return 'Something went wrong checking this document.';
    case 'loading-index-status':
      return `Checking this ${noun}’s indexing status…`;
    case 'index-status-error':
      return `Couldn't check this ${noun}'s indexing status.`;
    case 'not-indexed':
      return `This ${noun} is not indexed yet.`;
    case 'indexed':
      return `This ${noun} is indexed. Loading similar documents and matching records.`;
  }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Run Index — POST /api/ai/rag/send-to-index (existing route; task 033 fixes its index-name bug
// server-side — see RagEndpoints.SendToIndex)
// ─────────────────────────────────────────────────────────────────────────────────────────────

interface SendToIndexDocumentResultShape {
  documentId: string;
  success: boolean;
  chunksIndexed: number;
  indexName?: string | null;
  errorMessage?: string | null;
}

interface SendToIndexResponseShape {
  totalRequested: number;
  successCount: number;
  failedCount: number;
  results: SendToIndexDocumentResultShape[];
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// D-032-2 — GraphMetadata.warnings from GET /api/ai/visualization/related/{id}. Task 034 (Path 2,
// owner-approved 2026-09-15 — see notes/034-route-paging-escalation.md) renders the full results
// list here via `FindResultsList`, and MUST keep surfacing the PARTIAL_RESULTS warning per D-032-2 —
// it is the honest "the server itself withheld rows past its authorization budget" signal, unrelated
// to `FindResultsList`'s own progressive-reveal `hasMore`.
// ─────────────────────────────────────────────────────────────────────────────────────────────

const PARTIAL_RESULTS_CODE = 'PARTIAL_RESULTS';

interface GraphWarningShape {
  code: string;
  message: string;
}

interface RelatedDocumentsResponseShape {
  nodes?: FindResultNode[];
  metadata?: {
    totalResults?: number;
    warnings?: GraphWarningShape[] | null;
  };
}

type RelatedDocumentsState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; totalResults: number; partialResultsWarning: string | null; nodes: FindResultNode[] }
  | { kind: 'error'; message: string };

/**
 * Task 092 (UAT-3/8): maps this view's own fetch state to `FindResultsList`'s `DocumentsResultState`.
 * `idle` (the transient before the fetch effect's first run) folds into `loading` — `FindResultsList`
 * only needs loading/error/loaded, and there is nothing honest to show for `idle` that differs from
 * "finding similar documents…".
 */
function toDocumentsResultState(state: RelatedDocumentsState): DocumentsResultState {
  switch (state.kind) {
    case 'idle':
    case 'loading':
      return { kind: 'loading' };
    case 'error':
      return { kind: 'error', message: state.message };
    case 'loaded':
      return { kind: 'loaded', nodes: state.nodes, partialResultsWarning: state.partialResultsWarning };
  }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // Task 092 (UAT-3): NO `overflow: 'auto'` here anymore. `FindResultsList` owns the ONE scroll
  // container for the Find tab (its own `scrollArea`, flex:1/minHeight:0, no maxHeight cap) — this
  // view must not add a second overflow:auto layer above it (that was the "two scroll bars" defect).
  // `minHeight: 0` lets the flex:1 child below (FindResultsList, in the `indexed` case) actually
  // shrink to fit this container's height instead of forcing it to grow past it.
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    height: '100%',
    minHeight: 0,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  icon: {
    fontSize: '32px',
    color: tokens.colorNeutralForeground3,
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────────────────────

export interface FindViewProps {
  /**
   * The open document's identity state (task 013/024), from `App`. `undefined` = identity does not
   * apply for this host (Outlook) — see `resolveFindState`'s remarks.
   */
  documentIdentity?: DocumentIdentityState;
  /**
   * The documentId a COMPLETED SAVE produced this session (`App.savedContext.documentId`), used as an
   * identity source of last resort — see `resolveFindState`. Task 077 gaps (b) + (c).
   *
   * The producer is `SaveView.onComplete` in `App.tsx`, which fires on BOTH hosts (task 036 / FR-15).
   * For Outlook this is the ONLY source of a documentId that exists.
   */
  savedDocumentId?: string;
  /** Re-runs identity resolution ("Try again" / "Check again"). Omitted → no retry control. */
  onRetryDocumentIdentity?: () => void;
  /** Switches the pane to the Save tab. Omitted → no control shown in the save-prompt state. */
  onGoToSave?: () => void;
  /**
   * Task 077: what the pane's item is called in copy — `email` in Outlook, `document` in Word. `App`
   * derives it from the `canGetSender` CAPABILITY (an item with a sender is an email), not from
   * `hostType`, per NFR-10. Defaults to `document`.
   */
  itemNoun?: FindItemNoun;
  /**
   * Task 092 (UAT-3, NFR-10): whether this host can open a browser tab
   * (`hostAdapter.getCapabilities().canOpenBrowserWindow`, decided by `App` from the live adapter —
   * never a `hostType` check here, same pattern as `SaveView`'s `canOpenRecord`). `false`/absent
   * renders every document, parent-record and matching-record row as plain, non-interactive text —
   * the fallback surface, not an error. Defaults to `false`.
   */
  canOpenRecord?: boolean;
}

export const FindView: React.FC<FindViewProps> = ({
  documentIdentity,
  savedDocumentId,
  onRetryDocumentIdentity,
  onGoToSave,
  itemNoun = 'document',
  canOpenRecord = false,
}) => {
  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();

  // Task 092 (UAT-3, NFR-10): the Open buttons also need ORG_URL — unset, `openRecord` can only
  // no-op, so a visible row would do nothing when clicked; render it as plain text instead. Same
  // pattern as `SaveFlow.openRecordAvailable`. `handleOpenRecord` is the SAME callback for every row
  // kind (document / hub / matching record) — each caller in `FindResultsList` has already resolved
  // its own `(entityType, recordId)` before calling it, so there is nothing host-specific left here
  // beyond the capability + config gate.
  const openRecordAvailable = canOpenRecord && Boolean(process.env.ORG_URL);
  const handleOpenRecord = useCallback((entityType: string, recordId: string) => {
    openRecord({ orgUrl: process.env.ORG_URL, entityType, recordId });
  }, []);

  // Identity resolution wins when it produced a record; a completed save is the fallback. Both feed
  // the SAME `useDocumentProfile` read, so the index-status states work identically however the id
  // was learned — which is what makes Outlook's Find tab reachable at all.
  const resolvedDocumentId =
    documentIdentity !== undefined && documentIdentity !== 'checking' && documentIdentity.kind === 'resolved'
      ? documentIdentity.documentId
      : savedDocumentId;

  const {
    outcome: profileOutcome,
    searchIndexed,
    refetch: refetchIndexStatus,
  } = useDocumentProfile(resolvedDocumentId);

  const indexStatusErrorMessage = profileOutcome.kind === 'error' ? profileOutcome.message : undefined;

  const state = resolveFindState(documentIdentity, searchIndexed, indexStatusErrorMessage, savedDocumentId);

  // Task 077 gap (a) — the records half. Seeded from THIS document's AI profile (keywords → TL;DR →
  // summary), read by the same `useDocumentProfile` call above — no second read. Only in the `indexed`
  // state, where results render; `null` seed issues no request.
  const recordSeed = state.kind === 'indexed' ? deriveRecordSearchSeed(profileOutcome) : null;
  const recordMatches = useFindRecordMatches(recordSeed);

  // NFR-11: announce every state transition (not the initial mount — mirrors useAnnounceOnChange).
  const prevKindRef = useRef<FindState['kind']>(state.kind);
  useEffect(() => {
    if (prevKindRef.current === state.kind) {
      return;
    }
    prevKindRef.current = state.kind;
    announce(announceMessageFor(state, itemNoun), 'polite');
    // `itemNoun` is listed for correctness; it cannot cause a duplicate announcement, because the guard
    // above only announces when `state.kind` changes.
  }, [state, announce, itemNoun]);

  // ── Run Index ──────────────────────────────────────────────────────────────────────────────
  const [runIndexStatus, setRunIndexStatus] = useState<'idle' | 'running'>('idle');
  const [runIndexError, setRunIndexError] = useState<string | null>(null);

  const handleRunIndex = useCallback(async () => {
    if (state.kind !== 'not-indexed') {
      // Defense-in-depth — the button is only rendered/enabled in this state.
      return;
    }

    setRunIndexStatus('running');
    setRunIndexError(null);
    announce('Indexing started.', 'polite');

    try {
      const tenantId = authService.getAccount()?.tenantId;
      if (!tenantId) {
        throw new Error('Could not determine your tenant. Try signing in again.');
      }

      // ADR-044: canonicalize to bare-lowercase before it crosses the boundary.
      const documentId = cleanGuid(state.documentId);

      const response = await apiClient.post<SendToIndexResponseShape>('/api/ai/rag/send-to-index', {
        DocumentIds: [documentId],
        TenantId: tenantId,
      });

      const result = response.results?.[0];

      // indexing-pipeline.md: an HTTP 200 carrying ChunksIndexed = 0 (or a per-document
      // Success: false) is a FAILURE, not a success — never reported to the user as one.
      if (!result || !result.success || !(result.chunksIndexed > 0)) {
        setRunIndexStatus('idle');
        setRunIndexError(result?.errorMessage || 'Indexing did not complete for this document. Try again.');
        announce('Indexing failed.', 'assertive');
        return;
      }

      setRunIndexStatus('idle');
      announce('Indexing complete.', 'polite');
      // The add-in never writes sprk_searchindexed itself — re-read it. RagIndexingJobHandler /
      // RagEndpoints.SendToIndex already stamped it server-side by the time this call returned.
      refetchIndexStatus();
    } catch (err) {
      setRunIndexStatus('idle');
      const message =
        err instanceof ApiClientError
          ? err.error.detail || err.error.title
          : err instanceof Error
            ? err.message
            : 'Indexing failed.';
      setRunIndexError(message);
      announce('Indexing failed.', 'assertive');
    }
  }, [state, announce, refetchIndexStatus]);

  // ── D-032-2: minimal results container + PARTIAL_RESULTS surfacing (task 034 fills the rest) ──
  const indexedDocumentId = state.kind === 'indexed' ? state.documentId : undefined;
  const [relatedState, setRelatedState] = useState<RelatedDocumentsState>({ kind: 'idle' });

  useEffect(() => {
    if (!indexedDocumentId) {
      setRelatedState({ kind: 'idle' });
      return;
    }

    let cancelled = false;
    setRelatedState({ kind: 'loading' });

    (async () => {
      try {
        const id = cleanGuid(indexedDocumentId);
        const response = await apiClient.get<RelatedDocumentsResponseShape>(`/api/ai/visualization/related/${id}`);
        if (cancelled) return;

        const warning = response.metadata?.warnings?.find(w => w.code === PARTIAL_RESULTS_CODE);
        setRelatedState({
          kind: 'loaded',
          totalResults: response.metadata?.totalResults ?? 0,
          partialResultsWarning: warning?.message ?? null,
          nodes: response.nodes ?? [],
        });
      } catch (err) {
        if (cancelled) return;
        const message =
          err instanceof ApiClientError
            ? err.error.detail || err.error.title
            : err instanceof Error
              ? err.message
              : 'Failed to load similar documents.';
        setRelatedState({ kind: 'error', message });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [indexedDocumentId]);

  // ── Render ─────────────────────────────────────────────────────────────────────────────────

  switch (state.kind) {
    case 'checking':
    case 'loading-index-status':
      return (
        <div className={styles.container}>
          {liveRegion}
          <div className={styles.loadingContainer}>
            <Spinner size="medium" />
            <Text>Checking whether this {itemNoun} is in Spaarke…</Text>
          </div>
        </div>
      );

    case 'no-document':
      return (
        <div className={styles.container}>
          {liveRegion}
          <div className={styles.emptyState}>
            <DocumentSearchRegular className={styles.icon} />
            <Text weight="semibold">Save this {itemNoun} to Spaarke</Text>
            <Body1>
              Save this {itemNoun} to Spaarke so it can be indexed for AI similarity search. Once it&rsquo;s saved, Find
              shows similar documents and matching records here.
            </Body1>
            {onGoToSave && (
              <Button appearance="primary" onClick={onGoToSave}>
                Go to Save
              </Button>
            )}
          </div>
        </div>
      );

    case 'identity-conflict':
      return (
        <div className={styles.container}>
          {liveRegion}
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Can&rsquo;t check this document for indexing</MessageBarTitle>
              Spaarke already has a record for this file in a different storage location, so it can&rsquo;t tell which
              record this document belongs to. Ask your Spaarke administrator to check the document&rsquo;s record, then
              check again.
            </MessageBarBody>
            {onRetryDocumentIdentity && (
              <MessageBarActions>
                <Button appearance="outline" size="small" onClick={onRetryDocumentIdentity}>
                  Check again
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      );

    case 'identity-indeterminate':
      return (
        <div className={styles.container}>
          {liveRegion}
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Couldn&rsquo;t check this document</MessageBarTitle>
              {state.reason === 'unavailable'
                ? "Spaarke couldn't confirm this document's status right now, because the service is unavailable."
                : "Something went wrong while checking this document's Spaarke status."}{' '}
              Try again.
            </MessageBarBody>
            {onRetryDocumentIdentity && (
              <MessageBarActions>
                <Button appearance="outline" size="small" onClick={onRetryDocumentIdentity}>
                  Try again
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      );

    case 'identity-denied':
      return (
        <div className={styles.container}>
          {liveRegion}
          <MessageBar intent="info" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>You can&rsquo;t check this document</MessageBarTitle>
              This document is in Spaarke, but you don&rsquo;t have access to its record, so it can&rsquo;t be checked
              for indexing or searched for similar documents.
            </MessageBarBody>
          </MessageBar>
        </div>
      );

    case 'identity-error':
      return (
        <div className={styles.container}>
          {liveRegion}
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Something went wrong</MessageBarTitle>
              {state.message || 'Something went wrong checking this document.'}
            </MessageBarBody>
            {onRetryDocumentIdentity && (
              <MessageBarActions>
                <Button appearance="outline" size="small" onClick={onRetryDocumentIdentity}>
                  Try again
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      );

    case 'index-status-error':
      return (
        <div className={styles.container}>
          {liveRegion}
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Couldn&rsquo;t check this {itemNoun}&rsquo;s indexing status</MessageBarTitle>
              {state.message}
            </MessageBarBody>
            <MessageBarActions>
              <Button appearance="outline" size="small" onClick={refetchIndexStatus}>
                Try again
              </Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      );

    case 'not-indexed':
      return (
        <div className={styles.container}>
          {liveRegion}
          <div className={styles.section}>
            <Text weight="semibold">This {itemNoun} isn&rsquo;t indexed yet.</Text>
            <Body1>
              Index this {itemNoun} to find documents similar to it. This uses the AI similarity search this {itemNoun}{' '}
              belongs to.
            </Body1>
            <Button
              appearance="primary"
              onClick={handleRunIndex}
              disabled={runIndexStatus === 'running'}
              {...(runIndexStatus === 'running' ? { icon: <Spinner size="tiny" /> } : {})}
            >
              {runIndexStatus === 'running' ? 'Indexing…' : 'Run Index'}
            </Button>
            {runIndexError && (
              <MessageBar intent="error" layout="multiline">
                <MessageBarBody>
                  <MessageBarTitle>Indexing failed</MessageBarTitle>
                  {runIndexError}
                </MessageBarBody>
              </MessageBar>
            )}
          </div>
        </div>
      );

    case 'indexed':
      // Task 092 (UAT-3/8): FindResultsList is ALWAYS rendered here — it owns both the "Most similar
      // documents" and "Matching records" sections independently (documents loading/erroring never
      // hides or delays the records section, and vice versa). This view only supplies each section's
      // own data/state; it no longer gates FindResultsList's mount on `relatedState.kind === 'loaded'`.
      return (
        <div className={styles.container}>
          {liveRegion}
          <FindResultsList
            documents={toDocumentsResultState(relatedState)}
            announce={announce}
            records={recordMatches}
            {...(openRecordAvailable ? { onOpenRecord: handleOpenRecord } : {})}
          />
        </div>
      );
  }
};

export default FindView;
