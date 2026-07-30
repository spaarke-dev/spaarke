/**
 * EmailReadingPaneShell.tsx
 *
 * The Outlook-style two-pane composition root for the Email surface
 * (email-communication-solution-r5 task 032, FR-05/FR-08/FR-19; design Lens 2
 * BUILD verdict). Renders the reused `PanelSplitter` (+ `useTwoPanelLayout`,
 * both `@spaarke/ui-components` — ADR-012 reuse; this shell does NOT fork a
 * new splitter) with the `EmailCardList` (task 030) on the left and the
 * reading pane on the right. Selecting a card sets the shell's `selectedId`,
 * which drives the right pane; the splitter width persists across sessions
 * via `localStorage` (the hook's own persistence, keyed by `storageKey`).
 *
 * This is the COMPOSITION ROOT for the reading pane — it does NOT implement
 * the association/title-bar/body/recipients/attachments/related-to sub-views.
 * Those are supplied by the host as three `render*` slot props, top → bottom:
 * `renderTop` (the Association section — rendered at the VERY TOP so its
 * status dot is the first thing seen), `renderHeader` (the TITLE BAR: the
 * email subject on its own light-gray row) and `renderBody` (the composed
 * recipients + collapsible Attachments/Related-to sections + body region,
 * rendered BELOW the toolbar) — see `EmailReadingPaneShell.types.ts` for the
 * full slot contract. The full-width `<EmailToolbar/>` spans the reading-pane width and
 * dispatches Reply/Reply All/Forward/New plus the icon-only Save-to-SharePoint/
 * Create-Event/Create-To-Do/Link-Invoice/Open-full-form actions through the
 * host-supplied `actions` handlers — the shell never re-implements action-bar/
 * compose logic itself (the host wires the real dispatch).
 *
 * When nothing is selected, the right pane shows the "Select an email"
 * placeholder (FR-19 empty state).
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. This is
 * a Layer-2 (React 19 code-page) composition; it is not shared across the PCF
 * boundary. Fluent v9 semantic tokens only (ADR-021) — themes correctly via
 * the host `FluentProvider` in both light and dark mode.
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Button } from '@fluentui/react-components';
import { Mail24Regular, ArrowDown20Regular } from '@fluentui/react-icons';
import { PanelSplitter, useTwoPanelLayout } from '@spaarke/ui-components';
import { EmailCardList } from '../EmailCardList';
import { EmailToolbar } from './EmailToolbar';
import type { EmailReadingPaneShellProps } from './EmailReadingPaneShell.types';

/** Stable default persistence key — the reading-pane's splitter width is shared across sessions for every host that doesn't override it. */
const DEFAULT_STORAGE_KEY = 'sprk-email-reading-pane-splitter';
const DEFAULT_READING_PANE_WIDTH_PX = 480;
const DEFAULT_MIN_LIST_WIDTH_PX = 280;
const DEFAULT_MIN_READING_PANE_WIDTH_PX = 360;
/**
 * Default split ratio (owner UAT): the reading (detail) pane fills 80% of the
 * container, leaving a 20% email list — an Outlook-style reading-first layout.
 * The reused `useTwoPanelLayout` hook (a different package — not forked here)
 * only accepts a PIXEL default, so the outer `EmailReadingPaneShell` wrapper
 * measures the actual container width once and derives the 80% pixel default it
 * hands to the inner shell. Hosts where the container can't be measured (e.g.
 * jsdom under test) fall back to `defaultReadingPaneWidth`. The hook clamps to
 * both panes' minimum widths, so the two panes always sum to 100% (no overflow).
 */
const READING_PANE_DEFAULT_FRACTION = 0.8;

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    width: '100%',
    height: '100%',
    minHeight: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  listPane: {
    overflow: 'hidden',
    flexShrink: 0,
    height: '100%',
  },
  readingPane: {
    // `position: relative` anchors the circular scroll-down FAB (bottom-center).
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflow: 'hidden',
  },
  readingPaneScroll: {
    flex: '1 1 auto',
    minWidth: 0,
    minHeight: 0,
    overflowY: 'auto',
    // Never scroll the reading pane horizontally at standard resolution — wide
    // children (recipients, cards, body) wrap/ellipsis; anything else is clipped.
    overflowX: 'hidden',
    display: 'flex',
    flexDirection: 'column',
    // Hide the native vertical scrollbar — the circular ↓ FAB is the scroll
    // affordance (owner UAT; mirrors the Compose editor). Wheel/trackpad/keyboard
    // scrolling still work.
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': { display: 'none' },
  },
  // Circular "more below" scroll cue — appears only when the reading pane has
  // content below the fold; mirrors `ComposeEditor`'s scrollDownFab (§11).
  scrollDownFab: {
    position: 'absolute',
    bottom: tokens.spacingVerticalL,
    left: '50%',
    transform: 'translateX(-50%)',
    zIndex: 2,
    boxShadow: tokens.shadow16,
  },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flexGrow: 1,
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  placeholderIcon: {
    fontSize: '48px',
  },
});

const EmailReadingPaneShellInner: React.FC<EmailReadingPaneShellProps> = ({
  items,
  isLoading = false,
  initialSelectedId,
  onSelectedIdChange,
  actions,
  renderTop,
  renderHeader,
  renderBody,
  hideList = false,
  storageKey = DEFAULT_STORAGE_KEY,
  defaultReadingPaneWidth = DEFAULT_READING_PANE_WIDTH_PX,
  minListWidth = DEFAULT_MIN_LIST_WIDTH_PX,
  minReadingPaneWidth = DEFAULT_MIN_READING_PANE_WIDTH_PX,
}) => {
  const s = useStyles();

  // The shell owns selection state (FR-05) — EmailCardList only emits onSelect.
  const [selectedId, setSelectedId] = React.useState<string | undefined>(initialSelectedId);

  const handleSelect = React.useCallback(
    (id: string) => {
      setSelectedId(id);
      onSelectedIdChange?.(id);
    },
    [onSelectedIdChange]
  );

  // Circular scroll-down cue — visible only while the reading pane has content
  // below the fold (an alternative to the native scrollbar, per owner UAT;
  // mirrors ComposeEditor's FAB). Re-measured on scroll, on size changes
  // (ResizeObserver), on DOM changes (MutationObserver — the .eml body / sections
  // load async), and per selection.
  const readingPaneScrollRef = React.useRef<HTMLDivElement | null>(null);
  const [showScrollDown, setShowScrollDown] = React.useState<boolean>(false);

  React.useEffect(() => {
    const el = readingPaneScrollRef.current;
    if (!el) {
      setShowScrollDown(false);
      return;
    }
    const measure = (): void => {
      const remaining = el.scrollHeight - el.scrollTop - el.clientHeight;
      setShowScrollDown(remaining > 8);
    };
    measure();
    el.addEventListener('scroll', measure, { passive: true });
    let ro: ResizeObserver | undefined;
    let mo: MutationObserver | undefined;
    if (typeof ResizeObserver !== 'undefined') {
      ro = new ResizeObserver(measure);
      ro.observe(el);
    }
    if (typeof MutationObserver !== 'undefined') {
      mo = new MutationObserver(measure);
      mo.observe(el, { childList: true, subtree: true, characterData: true });
    }
    return () => {
      el.removeEventListener('scroll', measure);
      ro?.disconnect();
      mo?.disconnect();
    };
  }, [selectedId]);

  const scrollReadingPaneDown = React.useCallback((): void => {
    const el = readingPaneScrollRef.current;
    if (!el) return;
    el.scrollBy({ top: Math.round(el.clientHeight * 0.8), behavior: 'smooth' });
  }, []);

  // Reused two-panel layout hook (@spaarke/ui-components) — owns drag/keyboard
  // resize + localStorage persistence keyed by `storageKey`, so the splitter
  // width restores on next open without this shell re-implementing persistence.
  const { primaryWidth, detailWidth, splitterHandlers, isDragging, containerRef, currentRatio } = useTwoPanelLayout({
    defaultDetailWidth: defaultReadingPaneWidth,
    minPrimaryWidth: minListWidth,
    minDetailWidth: minReadingPaneWidth,
    storageKey,
  });

  return (
    <div
      className={s.root}
      ref={containerRef as React.RefObject<HTMLDivElement>}
      data-testid="email-reading-pane-shell"
    >
      {!hideList && (
        <>
          <div className={s.listPane} style={{ width: primaryWidth }} data-testid="email-list-pane">
            <EmailCardList items={items} isLoading={isLoading} selectedId={selectedId} onSelect={handleSelect} />
          </div>

          <PanelSplitter
            onMouseDown={splitterHandlers.onMouseDown}
            onKeyDown={splitterHandlers.onKeyDown}
            onDoubleClick={splitterHandlers.onDoubleClick}
            isDragging={isDragging}
            currentRatio={currentRatio}
          />
        </>
      )}

      <div
        className={s.readingPane}
        style={{ width: hideList ? '100%' : detailWidth }}
        data-testid="email-reading-pane"
      >
        {selectedId ? (
          <React.Fragment key={selectedId}>
            {renderTop?.(selectedId)}
            {renderHeader?.(selectedId)}
            <EmailToolbar selectedId={selectedId} actions={actions} />
            <div className={s.readingPaneScroll} ref={readingPaneScrollRef}>
              {renderBody?.(selectedId)}
            </div>
            {showScrollDown && (
              <Button
                appearance="primary"
                shape="circular"
                size="large"
                className={s.scrollDownFab}
                icon={<ArrowDown20Regular />}
                aria-label="Scroll down for more"
                onClick={scrollReadingPaneDown}
                data-testid="email-reading-pane-scroll-down"
              />
            )}
          </React.Fragment>
        ) : (
          <div className={s.placeholder} role="status">
            <Mail24Regular className={s.placeholderIcon} aria-hidden="true" />
            <Text>Select an email</Text>
          </div>
        )}
      </div>
    </div>
  );
};

EmailReadingPaneShellInner.displayName = 'EmailReadingPaneShellInner';

/**
 * Outer wrapper that establishes the 20%/80% default split (owner UAT #2).
 * Because the reused `useTwoPanelLayout` hook only takes a PIXEL default, this
 * measures the real container width once (before the inner shell mounts) and
 * derives the 80% reading-pane pixel default from it, so the ratio holds at any
 * container size. When the container can't be measured (0-width — e.g. jsdom
 * under test), it falls back to the caller's `defaultReadingPaneWidth`.
 */
export const EmailReadingPaneShell: React.FC<EmailReadingPaneShellProps> = props => {
  const { defaultReadingPaneWidth = DEFAULT_READING_PANE_WIDTH_PX } = props;
  const s = useStyles();
  const measureRef = React.useRef<HTMLDivElement | null>(null);
  const [resolvedReadingPaneWidth, setResolvedReadingPaneWidth] = React.useState<number | null>(null);

  React.useLayoutEffect(() => {
    const width = measureRef.current?.getBoundingClientRect().width ?? 0;
    setResolvedReadingPaneWidth(
      width > 0 ? Math.round(width * READING_PANE_DEFAULT_FRACTION) : defaultReadingPaneWidth
    );
  }, [defaultReadingPaneWidth]);

  // Measure pass — a full-size container to size against, replaced synchronously
  // (before paint, via the layout effect) by the real shell below.
  if (resolvedReadingPaneWidth === null) {
    return <div className={s.root} ref={measureRef} aria-hidden="true" />;
  }

  return <EmailReadingPaneShellInner {...props} defaultReadingPaneWidth={resolvedReadingPaneWidth} />;
};

EmailReadingPaneShell.displayName = 'EmailReadingPaneShell';
