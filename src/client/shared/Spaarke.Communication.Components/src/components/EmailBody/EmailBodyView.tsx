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
import { makeStyles, tokens, Text, Button, Link, Skeleton, SkeletonItem, Tooltip } from '@fluentui/react-components';
import {
  ErrorCircle24Regular,
  ArrowClockwise16Regular,
  Info16Regular,
  DocumentText16Regular,
  Open16Regular,
} from '@fluentui/react-icons';
import { sanitizeEmailHtml } from '@spaarke/ui-components';
import { authenticatedFetch as defaultAuthenticatedFetch } from '@spaarke/auth';
import { buildReaderReferenceMap, resolveQuotedCitation, type QuotedCitationResolution } from '../../logic/citations';
import { HighlightedText } from './HighlightedText';
import type { EmailBodyViewProps, ReconciliationAttachmentContent } from './EmailBodyView.types';

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
  // Attachment-text fold (task 053, NFR-11) — each attachment's extracted text
  // rendered as readable normalized text BELOW the body so body + attachment
  // text read as one continuous surface. Semantic tokens only (ADR-021).
  folds: {
    display: 'flex',
    flexDirection: 'column',
    flexShrink: 0,
    paddingInline: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalL,
  },
  fold: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalM,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  foldHead: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    justifyContent: 'space-between',
  },
  foldName: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  foldIcon: { flexShrink: 0, color: tokens.colorNeutralForeground3 },
  // The extracted text itself — the normalized surface task 054 anchors into.
  // `pre-wrap` preserves the extraction's own line breaks; wraps long lines.
  foldText: {
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    wordBreak: 'break-word',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  foldUnavailable: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: 'italic',
  },
  openOriginalLink: { flexShrink: 0 },
  // Citation anchor affordances (task 054, NFR-11) — the body "cited passage"
  // callout + the "source not locatable" note. Semantic tokens only (ADR-021).
  citationBanner: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    margin: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    borderLeftWidth: tokens.strokeWidthThick,
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorBrandStroke1,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  citationBannerLabel: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  notLocatable: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    margin: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  notLocatableIcon: { flexShrink: 0, color: tokens.colorStatusWarningForeground1 },
});

/** The active fold-highlight target passed to {@link FoldedAttachments} when a citation resolves into an attachment. */
interface ActiveFoldHighlight {
  attachmentId: string;
  /** The NORMALIZED segment text the span offsets index into (so the highlight aligns exactly). */
  text: string;
  start: number;
  end: number;
}

/**
 * Renders each attachment's extracted content folded into the reader as
 * readable normalized text (task 053, NFR-11). An attachment that could not be
 * extracted (`extractable === false` with no `text`) renders a non-fatal
 * "content not available as text" note instead of its text — open-original
 * stays available (the negative acceptance case). Attachments with a
 * `documentId` render an "Open original" link that fires `onOpenOriginal`.
 */
const FoldedAttachments: React.FC<{
  attachments: ReadonlyArray<ReconciliationAttachmentContent>;
  onOpenOriginal?: (attachment: ReconciliationAttachmentContent) => void;
  activeFold?: ActiveFoldHighlight;
}> = ({ attachments, onOpenOriginal, activeFold }) => {
  const s = useStyles();
  if (attachments.length === 0) return null;
  return (
    <div className={s.folds} data-testid="email-body-attachment-folds">
      {attachments.map(att => {
        const hasText = typeof att.text === 'string' && att.text.length > 0;
        const unextractable = !hasText && att.extractable === false;
        const canOpen = typeof att.documentId === 'string' && att.documentId.length > 0;
        const highlight = activeFold && activeFold.attachmentId === att.attachmentId ? activeFold : undefined;
        return (
          <section key={att.attachmentId} className={s.fold} data-testid="email-body-attachment-fold">
            <div className={s.foldHead}>
              <span className={s.foldName} title={att.name}>
                <DocumentText16Regular className={s.foldIcon} aria-hidden="true" />
                {att.name}
              </span>
              {canOpen && onOpenOriginal ? (
                <Link
                  as="button"
                  appearance="subtle"
                  className={s.openOriginalLink}
                  onClick={() => onOpenOriginal(att)}
                  data-testid="email-body-attachment-open-original"
                >
                  <Open16Regular aria-hidden="true" /> Open original
                </Link>
              ) : null}
            </div>
            {hasText && highlight ? (
              // Active citation resolves into THIS attachment — render its
              // normalized text with the exact cited span highlighted + scrolled.
              <div className={s.foldText} data-testid="email-body-attachment-text">
                <HighlightedText text={highlight.text} start={highlight.start} end={highlight.end} />
              </div>
            ) : hasText ? (
              <div className={s.foldText} data-testid="email-body-attachment-text">
                {att.text}
              </div>
            ) : unextractable ? (
              <div className={s.foldUnavailable} data-testid="email-body-attachment-unavailable">
                <Info16Regular aria-hidden="true" />
                <span>Content not available as text — open the original to view it.</span>
              </div>
            ) : null}
          </section>
        );
      })}
    </div>
  );
};
FoldedAttachments.displayName = 'FoldedAttachments';

/** Internal render phase — one of loading / eml / fallback / record-error. */
type Phase = { kind: 'loading' } | { kind: 'eml'; html: string } | { kind: 'fallback' } | { kind: 'record-error' };

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
  attachments,
  onOpenOriginal,
  activeCitation,
}) => {
  const s = useStyles();

  // Resolve the active proposal citation over the reader's normalized text
  // (body + folded attachment text) — the same surface the AI extraction ran
  // over (task 054, NFR-11). Pure, memoized; never throws. `resolution` is null
  // when no citation is active.
  const referenceMap = React.useMemo(() => buildReaderReferenceMap({ body, attachments }), [body, attachments]);
  const resolution = React.useMemo<QuotedCitationResolution | null>(
    () => (activeCitation ? resolveQuotedCitation(activeCitation, referenceMap) : null),
    [activeCitation, referenceMap]
  );

  // When the citation resolves into an ATTACHMENT, hand the fold its exact span
  // (over the segment's normalized text) so it highlights inline.
  const activeFold = React.useMemo<ActiveFoldHighlight | undefined>(() => {
    if (!resolution || !resolution.located || resolution.kind !== 'attachment') return undefined;
    const seg = referenceMap.segments.find(sg => sg.segmentId === resolution.segmentId);
    if (!seg) return undefined;
    return { attachmentId: resolution.segmentId, text: seg.text, start: resolution.start, end: resolution.end };
  }, [resolution, referenceMap]);

  // The attachment-text fold — appended below the body in every content phase
  // (eml + fallback) so body + attachment text form one continuous reader
  // surface (task 053, NFR-11). Not rendered in the loading/record-error states.
  const folds =
    attachments && attachments.length > 0 ? (
      <FoldedAttachments attachments={attachments} onOpenOriginal={onOpenOriginal} activeFold={activeFold} />
    ) : null;

  // The citation banner rendered ABOVE the body (task 054): a "cited passage"
  // callout when the quote resolves into the BODY (the `.eml` iframe can't be
  // highlighted in place, NFR-03 — so the exact passage is shown here), or a
  // "source not locatable" note when a forged/absent quote can't be anchored
  // (never a silent mis-navigation).
  const citationBanner = (() => {
    if (!resolution) return null;
    if (!resolution.located) {
      return (
        <div className={s.notLocatable} role="status" data-testid="email-body-citation-not-locatable">
          <Info16Regular className={s.notLocatableIcon} aria-hidden="true" />
          <span>
            Source not locatable — the cited text wasn&apos;t found in this email. Open the original to verify.
          </span>
        </div>
      );
    }
    if (resolution.kind === 'body') {
      const seg = referenceMap.segments.find(sg => sg.segmentId === resolution.segmentId);
      if (!seg) return null;
      return (
        <div className={s.citationBanner} role="note" data-testid="email-body-citation-banner">
          <span className={s.citationBannerLabel}>
            <DocumentText16Regular aria-hidden="true" /> Cited passage
          </span>
          <HighlightedText text={seg.text} start={resolution.start} end={resolution.end} />
        </div>
      );
    }
    return null; // attachment-sourced → highlighted inline in its fold (activeFold).
  })();

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
            <Button appearance="secondary" icon={<ArrowClockwise16Regular />} onClick={onRetryRecord}>
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
        <Skeleton className={s.skeleton} aria-label="Loading email" role="status" data-testid="email-body-loading">
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
        {citationBanner}
        <iframe
          className={s.iframe}
          title="Email message"
          sandbox=""
          referrerPolicy="no-referrer"
          srcDoc={phase.html}
          data-testid="email-body-iframe"
        />
        {folds}
      </div>
    );
  }

  // phase.kind === 'fallback' — client-sanitized `sprk_body` + "unavailable" note.
  const safeHtml = sanitizeEmailHtml(body ?? '');
  return (
    <div className={s.root} data-testid="email-body-fallback">
      {citationBanner}
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
      {folds}
    </div>
  );
};

EmailBodyView.displayName = 'EmailBodyView';
