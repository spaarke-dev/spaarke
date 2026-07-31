/**
 * ComposeExternalChangeBanner.tsx — external-change refresh banner (FR-07 / gap G8, task 030).
 *
 * Project: spaarkeai-compose-r5, task 030 (Phase 3 Concurrency + UX).
 *
 * Renders the non-blocking banner spec FR-07 requires when Compose detects that the underlying
 * document changed in the document-management system (an external Office edit landed a new SPE
 * version), with the FIXED wording:
 *
 *   "Document updated from document management system version"
 *
 * Two shapes, driven by whether the editor holds UNSAVED local edits (NFR-08 — never silently drop
 * a user's work):
 *   - CLEAN editor → the parent has already transparently remounted the projection from the
 *     server-authoritative bytes; this banner is a quiet informational confirmation (dismissable).
 *   - DIRTY editor → the parent did NOT remount (that would discard unsaved edits). The banner
 *     surfaces a "Reload" action so the user CHOOSES to discard-and-remount (an explicit action,
 *     not a silent loss). Keeping editing is always allowed — the banner is non-blocking.
 *
 * Presentational only — the parent (ComposeWorkspace) owns the detected-change state + the reload/
 * dismiss callbacks. Mirrors ComposeReanchorBanner's pure-render composition + Fluent MessageBar
 * pattern exactly.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: ComposeReanchorBanner reports a return-from-Word RE-ANCHOR summary (per-band anchor
 *     counts + "Review changes"); ComposeBannerStack renders CRUD/lifecycle status rows. Neither
 *     models "the document changed externally — reload it" (a distinct payload + a distinct reload
 *     action gated on local dirty state).
 *   - Extension: a new row could be added to ComposeBannerStack, but this carries a distinct action
 *     (reload/remount) and a distinct dirty-gated shape; a focused sibling (like ComposeReanchorBanner)
 *     avoids widening BannerStack's prop surface. A follow-on MAY compose it INTO the stack.
 *   - Cost-of-doing-nothing: FR-07's "banner reports the external change + offers a refresh" fails;
 *     an external Office edit either silently diverges from what the user sees or (worse) a blind
 *     remount discards their unsaved edits.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 semantic tokens only — no hex literals; correct in light AND dark theme.
 *   - ADR-022: React 19 pure functional component.
 *   - NFR-08: a dirty editor is NEVER auto-remounted here; the reload is an explicit user action.
 *
 * @see ./ComposeReanchorBanner.tsx (the sibling this mirrors)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Button,
} from '@fluentui/react-components';

/** The fixed banner wording spec FR-07 mandates (asserted verbatim in tests). */
export const EXTERNAL_CHANGE_BANNER_TEXT = 'Document updated from document management system version';

export interface ComposeExternalChangeBannerProps {
  /** Whether an external change has been detected + not yet dismissed/reloaded. Banner renders only when true. */
  pending: boolean;
  /**
   * Whether the editor holds UNSAVED local edits. When true the parent did NOT auto-remount (NFR-08),
   * so the banner offers an explicit "Reload" action (discard-and-remount is the user's choice). When
   * false the parent already remounted transparently; the banner is a quiet informational confirmation.
   */
  hasUnsavedEdits: boolean;
  /** Reload = discard local edits + remount the projection from server bytes. Shown only when dirty. */
  onReload: () => void;
  /** Dismiss the banner. Optional — when omitted the dismiss affordance is not rendered. */
  onDismiss?: () => void;
}

const useStyles = makeStyles({
  root: {
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
});

export function ComposeExternalChangeBanner(props: ComposeExternalChangeBannerProps): React.JSX.Element | null {
  const { pending, hasUnsavedEdits, onReload, onDismiss } = props;
  const styles = useStyles();

  if (!pending) {
    return null;
  }

  // Dirty → warn + offer explicit reload (the remount was deferred to protect unsaved edits).
  // Clean → info (the parent already remounted transparently).
  const intent = hasUnsavedEdits ? 'warning' : 'info';

  return (
    <div className={styles.root}>
      <MessageBar intent={intent} data-testid="compose-external-change-banner" aria-live="polite">
        <MessageBarBody>
          <MessageBarTitle>{EXTERNAL_CHANGE_BANNER_TEXT}</MessageBarTitle>
          {hasUnsavedEdits ? (
            <span data-testid="compose-external-change-banner-unsaved">
              You have unsaved edits — reloading will discard them.
            </span>
          ) : null}
        </MessageBarBody>
        <MessageBarActions
          containerAction={
            onDismiss ? (
              <Button
                appearance="transparent"
                size="small"
                onClick={onDismiss}
                aria-label="Dismiss document-updated banner"
                data-testid="compose-external-change-banner-dismiss"
              >
                Dismiss
              </Button>
            ) : undefined
          }
        >
          {hasUnsavedEdits ? (
            <Button
              appearance="primary"
              size="small"
              onClick={onReload}
              data-testid="compose-external-change-banner-reload"
            >
              Reload
            </Button>
          ) : null}
        </MessageBarActions>
      </MessageBar>
    </div>
  );
}

ComposeExternalChangeBanner.displayName = 'ComposeExternalChangeBanner';

export default ComposeExternalChangeBanner;
