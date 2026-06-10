/**
 * RecordNavigationModalShell — universal modal shell for cross-record
 * navigation around an embedded record surface (typically an iframe hosting
 * an OOB MDA form).
 *
 * Renders chrome (header with `<` / `>` nav, "N of M" counter, title, and an
 * optional action-bar slot) plus a content area for caller-supplied children.
 * Orchestrates the cross-frame `request-dirty-check` / `dirty-check-result`
 * protocol so unsaved-change prompts surface BEFORE the iframe `src` swaps.
 *
 * The shell does NOT own the modal envelope (`Dialog` / `DialogSurface`).
 * Callers wrap it in their own modal surface — either a Fluent v9 `Dialog`
 * (matter-ui-style preview dialogs) or a Code Page launched via
 * `Xrm.Navigation.navigateTo` (smart-todo-r4 SmartTodo modal per FR-13 /
 * FR-17).
 *
 * Cross-frame messaging contract (FR-14) — see this component's `README.md`
 * for the authoritative protocol description. Summary:
 *   1. On nav click, shell posts `{type: "request-dirty-check", correlationId}`
 *      to `dirtyCheckTargetWindow` (the iframe's `contentWindow`).
 *   2. Iframe-side listener responds `{type: "dirty-check-result",
 *      correlationId, dirty: bool}`. Inbound origin is validated against
 *      `allowedOrigins`.
 *   3. Timeout fallback: if no response within `dirtyCheckTimeout` ms, the
 *      shell treats the iframe as clean and proceeds.
 *   4. If `dirty === true`, a Fluent v9 `Dialog` confirms discard. On confirm,
 *      `onDirtyDiscard` fires, then `onNavigate` runs. On cancel, navigation
 *      is aborted (no state change).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-022 - React version boundaries (16.14-safe, no React 18-only APIs)
 * @see spec.md FR-12, FR-13, FR-14 (smart-todo-r4)
 */

import * as React from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Divider,
  Text,
  Tooltip,
  mergeClasses,
} from '@fluentui/react-components';
import { ChevronLeft20Regular, ChevronRight20Regular } from '@fluentui/react-icons';
import { useRecordNavigationModalShellStyles } from './RecordNavigationModalShell.styles';
import {
  DIRTY_CHECK_REQUEST_TYPE,
  DIRTY_CHECK_RESULT_TYPE,
  type IDirtyCheckRequest,
  type IDirtyCheckResponse,
  type IRecordNavigationModalShellProps,
  type RecordNavigationDirection,
} from './types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Default allowed inbound origins for `dirty-check-result` messages.
 *
 * Pattern syntax:
 *   - exact: `"https://contoso.crm.dynamics.com"`
 *   - subdomain wildcard: `"https://*.dynamics.com"` matches
 *     `https://contoso.crm.dynamics.com` but NOT `https://dynamics.com` (the
 *     wildcard requires at least one label).
 *   - `window.location.origin` is added at runtime so same-origin iframes
 *     (e.g. Code Page → Code Page embedding) work without configuration.
 */
const DEFAULT_ALLOWED_ORIGIN_PATTERNS: ReadonlyArray<string> = Object.freeze(['https://*.dynamics.com']);

/**
 * Returns whether `origin` matches any pattern in `allowList`.
 * Single leading `*.` subdomain wildcards are supported.
 */
function isOriginAllowed(origin: string, allowList: ReadonlyArray<string>): boolean {
  if (!origin) return false;
  for (const pattern of allowList) {
    if (pattern === origin) return true;
    // Wildcard subdomain pattern: "https://*.foo.com"
    const wildcardMatch = pattern.match(/^(https?:\/\/)\*\.(.+)$/);
    if (wildcardMatch) {
      const [, scheme, suffix] = wildcardMatch;
      // origin must start with scheme + non-empty subdomain + "." + suffix
      const prefix = scheme;
      if (!origin.startsWith(prefix)) continue;
      const host = origin.slice(prefix.length);
      // Require at least one subdomain label before suffix
      const dotSuffix = `.${suffix}`;
      if (host.endsWith(dotSuffix) && host.length > dotSuffix.length) {
        return true;
      }
    }
  }
  return false;
}

/**
 * Generates a stable, unique correlation id for a dirty-check round trip.
 * Falls back to `Math.random()` + counter when `crypto.randomUUID` is
 * unavailable (e.g. older jsdom, IE-edge cases).
 */
let _correlationCounter = 0;
function generateCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  _correlationCounter += 1;
  return `rnms-${Date.now()}-${_correlationCounter}-${Math.floor(Math.random() * 1e9)}`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecordNavigationModalShell: React.FC<IRecordNavigationModalShellProps> = ({
  currentIndex,
  navigationTotal,
  onNavigate,
  title,
  actionBar,
  children,
  dirtyCheckTargetWindow,
  dirtyCheckTargetOrigin = '*',
  allowedOrigins,
  dirtyCheckTimeout = 1000,
  onDirtyDiscard,
  className,
  'data-testid': testId,
}) => {
  const styles = useRecordNavigationModalShellStyles();

  // ---------------------------------------------------------------------
  // Discard-confirmation dialog state
  // ---------------------------------------------------------------------

  const [pendingDirection, setPendingDirection] = React.useState<RecordNavigationDirection | null>(null);
  const discardDialogOpen = pendingDirection !== null;

  // ---------------------------------------------------------------------
  // Disabled-state derivation (FR-12)
  // ---------------------------------------------------------------------

  const prevDisabled = currentIndex <= 0;
  const nextDisabled = currentIndex >= navigationTotal - 1;

  // ---------------------------------------------------------------------
  // Resolved origin allow-list (memoized; includes window.location.origin)
  // ---------------------------------------------------------------------

  const resolvedAllowedOrigins = React.useMemo<ReadonlyArray<string>>(() => {
    const explicit = allowedOrigins ?? DEFAULT_ALLOWED_ORIGIN_PATTERNS;
    if (typeof window !== 'undefined' && window.location && window.location.origin) {
      return Object.freeze([...explicit, window.location.origin]);
    }
    return explicit;
  }, [allowedOrigins]);

  // ---------------------------------------------------------------------
  // Dirty-check round trip + nav invocation
  // ---------------------------------------------------------------------

  /**
   * Posts a `request-dirty-check` to the iframe and resolves with the
   * iframe's reported dirty state. Falls back to `false` (clean) when:
   *   - no `dirtyCheckTargetWindow` is supplied (caller opted out)
   *   - the iframe does not respond within `dirtyCheckTimeout`
   *   - an untrusted-origin response arrives (treated as no response)
   */
  const queryDirtyState = React.useCallback((): Promise<boolean> => {
    const targetWindow = dirtyCheckTargetWindow;
    if (!targetWindow) {
      return Promise.resolve(false);
    }

    return new Promise<boolean>(resolve => {
      const correlationId = generateCorrelationId();
      let settled = false;

      const handleMessage = (ev: MessageEvent): void => {
        if (settled) return;
        // Validate inbound origin against the allow-list.
        if (!isOriginAllowed(ev.origin, resolvedAllowedOrigins)) return;
        // Validate payload shape.
        const data = ev.data as Partial<IDirtyCheckResponse> | null;
        if (!data || typeof data !== 'object') return;
        if (data.type !== DIRTY_CHECK_RESULT_TYPE) return;
        if (data.correlationId !== correlationId) return;
        settled = true;
        window.removeEventListener('message', handleMessage);
        clearTimeout(timeoutId);
        resolve(Boolean(data.dirty));
      };

      const timeoutId = setTimeout(() => {
        if (settled) return;
        settled = true;
        window.removeEventListener('message', handleMessage);
        // Timeout — treat as clean per spec FR-14 fallback semantics.
        resolve(false);
      }, dirtyCheckTimeout);

      window.addEventListener('message', handleMessage);

      const request: IDirtyCheckRequest = {
        type: DIRTY_CHECK_REQUEST_TYPE,
        correlationId,
      };
      try {
        targetWindow.postMessage(request, dirtyCheckTargetOrigin);
      } catch {
        // postMessage can throw if the target window is cross-origin and
        // detached (e.g. iframe was just unmounted). Treat as clean.
        if (!settled) {
          settled = true;
          window.removeEventListener('message', handleMessage);
          clearTimeout(timeoutId);
          resolve(false);
        }
      }
    });
  }, [dirtyCheckTargetWindow, dirtyCheckTargetOrigin, dirtyCheckTimeout, resolvedAllowedOrigins]);

  /**
   * Common path for both prev and next:
   *   1. Run dirty-check (or skip when no target window).
   *   2. If dirty, surface the confirm dialog (state machine: `pendingDirection`).
   *   3. If clean, immediately invoke `onNavigate`.
   */
  const attemptNavigate = React.useCallback(
    async (direction: RecordNavigationDirection): Promise<void> => {
      const dirty = await queryDirtyState();
      if (dirty) {
        setPendingDirection(direction);
        return;
      }
      await onNavigate(direction);
    },
    [queryDirtyState, onNavigate]
  );

  const handlePrev = React.useCallback(() => {
    if (prevDisabled) return;
    void attemptNavigate('prev');
  }, [prevDisabled, attemptNavigate]);

  const handleNext = React.useCallback(() => {
    if (nextDisabled) return;
    void attemptNavigate('next');
  }, [nextDisabled, attemptNavigate]);

  // ---------------------------------------------------------------------
  // Discard-dialog handlers
  // ---------------------------------------------------------------------

  const handleDiscardConfirm = React.useCallback(async () => {
    const direction = pendingDirection;
    setPendingDirection(null);
    if (direction === null) return;
    onDirtyDiscard?.();
    await onNavigate(direction);
  }, [pendingDirection, onDirtyDiscard, onNavigate]);

  const handleDiscardCancel = React.useCallback(() => {
    setPendingDirection(null);
  }, []);

  // ---------------------------------------------------------------------
  // Counter string ("N of M") — defensive when total is 0
  // ---------------------------------------------------------------------

  const counterText = navigationTotal > 0 ? `${currentIndex + 1} of ${navigationTotal}` : `0 of 0`;

  // ---------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------

  return (
    <div className={mergeClasses(styles.root, className)} data-testid={testId}>
      {/* Header — title + nav + action bar slot */}
      <div className={styles.header}>
        <Text as="h2" className={styles.title} size={400} title={title}>
          {title}
        </Text>

        <div className={styles.headerActions} aria-label="Record navigation actions">
          <div className={styles.navGroup} role="group" aria-label="Record navigation">
            <Tooltip content="Previous record" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<ChevronLeft20Regular />}
                aria-label="Previous record"
                disabled={prevDisabled}
                onClick={handlePrev}
              />
            </Tooltip>
            <Text
              size={200}
              className={styles.navCounter}
              aria-live="polite"
              aria-label={`Record ${currentIndex + 1} of ${navigationTotal}`}
            >
              {counterText}
            </Text>
            <Tooltip content="Next record" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<ChevronRight20Regular />}
                aria-label="Next record"
                disabled={nextDisabled}
                onClick={handleNext}
              />
            </Tooltip>
          </div>

          {actionBar && (
            <>
              <Divider vertical className={styles.navDivider} />
              {actionBar}
            </>
          )}
        </div>
      </div>

      {/* Content slot — typically the iframe pointing at the OOB MDA form */}
      <div className={styles.content}>{children}</div>

      {/* Discard-confirmation dialog — only mounted when prompt is active */}
      <Dialog
        open={discardDialogOpen}
        onOpenChange={(_, data) => {
          if (!data.open) handleDiscardCancel();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Discard unsaved changes?</DialogTitle>
            <DialogContent>
              <Text>This record has unsaved changes. If you navigate away, those changes will be lost.</Text>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={handleDiscardCancel}>
                Cancel
              </Button>
              <Button appearance="primary" onClick={() => void handleDiscardConfirm()}>
                Discard and continue
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

RecordNavigationModalShell.displayName = 'RecordNavigationModalShell';

export default RecordNavigationModalShell;
