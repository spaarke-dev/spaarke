/**
 * @spaarke/ai-widgets — FindSimilarWizardWidget
 *
 * Workspace widget that opens the existing Find Similar Documents Code Page
 * (`sprk_findsimilar`) as a Dataverse modal dialog (FR-19: Find Similar card).
 *
 * Existing Find Similar dialog invocation contract (REUSED, not re-authored):
 * ---------------------------------------------------------------------------
 * The "Find Similar" dialog is the existing `sprk_findsimilar` Code Page
 * (solution: `src/solutions/FindSimilarCodePage/`). It wraps the shared
 * `FindSimilarApp` component which handles either selecting an existing
 * `sprk_document` record via Xrm lookup, or uploading a file, before opening
 * the `sprk_documentrelationshipviewer` Code Page. The dialog is invoked via
 * the Dataverse host navigation API:
 *
 *   Xrm.Navigation.navigateTo(
 *     {
 *       pageType: "webresource",
 *       webresourceName: "sprk_findsimilar",
 *       data: "bffBaseUrl=...[&documentId=...][&containerId=...]"
 *     },
 *     { target: 2, width: { value: 60, unit: "%" },
 *       height: { value: 70, unit: "%" },
 *       title: "Find Similar Documents" }
 *   );
 *
 * Canonical existing usage (the reference implementation):
 *   `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts` (line 63)
 *   and `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`
 *   (lines 294–299, `navigateTo({webresourceName: "sprk_findsimilar"})`).
 *
 * Design notes:
 * - This widget is a THIN DISPATCHER, not an embedded multi-step flow. It opens
 *   a Dataverse modal dialog (the Find Similar Code Page) on mount. The Code
 *   Page is self-contained — once opened, the widget tab simply displays a
 *   launching / launched status message. OC-04 ("reuse, do not re-author") is
 *   satisfied because the Find Similar UI lives in the existing Code Page;
 *   the widget is a launcher only.
 * - In non-Dataverse environments (Vite dev, tests) `Xrm` is unavailable; the
 *   widget renders a clear placeholder message rather than crashing.
 * - No `authenticatedFetch` is required at this layer — the Find Similar Code
 *   Page initializes its own MSAL-authenticated context (it calls `initAuth()`
 *   in its `main.tsx`) and passes the resulting `authenticatedFetch` to
 *   `FindSimilarApp`. ADR-028 invariant: no token snapshots in props or state.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: 043 (FR-19 — Find Similar)
 *
 * @see ADR-012  — Shared component library (reuse, not copy)
 * @see ADR-021  — Fluent UI v9 tokens only
 * @see ADR-025  — @fluentui/react-icons v9
 * @see ADR-028  — No token snapshots in props/state
 * @see CreateProjectWizardWidget — sibling dispatcher widget with identical mechanics
 * @see EmailComposeWidget        — dispatcher pattern blueprint (task 044)
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Spinner, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DocumentSearchRegular, OpenRegular } from '@fluentui/react-icons';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Web resource name of the Find Similar Documents Code Page. */
const FIND_SIMILAR_WEBRESOURCE = 'sprk_findsimilar';

/** Display label shown in the launching status and dialog title. */
const DISPLAY_NAME = 'Find Similar Documents';

// ---------------------------------------------------------------------------
// Data payload shape
// ---------------------------------------------------------------------------

/**
 * Data delivered to this widget on mount or via SSE.
 *
 * All fields are optional — the widget renders a placeholder if anything
 * required for launch is missing.
 */
export interface FindSimilarWizardData {
  /**
   * BFF API base URL passed to the Find Similar Code Page so it can make
   * MSAL-authenticated BFF calls (e.g.
   * `https://spe-api-dev-67e2xz.azurewebsites.net/api`).
   */
  bffBaseUrl?: string;

  /**
   * Optional pre-selected source document id. When present, the Find Similar
   * Code Page can pre-populate Path A (existing-document lookup) without
   * requiring the user to re-select. Mirrors the documentId param accepted by
   * `WorkspaceGrid` (lines 294–298).
   */
  documentId?: string;

  /**
   * Optional SPE container id, passed through for context when the Find
   * Similar Code Page invokes the DocumentRelationshipViewer.
   */
  containerId?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalL,
    textAlign: 'center',
  },
  icon: {
    color: tokens.colorBrandForeground1,
    fontSize: '48px',
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
    maxWidth: '480px',
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
    maxWidth: '480px',
  },
  buttonRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// navigateTo helper
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Resolve the host Xrm.Navigation API by walking the frame hierarchy.
 * Returns null when running outside a Dataverse host (e.g. Vite dev).
 */
function resolveXrmNavigation(): any | null {
  const frames: Window[] = [window];
  try {
    if (window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin — skip */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin — skip */
  }

  for (const frame of frames) {
    try {
      const nav = (frame as any).Xrm?.Navigation;
      if (nav?.navigateTo) {
        return nav;
      }
    } catch {
      /* cross-origin — skip */
    }
  }
  return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Open the existing Find Similar Documents Code Page.
 *
 * Mirrors the navigateTo pattern in
 * `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`
 * (lines 294–299) so behavior is identical to the standalone LegalWorkspace
 * invocation path.
 */
async function openFindSimilar(
  bffBaseUrl: string | undefined,
  documentId: string | undefined,
  containerId: string | undefined
): Promise<void> {
  const nav = resolveXrmNavigation();
  if (!nav) {
    throw new Error('Xrm.Navigation is unavailable. Find Similar Documents can only be opened from a Dataverse host.');
  }

  const params: string[] = [];
  if (documentId) {
    params.push(`documentId=${encodeURIComponent(documentId)}`);
  }
  if (containerId) {
    params.push(`containerId=${encodeURIComponent(containerId)}`);
  }
  if (bffBaseUrl) {
    params.push(`bffBaseUrl=${encodeURIComponent(bffBaseUrl)}`);
  }

  await nav.navigateTo(
    {
      pageType: 'webresource',
      webresourceName: FIND_SIMILAR_WEBRESOURCE,
      data: params.join('&'),
    },
    {
      target: 2,
      width: { value: 60, unit: '%' },
      height: { value: 70, unit: '%' },
      title: DISPLAY_NAME,
    }
  );
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * FindSimilarWizardWidget
 *
 * Mounts inside a workspace tab and immediately invokes
 * `Xrm.Navigation.navigateTo` to open the existing Find Similar Documents
 * Code Page. The tab itself shows a launching status while the dialog is in
 * flight; once the dialog closes, the user can either close the tab or
 * relaunch.
 */
const FindSimilarWizardWidget: React.FC<WorkspaceWidgetProps<FindSimilarWizardData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  /** UI state. */
  const [launchState, setLaunchState] = useState<'launching' | 'opened' | 'error' | 'unavailable'>('launching');
  const [launchError, setLaunchError] = useState<string | null>(null);

  /** Guard against double-launch in React 19 StrictMode (effects run twice in dev). */
  const launchedRef = useRef(false);

  const handleLaunch = useCallback(async () => {
    setLaunchState('launching');
    setLaunchError(null);
    try {
      await openFindSimilar(data?.bffBaseUrl, data?.documentId, data?.containerId);
      // navigateTo resolves when the dialog CLOSES — at that point the user
      // has either completed the flow or cancelled. We mark "opened" (i.e.
      // launch succeeded) so the tab shows a relaunch affordance.
      setLaunchState('opened');
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message.includes('Xrm.Navigation is unavailable')) {
        setLaunchState('unavailable');
      } else {
        setLaunchError(message);
        setLaunchState('error');
      }
    }
  }, [data?.bffBaseUrl, data?.documentId, data?.containerId]);

  // Auto-launch on first mount. The launchedRef guard tolerates React 19
  // StrictMode (which intentionally double-invokes effects in dev).
  useEffect(() => {
    if (launchedRef.current) return;
    launchedRef.current = true;
    void handleLaunch();
  }, [handleLaunch]);

  // ── Render: external loading / error states from the shell ──────────────
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label={`Loading ${DISPLAY_NAME}...`} />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <DocumentSearchRegular className={styles.icon} aria-hidden />
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  // ── Render: unavailable (Vite dev / non-Dataverse host) ─────────────────
  if (launchState === 'unavailable') {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <DocumentSearchRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text size={300} className={styles.subtitle}>
          Find Similar Documents is only available inside a Dataverse host. Open this page from within Power Apps to
          search for similar documents.
        </Text>
      </div>
    );
  }

  // ── Render: launch error ────────────────────────────────────────────────
  if (launchState === 'error') {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <DocumentSearchRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text className={styles.errorText}>{launchError ?? 'Failed to open Find Similar Documents.'}</Text>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={() => void handleLaunch()}
          aria-label={`Retry opening ${DISPLAY_NAME}`}
          data-testid="find-similar-wizard-retry"
        >
          Retry
        </Button>
      </div>
    );
  }

  // ── Render: opened (dialog closed) — offer relaunch ─────────────────────
  if (launchState === 'opened') {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <DocumentSearchRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text size={300} className={styles.subtitle}>
          The Find Similar dialog has closed. Click below to reopen it.
        </Text>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={() => void handleLaunch()}
          aria-label={`Reopen ${DISPLAY_NAME}`}
          data-testid="find-similar-wizard-relaunch"
        >
          Reopen
        </Button>
      </div>
    );
  }

  // ── Render: launching ───────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)}>
      <DocumentSearchRegular className={styles.icon} aria-hidden />
      <Text as="h2" size={500} weight="semibold" className={styles.title}>
        {DISPLAY_NAME}
      </Text>
      <Spinner size="medium" label="Opening Find Similar Documents..." />
    </div>
  );
};

FindSimilarWizardWidget.displayName = 'FindSimilarWizardWidget';

// ---------------------------------------------------------------------------
// serializeState helper (D-08 — identifiers only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 *
 * The widget is a thin dispatcher — there is no in-widget state to restore
 * (the Find Similar Code Page is self-contained). We round-trip only the
 * widget type so the shell can re-mount the tab on session restore; on
 * restore the widget will auto-launch the dialog again per its mount effect.
 */
export function serializeFindSimilarWizardState(): WidgetState<FindSimilarWizardData> {
  return {
    widgetType: 'find-similar-wizard',
    version: 1,
    queryParams: {},
    timestamp: new Date().toISOString(),
  };
}

export default FindSimilarWizardWidget;
