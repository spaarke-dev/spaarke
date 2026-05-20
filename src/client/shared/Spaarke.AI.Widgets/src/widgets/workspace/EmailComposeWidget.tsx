/**
 * @spaarke/ai-widgets — EmailComposeWidget
 *
 * Workspace widget that opens the Analysis Builder (Playbook Library Code Page)
 * pre-configured for the `email-compose` intent (FR-19: Send Email card).
 *
 * Analysis Builder invocation contract (DISCOVERED from existing usage):
 * ------------------------------------------------------------------------
 * The "Analysis Builder" is the existing `sprk_playbooklibrary` Code Page (it
 * was merged into the Playbook Library). It is invoked via the Dataverse host
 * navigation API:
 *
 *   Xrm.Navigation.navigateTo(
 *     {
 *       pageType: "webresource",
 *       webresourceName: "sprk_playbooklibrary",
 *       data: "intent=email-compose[&bffBaseUrl=...]"
 *     },
 *     { target: 2, width: { value: 60, unit: "%" },
 *       height: { value: 70, unit: "%" },
 *       title: "Playbook Library" }
 *   );
 *
 * The `intent` data param accepts the canonical intent IDs defined in
 * `src/solutions/LegalWorkspace/src/components/GetStarted/analysisBuilderTypes.ts`
 * — specifically `email-compose` and `meeting-schedule`. The Playbook Library
 * reads the intent param on mount and pre-configures its initial workflow.
 *
 * Canonical existing usage (the reference implementation):
 *   `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts`
 *   lines 77–94 (`openPlaybookIntent` helper, called by `createPlaybookHandlers`).
 *
 * Alternative pattern (window.open) exists in `nextStepLauncher.ts` for iframe
 * contexts where `navigateTo` triggers a "Leave this page?" dialog. This widget
 * runs inside the SpaarkeAi Code Page (which IS itself an iframe under Dataverse)
 * — so we use the `navigateTo` pattern matching ActionCardHandlers (proven path
 * for cards inside Code Pages).
 *
 * Design notes:
 * - This widget is a THIN DISPATCHER, not an embedded multi-step flow. It opens
 *   a Dataverse modal dialog (the Playbook Library) on mount. The dialog is
 *   self-contained — once opened, the widget tab simply displays a
 *   launching/launched status message.
 * - In non-Dataverse environments (Vite dev, tests) `Xrm` is unavailable; the
 *   widget renders a clear placeholder message rather than crashing.
 * - No `authenticatedFetch` is required — the Playbook Library Code Page makes
 *   its own MSAL-authenticated BFF calls (passed `bffBaseUrl` via data param).
 *   ADR-028 invariant: no token snapshots in props or state.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: 044 (FR-19 — Send Email)
 *
 * @see ADR-012  — Shared component library (reuse, not copy)
 * @see ADR-021  — Fluent UI v9 tokens only
 * @see ADR-025  — @fluentui/react-icons v9
 * @see ADR-028  — No token snapshots in props/state
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  MailRegular,
  OpenRegular,
} from '@fluentui/react-icons';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Web resource name of the Playbook Library (= Analysis Builder) Code Page. */
const ANALYSIS_BUILDER_WEBRESOURCE = 'sprk_playbooklibrary';

/** Canonical intent identifier for the compose-email flow (FR-19). */
const INTENT = 'email-compose';

/** Display label shown in the launching status and dialog title. */
const DISPLAY_NAME = 'Send Email';

// ---------------------------------------------------------------------------
// Data payload shape
// ---------------------------------------------------------------------------

/**
 * Data delivered to this widget on mount or via SSE.
 *
 * All fields are optional — the widget renders a placeholder if anything
 * required for launch is missing.
 */
export interface EmailComposeData {
  /**
   * BFF API base URL passed to the Playbook Library Code Page so it can make
   * MSAL-authenticated BFF calls (e.g. `https://spe-api-dev-67e2xz.azurewebsites.net/api`).
   */
  bffBaseUrl?: string;

  /**
   * Optional pre-fill prompt. The widget passes this through to the Playbook
   * Library via the data param (the Playbook Library reads it as the opening
   * user message).
   */
  initialPrompt?: string;
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
 * Open the Analysis Builder (Playbook Library Code Page) with the
 * `email-compose` intent pre-configured.
 *
 * Mirrors the navigateTo pattern in
 * `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts`.
 */
async function openAnalysisBuilder(
  bffBaseUrl: string | undefined,
  initialPrompt: string | undefined
): Promise<void> {
  const nav = resolveXrmNavigation();
  if (!nav) {
    throw new Error(
      'Xrm.Navigation is unavailable. Analysis Builder can only be opened from a Dataverse host.'
    );
  }

  const params: string[] = [`intent=${INTENT}`];
  if (bffBaseUrl) {
    params.push(`bffBaseUrl=${encodeURIComponent(bffBaseUrl)}`);
  }
  if (initialPrompt) {
    params.push(`initialPrompt=${encodeURIComponent(initialPrompt)}`);
  }

  await nav.navigateTo(
    {
      pageType: 'webresource',
      webresourceName: ANALYSIS_BUILDER_WEBRESOURCE,
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
 * EmailComposeWidget
 *
 * Mounts inside a workspace tab and immediately invokes
 * `Xrm.Navigation.navigateTo` to open the Analysis Builder Code Page with the
 * `email-compose` intent. The tab itself shows a launching status while the
 * dialog is in flight; once the dialog closes, the user can either close the
 * tab or relaunch.
 */
const EmailComposeWidget: React.FC<WorkspaceWidgetProps<EmailComposeData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  /** UI state. */
  const [launchState, setLaunchState] = useState<'launching' | 'opened' | 'error' | 'unavailable'>(
    'launching'
  );
  const [launchError, setLaunchError] = useState<string | null>(null);

  /** Guard against double-launch in React 19 StrictMode (effects run twice in dev). */
  const launchedRef = useRef(false);

  const handleLaunch = useCallback(async () => {
    setLaunchState('launching');
    setLaunchError(null);
    try {
      await openAnalysisBuilder(data?.bffBaseUrl, data?.initialPrompt);
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
  }, [data?.bffBaseUrl, data?.initialPrompt]);

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
        <MailRegular className={styles.icon} aria-hidden />
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  // ── Render: unavailable (Vite dev / non-Dataverse host) ─────────────────
  if (launchState === 'unavailable') {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <MailRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text size={300} className={styles.subtitle}>
          Analysis Builder is only available inside a Dataverse host. Open this
          page from within Power Apps to compose an email.
        </Text>
      </div>
    );
  }

  // ── Render: launch error ────────────────────────────────────────────────
  if (launchState === 'error') {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <MailRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text className={styles.errorText}>
          {launchError ?? 'Failed to open Analysis Builder.'}
        </Text>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={() => void handleLaunch()}
          aria-label={`Retry opening ${DISPLAY_NAME}`}
          data-testid="email-compose-retry"
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
        <MailRegular className={styles.icon} aria-hidden />
        <Text as="h2" size={500} weight="semibold" className={styles.title}>
          {DISPLAY_NAME}
        </Text>
        <Text size={300} className={styles.subtitle}>
          The Analysis Builder dialog has closed. Click below to reopen it.
        </Text>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={() => void handleLaunch()}
          aria-label={`Reopen ${DISPLAY_NAME}`}
          data-testid="email-compose-relaunch"
        >
          Reopen
        </Button>
      </div>
    );
  }

  // ── Render: launching ───────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)}>
      <MailRegular className={styles.icon} aria-hidden />
      <Text as="h2" size={500} weight="semibold" className={styles.title}>
        {DISPLAY_NAME}
      </Text>
      <Spinner size="medium" label="Opening Analysis Builder..." />
    </div>
  );
};

EmailComposeWidget.displayName = 'EmailComposeWidget';

// ---------------------------------------------------------------------------
// serializeState helper (D-08 — identifiers only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 *
 * The widget is a thin dispatcher — there is no in-widget state to restore
 * (the Analysis Builder dialog is self-contained). We round-trip only the
 * widget type so the shell can re-mount the tab on session restore; on
 * restore the widget will auto-launch the dialog again per its mount effect.
 */
export function serializeEmailComposeState(): WidgetState<EmailComposeData> {
  return {
    widgetType: 'email-compose',
    version: 1,
    queryParams: {},
    timestamp: new Date().toISOString(),
  };
}

export default EmailComposeWidget;
