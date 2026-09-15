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
import { cleanGuid } from '../../utils/cleanGuid';
import { useAnnounce } from '../../hooks/useAnnounce';
import { useDocumentProfile } from '../../hooks/useDocumentProfile';
import type { DocumentIdentityState } from '../../services/documentIdentityService';

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
 * | Resolved, indexed (`sprk_searchindexed` true) | A minimal results container (task 034 fills it in) |
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
 * `documentIdentityService.ts` and `App.tsx`'s `DocumentIdentityState` doc). There is currently no
 * Outlook-side equivalent of task 013's URL-based resolution, so this is treated the SAME as `'new'`
 * — consistent with `SaveModeSection.resolveSaveMode`'s `identity === undefined → view: 'none'`
 * (defaults to "we don't know of an existing record"), and an honest default: Find cannot show
 * similarity results for a document it has no id for. See
 * `projects/spaarkeai-word-add-in-r1/notes/033-find-view-index-gating-decisions.md` for the full
 * reasoning and the known gap this inherits (no client flow threads a just-completed save's
 * documentId back into `App.savedContext` for either host today — pre-existing, not introduced here).
 */
export function resolveFindState(
  documentIdentity: DocumentIdentityState | undefined,
  searchIndexed: boolean | null | undefined,
  indexStatusErrorMessage: string | undefined
): FindState {
  if (documentIdentity === 'checking') {
    return { kind: 'checking' };
  }

  if (documentIdentity === undefined || documentIdentity.kind === 'new') {
    return { kind: 'no-document' };
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

function announceMessageFor(state: FindState): string {
  switch (state.kind) {
    case 'checking':
      return 'Checking whether this document is in Spaarke…';
    case 'no-document':
      return 'This document is not yet saved to Spaarke.';
    case 'identity-conflict':
      return 'This document has a conflicting Spaarke record and cannot be checked for indexing.';
    case 'identity-indeterminate':
      return "Couldn't determine this document's Spaarke status right now.";
    case 'identity-denied':
      return "You don't have access to this document's Spaarke record.";
    case 'identity-error':
      return 'Something went wrong checking this document.';
    case 'loading-index-status':
      return 'Checking this document’s indexing status…';
    case 'index-status-error':
      return "Couldn't check this document's indexing status.";
    case 'not-indexed':
      return 'This document is not indexed yet.';
    case 'indexed':
      return 'This document is indexed. Loading similar documents.';
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
// D-032-2 — GraphMetadata.warnings from GET /api/ai/visualization/related/{id}. Task 034 owns the
// full results view (lazy-scroll + records bridge); this task's results container is intentionally
// minimal (task 015 precedent), but MUST surface the PARTIAL_RESULTS warning per D-032-2.
// ─────────────────────────────────────────────────────────────────────────────────────────────

const PARTIAL_RESULTS_CODE = 'PARTIAL_RESULTS';

interface GraphWarningShape {
  code: string;
  message: string;
}

interface RelatedDocumentsResponseShape {
  nodes?: unknown[];
  metadata?: {
    totalResults?: number;
    warnings?: GraphWarningShape[] | null;
  };
}

type RelatedDocumentsState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; totalResults: number; partialResultsWarning: string | null }
  | { kind: 'error'; message: string };

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    height: '100%',
    overflow: 'auto',
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
  /** Re-runs identity resolution ("Try again" / "Check again"). Omitted → no retry control. */
  onRetryDocumentIdentity?: () => void;
  /** Switches the pane to the Save tab. Omitted → no control shown in the save-prompt state. */
  onGoToSave?: () => void;
}

export const FindView: React.FC<FindViewProps> = ({ documentIdentity, onRetryDocumentIdentity, onGoToSave }) => {
  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();

  const resolvedDocumentId =
    documentIdentity !== undefined && documentIdentity !== 'checking' && documentIdentity.kind === 'resolved'
      ? documentIdentity.documentId
      : undefined;

  const {
    outcome: profileOutcome,
    searchIndexed,
    refetch: refetchIndexStatus,
  } = useDocumentProfile(resolvedDocumentId);

  const indexStatusErrorMessage = profileOutcome.kind === 'error' ? profileOutcome.message : undefined;

  const state = resolveFindState(documentIdentity, searchIndexed, indexStatusErrorMessage);

  // NFR-11: announce every state transition (not the initial mount — mirrors useAnnounceOnChange).
  const prevKindRef = useRef<FindState['kind']>(state.kind);
  useEffect(() => {
    if (prevKindRef.current === state.kind) {
      return;
    }
    prevKindRef.current = state.kind;
    announce(announceMessageFor(state), 'polite');
  }, [state, announce]);

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
            <Text>Checking whether this document is in Spaarke…</Text>
          </div>
        </div>
      );

    case 'no-document':
      return (
        <div className={styles.container}>
          {liveRegion}
          <div className={styles.emptyState}>
            <DocumentSearchRegular className={styles.icon} />
            <Text weight="semibold">Save this document to Spaarke</Text>
            <Body1>Save this document to Spaarke so it can be indexed for AI similarity search.</Body1>
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
              <MessageBarTitle>Couldn&rsquo;t check this document&rsquo;s indexing status</MessageBarTitle>
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
            <Text weight="semibold">This document isn&rsquo;t indexed yet.</Text>
            <Body1>
              Index this document to find documents similar to it. This uses the AI similarity search this document
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
      return (
        <div className={styles.container}>
          {liveRegion}
          {relatedState.kind === 'loading' && (
            <div className={styles.loadingContainer}>
              <Spinner size="medium" />
              <Text>Finding similar documents…</Text>
            </div>
          )}
          {relatedState.kind === 'error' && (
            <MessageBar intent="error" layout="multiline">
              <MessageBarBody>
                <MessageBarTitle>Couldn&rsquo;t load similar documents</MessageBarTitle>
                {relatedState.message}
              </MessageBarBody>
            </MessageBar>
          )}
          {relatedState.kind === 'loaded' && (
            <div className={styles.section}>
              {relatedState.partialResultsWarning && (
                <MessageBar intent="warning" layout="multiline">
                  <MessageBarBody>
                    <MessageBarTitle>Results may be incomplete</MessageBarTitle>
                    {relatedState.partialResultsWarning}
                  </MessageBarBody>
                </MessageBar>
              )}
              <Text>
                {relatedState.totalResults > 0
                  ? `${relatedState.totalResults} similar document${relatedState.totalResults === 1 ? '' : 's'} found.`
                  : 'No similar documents found.'}
              </Text>
              <Body1>Similarity results are coming soon.</Body1>
            </div>
          )}
        </div>
      );
  }
};

export default FindView;
