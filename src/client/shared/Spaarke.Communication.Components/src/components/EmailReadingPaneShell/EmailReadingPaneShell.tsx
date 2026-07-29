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
 * This is the COMPOSITION ROOT for the P3b wave — it does NOT implement the
 * body/header/attachments/connections/tracking sub-views. Those are supplied
 * by the host as `render*` slot props (tasks 033/034/035/036 fill them in
 * without editing this file — the slot contract this task establishes; see
 * `EmailReadingPaneShell.types.ts`). The full-width `<EmailToolbar/>` spans
 * the reading-pane width and dispatches Reply/Reply All/Forward/New/Archive/
 * Create through the host-supplied `actions` handlers — the shell never
 * re-implements action-bar/compose logic itself (task 022's extracted
 * `logic/actions` is the canonical source; task 036 owns the real dispatch).
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
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { Mail24Regular } from '@fluentui/react-icons';
import { PanelSplitter, useTwoPanelLayout } from '@spaarke/ui-components';
import { EmailCardList } from '../EmailCardList';
import { EmailToolbar } from './EmailToolbar';
import type { EmailReadingPaneShellProps } from './EmailReadingPaneShell.types';

/** Stable default persistence key — the reading-pane's splitter width is shared across sessions for every host that doesn't override it. */
const DEFAULT_STORAGE_KEY = 'sprk-email-reading-pane-splitter';
const DEFAULT_READING_PANE_WIDTH_PX = 480;
const DEFAULT_MIN_LIST_WIDTH_PX = 280;
const DEFAULT_MIN_READING_PANE_WIDTH_PX = 360;

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
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflow: 'hidden',
  },
  readingPaneScroll: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
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

export const EmailReadingPaneShell: React.FC<EmailReadingPaneShellProps> = ({
  items,
  isLoading = false,
  initialSelectedId,
  onSelectedIdChange,
  actions,
  renderHeader,
  renderBody,
  renderAttachments,
  renderConnections,
  renderTracking,
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

      <div className={s.readingPane} style={{ width: detailWidth }} data-testid="email-reading-pane">
        {selectedId ? (
          <React.Fragment key={selectedId}>
            <EmailToolbar selectedId={selectedId} actions={actions} />
            <div className={s.readingPaneScroll}>
              {renderHeader?.(selectedId)}
              {renderBody?.(selectedId)}
              {renderAttachments?.(selectedId)}
              {renderConnections?.(selectedId)}
              {renderTracking?.(selectedId)}
            </div>
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

EmailReadingPaneShell.displayName = 'EmailReadingPaneShell';
