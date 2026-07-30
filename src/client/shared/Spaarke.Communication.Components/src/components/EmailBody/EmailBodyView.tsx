/**
 * EmailBodyView.tsx
 *
 * Reading-pane BODY sub-view (email-communication-solution-r5 task 033,
 * FR-06/FR-19, NFR-02/NFR-03; design Lens 2/6). Fills the task-032
 * `EmailReadingPaneShell` `renderBody(selectedId)` slot. Renders the selected
 * email "as sent":
 *
 *   - `.eml` branch: `authenticatedFetch('GET /api/documents/{emlDocumentId}/
 *     eml-render')` → render the returned SERVER-sanitized HTML in a SANDBOXED
 *     iframe (`sandbox=""` — NO `allow-scripts`, NO `allow-same-origin`). This
 *     iframe is defense-in-depth (NFR-03): mandatory even though the task-010
 *     endpoint already sanitizes server-side. The `.eml` HTML is NEVER rendered
 *     via `dangerouslySetInnerHTML`.
 *   - degradation branch: no archive doc id OR a non-2xx/failed render ⇒ render
 *     `sanitizeEmailHtml(body)` (CLIENT-sanitized, task 001) + a "full history
 *     unavailable" note. Archive-less is a NORMAL state — no error banner.
 *   - loading: a body skeleton while the render is in flight (the shell already
 *     painted the header from the record first — NFR-02; the body never blocks
 *     the header).
 *   - error: ONLY for a host record-LOAD failure (`recordLoadError`) — with a
 *     retry affordance. An archive-less email is degradation, not error.
 *
 * Auth (ADR-028): `authenticatedFetch` from `@spaarke/auth` (host initialises
 * the provider via `initAuth()` at app root). Fluent v9 + dark mode via the host
 * `FluentProvider` (ADR-021) — no hardcoded colors on the chrome (the sandboxed
 * `.eml` body reflects the sender's own styling by design). React-version-safe
 * (ADR-022 / NFR-05): `React.FC` + standard hooks, no `as React.ComponentType`.
 */
import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Skeleton,
  SkeletonItem,
  Tooltip,
} from '@fluentui/react-components';
import { ErrorCircle24Regular, ArrowClockwise16Regular, Info16Regular } from '@fluentui/react-icons';
import { sanitizeEmailHtml } from '@spaarke/ui-components';
import { authenticatedFetch as defaultAuthenticatedFetch } from '@spaarke/auth';
import type { EmailBodyViewProps } from './EmailBodyView.types';

const useStyles = makeStyles({
  root: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    minHeight: 0,
    width: '100%',
  },
  // Unobtrusive "full history unavailable" affordance — rendered at the VERY END
  // of the body as a small (i) centered on a faint divider line (owner UAT).
  // Replaces the old top banner that read as a warning ("distracting").
  fallbackFooter: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    // Extra separation between the message text and the end-of-message note (owner UAT).
    marginTop: tokens.spacingVerticalXXXL,
    paddingInline: tokens.spacingHorizontalXL,
    paddingBottom: tokens.spacingVerticalL,
  },
  footerLine: { flex: '1 1 auto', height: '1px', backgroundColor: tokens.colorNeutralStroke2 },
  fallbackInfo: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    background: 'none',
    border: 'none',
    padding: tokens.spacingHorizontalXXS,
    cursor: 'help',
    ':hover': { color: tokens.colorNeutralForeground2 },
  },
  // The sandboxed iframe fills the pane. A `sandbox=""` iframe cannot be
  // measured from the parent (no `allow-same-origin`), so it cannot be
  // auto-sized to its content height without relaxing the sandbox (forbidden,
  // NFR-03). It therefore fills the available space and scrolls internally.
  iframe: {
    flex: '1 1 auto',
    width: '100%',
    minHeight: '360px',
    border: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Info note styled from semantic tokens (not `MessageBar`, whose reflow hook
  // needs a `ResizeObserver` jsdom lacks) — themes correctly in light + dark.
  note: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    margin: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
  noteIcon: {
    flexShrink: 0,
    marginTop: '2px',
    color: tokens.colorNeutralForeground3,
  },
  noteText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  noteTitle: {
    fontWeight: tokens.fontWeightSemibold,
  },
  fallbackBody: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    color: tokens.colorNeutralForeground1,
    overflowWrap: 'anywhere',
    wordBreak: 'break-word',
  },
  skeleton: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
  },
  skeletonLine: {
    height: '16px',
  },
  error: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    flex: '1 1 auto',
    minHeight: '240px',
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground2,
  },
  errorIcon: {
    color: tokens.colorStatusDangerForeground1,
  },
});

/** Internal render phase — one of loading / eml / fallback / record-error. */
type Phase =
  | { kind: 'loading' }
  | { kind: 'eml'; html: string }
  | { kind: 'fallback' }
  | { kind: 'record-error' };

/**
 * Build the relative `eml-render` path. `authenticatedFetch` prefixes `/api`
 * and resolves against the configured BFF base URL internally (see
 * `@spaarke/auth` `authenticatedFetch` → `buildBffApiUrl`), so we pass a
 * relative path only — never a hand-concatenated absolute URL (ADR-028).
 */
function emlRenderPath(emlDocumentId: string): string {
  return `/documents/${encodeURIComponent(emlDocumentId)}/eml-render`;
}

export const EmailBodyView: React.FC<EmailBodyViewProps> = ({
  selectedId,
  emlDocumentId,
  body,
  recordLoadError = false,
  onRetryRecord,
  authenticatedFetch = defaultAuthenticatedFetch,
}) => {
  const s = useStyles();

  const hasArchive = typeof emlDocumentId === 'string' && emlDocumentId.length > 0;

  // Initial phase (also the value used when no async work is needed): a record
  // load-failure is an error; an archive-less record degrades immediately; only
  // a present archive id enters the loading→fetch cycle.
  const initialPhase = React.useCallback((): Phase => {
    if (recordLoadError) return { kind: 'record-error' };
    if (!hasArchive) return { kind: 'fallback' };
    return { kind: 'loading' };
  }, [recordLoadError, hasArchive]);

  const [phase, setPhase] = React.useState<Phase>(initialPhase);

  React.useEffect(() => {
    // Record-load failure short-circuits every other branch (host couldn't load
    // the record at all — nothing to render but the retry affordance).
    if (recordLoadError) {
      setPhase({ kind: 'record-error' });
      return;
    }

    // No archive document ⇒ normal degradation to `sprk_body`. No fetch, no error.
    if (!hasArchive) {
      setPhase({ kind: 'fallback' });
      return;
    }

    // `.eml` archive present ⇒ fetch the server-rendered sanitized HTML.
    // Any failure (non-2xx → `authenticatedFetch` throws ApiError; network/other
    // → rejects) FAILS SOFT to the `sprk_body` degradation branch, never a crash.
    let cancelled = false;
    setPhase({ kind: 'loading' });

    void (async () => {
      try {
        const res = await authenticatedFetch(emlRenderPath(emlDocumentId as string));
        // `authenticatedFetch` only returns on `res.ok`; guard defensively anyway.
        if (!res.ok) {
          if (!cancelled) setPhase({ kind: 'fallback' });
          return;
        }
        const html = await res.text();
        if (!cancelled) setPhase({ kind: 'eml', html });
      } catch {
        if (!cancelled) setPhase({ kind: 'fallback' });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [selectedId, emlDocumentId, hasArchive, recordLoadError, authenticatedFetch]);

  if (phase.kind === 'record-error') {
    return (
      <div className={s.root}>
        <div className={s.error} role="alert" data-testid="email-body-error">
          <ErrorCircle24Regular className={s.errorIcon} aria-hidden="true" />
          <Text>This email couldn&apos;t be loaded.</Text>
          {onRetryRecord ? (
            <Button
              appearance="secondary"
              icon={<ArrowClockwise16Regular />}
              onClick={onRetryRecord}
            >
              Retry
            </Button>
          ) : null}
        </div>
      </div>
    );
  }

  if (phase.kind === 'loading') {
    return (
      <div className={s.root}>
        <Skeleton
          className={s.skeleton}
          aria-label="Loading email"
          role="status"
          data-testid="email-body-loading"
        >
          <SkeletonItem className={s.skeletonLine} style={{ width: '90%' }} />
          <SkeletonItem className={s.skeletonLine} style={{ width: '75%' }} />
          <SkeletonItem className={s.skeletonLine} style={{ width: '82%' }} />
          <SkeletonItem className={s.skeletonLine} style={{ width: '60%' }} />
        </Skeleton>
      </div>
    );
  }

  if (phase.kind === 'eml') {
    // Defense-in-depth (NFR-03): the server already sanitized this HTML, but we
    // STILL render it inside a `sandbox=""` iframe — NO `allow-scripts` and NO
    // `allow-same-origin`. So even a sanitizer-bypass payload cannot execute
    // script or reach the parent origin. `srcDoc` injects the document; inline
    // images already resolved to `data:` URIs server-side.
    return (
      <div className={s.root}>
        <iframe
          className={s.iframe}
          title="Email message"
          sandbox=""
          referrerPolicy="no-referrer"
          srcDoc={phase.html}
          data-testid="email-body-iframe"
        />
      </div>
    );
  }

  // phase.kind === 'fallback' — client-sanitized `sprk_body` + "unavailable" note.
  const safeHtml = sanitizeEmailHtml(body ?? '');
  return (
    <div className={s.root} data-testid="email-body-fallback">
      <div
        className={s.fallbackBody}
        data-testid="email-body-fallback-content"
        // eslint-disable-next-line react/no-danger -- content is client-sanitized via the hardened shared `sanitizeEmailHtml` (task 001, NFR-03) immediately above.
        dangerouslySetInnerHTML={{ __html: safeHtml }}
      />
      {/* End-of-message (i) on a faint divider line — the least-intrusive place
          to note the archived copy is unavailable (owner UAT). */}
      <div className={s.fallbackFooter}>
        <span className={s.footerLine} aria-hidden="true" />
        <Tooltip
          relationship="description"
          content="Full history unavailable — showing the latest message only. The archived copy of this email isn't available, so quoted replies and inline images may be missing."
        >
          <button
            type="button"
            className={s.fallbackInfo}
            data-testid="email-body-fallback-note"
            aria-label="Full history unavailable"
          >
            <Info16Regular aria-hidden="true" />
          </button>
        </Tooltip>
        <span className={s.footerLine} aria-hidden="true" />
      </div>
    </div>
  );
};

EmailBodyView.displayName = 'EmailBodyView';
