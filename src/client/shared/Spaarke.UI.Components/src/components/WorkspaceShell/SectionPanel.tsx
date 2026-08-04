/**
 * SectionPanel — titled bordered section card with optional toolbar, badge count,
 * and collapsible content area.
 *
 * This is the structural wrapper used by WorkspaceGrid for "Get Started",
 * "Quick Summary", "Latest Updates", "My To Do List", and "My Documents" panels.
 *
 * Design requirements:
 *   - Bordered card with rounded corners using Fluent v9 tokens
 *   - Title bar with optional badge count beside the title
 *   - Optional toolbar row below the title (refresh button, dividers, action buttons)
 *   - Optional collapse/expand toggle
 *   - Fluent v9 semantic tokens only — no hard-coded colors
 *   - Dark mode: inherits token values automatically
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */

import * as React from 'react';
import { Text, Badge, Button, makeStyles, shorthands, tokens, mergeClasses } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronUpRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SectionPanelProps {
  /** Section title used for aria-labels and accessibility. */
  title: string;
  /**
   * Optional React node to render in the title area instead of the title text.
   * When provided, renders in place of `<Text>{title}</Text>`.
   * The `title` string is still used for the collapsible button's aria-label.
   */
  titleContent?: React.ReactNode;
  /**
   * When true, suppress the section title bar entirely (round-8.2 per operator).
   * Used by grid-backed sections (Documents, Matters, Projects, Invoices, Work
   * Assignments) that carry the DataGrid's OWN elevated header — the SectionPanel
   * title would be a redundant second header. The `title` string is still used for
   * the collapsible aria-label if `collapsible` is set. When there is nothing else
   * to show in the bar (no toolbar/badge/collapse control), the whole title bar is
   * omitted so no empty header strip remains above the grid.
   */
  hideTitle?: boolean;
  /** Optional badge count shown beside the title. Renders only when > 0. */
  badgeCount?: number;
  /**
   * Optional toolbar content rendered on the right side of the title bar.
   * Typically contains refresh/open/add buttons from the workspace consumer.
   */
  toolbar?: React.ReactNode;
  /** Section body content. */
  children?: React.ReactNode;
  /**
   * When true, the section supports user-initiated collapse/expand.
   * The expand/collapse button appears in the title bar.
   * Default: false (always expanded).
   */
  collapsible?: boolean;
  /**
   * Controlled open state for the panel.
   * Use together with `onOpenChange` for controlled mode.
   * Defaults to `true` when not provided (uncontrolled, always open).
   */
  open?: boolean;
  /** Called when the user toggles the open state. */
  onOpenChange?: (open: boolean) => void;
  /** Additional className applied to the outer card container. */
  className?: string;
  /** Optional inline style applied to the outer card container. */
  style?: React.CSSProperties;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    // ai-spaarke-ai-workspace-UI-r1 iter 2 round 7 (2026-06-09):
    // SectionPanel cards are placed in a CSS grid row (WorkspaceShell.row,
    // gridTemplateColumns: '1fr' / '1fr 1fr' / etc). Grid items default to
    // `min-width: auto` which equals their intrinsic content width — so if
    // an embedded DataGrid renders wide, the card grows to fit the grid
    // instead of constraining to its `1fr` track. Explicit `min-width: 0`
    // breaks that intrinsic-width inflation and lets the track constraint
    // win. Same fix pattern as in DataGrid root/innerCard/gridScroll +
    // DataverseEntityViewWidget root — this completes the chain.
    minWidth: 0,
    // R4-110 height-chain audit (2026-06-23): the card does NOT need
    // `height: 100%` because it is a direct grid item of `WorkspaceShell.row`
    // which has `alignItems: stretch` (the CSS grid default). The row's
    // stretch alignment sizes the card to the full row track height.
    //
    // The UAT round 7 collapse (workspace shrank to 40px) was caused by
    // the chain BREAKING ABOVE this point — `WorkspaceLayoutWidget.root`
    // had `flex: 1` but its parent was `display: block` (flex ignored),
    // so the row had no determinate height to share. Rounds 11/12 fixed
    // that at the source. R4-110 added chain robustness at
    // `WorkspaceTabManagerComponent.content` so future widget roots can
    // use either `flex: 1` or `height: 100%` and the chain still works.
  },
  titleBar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  titleArea: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  // Section-header title typography (round-8.2 per operator). 16px
  // (fontSizeBase400) · semibold (600) · #242424 (colorNeutralForeground1 in light;
  // semantic token adapts in dark — ADR-021). Grid-backed sections suppress this
  // title entirely (they carry the DataGrid's own elevated header) via `hideTitle`.
  titleText: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
  },
  toolbarRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: '1px' as const,
    borderBottomStyle: 'solid' as const,
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: '36px',
  },
  toolbarSpacer: {
    flex: '1 1 0',
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    // `minHeight: 0` lets this flex item shrink below its content size so a child
    // that manages its own internal scroll (e.g. the Email two-pane surface) bounds
    // and scrolls INTERNALLY instead of growing and pushing an outer scroll. Mirrors
    // the same fix on the tab-manager `content` wrapper (R4-110). Owner UAT R5 item 1.
    minHeight: 0,
    overflow: 'hidden',
  },
  contentCollapsed: {
    display: 'none',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SectionPanel — bordered workspace section with title, optional toolbar, and body.
 *
 * Use this to wrap any workspace section content (action card rows, metric card rows,
 * feed components, lists). The panel handles the structural chrome (border, title bar,
 * toolbar row) so the consumer only needs to supply the title, optional badge count,
 * optional toolbar buttons, and children.
 *
 * @example
 * ```tsx
 * <SectionPanel
 *   title="My To Do List"
 *   badgeCount={todoCount}
 *   toolbar={
 *     <>
 *       <Button appearance="subtle" size="small" icon={<ArrowClockwiseRegular />} onClick={refetch} />
 *       <Button appearance="subtle" size="small" icon={<AddRegular />} onClick={openCreateWizard} />
 *     </>
 *   }
 * >
 *   <SmartToDo embedded webApi={webApi} userId={userId} />
 * </SectionPanel>
 * ```
 */
export const SectionPanel: React.FC<SectionPanelProps> = ({
  title,
  titleContent,
  hideTitle = false,
  badgeCount,
  toolbar,
  children,
  collapsible = false,
  open: openProp,
  onOpenChange,
  className,
  style,
}) => {
  const styles = useStyles();

  // Uncontrolled open state — defaults to true (open)
  const [internalOpen, setInternalOpen] = React.useState(true);
  const isOpen = openProp !== undefined ? openProp : internalOpen;

  const handleToggle = React.useCallback(() => {
    const next = !isOpen;
    if (openProp === undefined) {
      setInternalOpen(next);
    }
    onOpenChange?.(next);
  }, [isOpen, openProp, onOpenChange]);

  const showBadge = badgeCount !== undefined && badgeCount > 0;

  return (
    <div className={mergeClasses(styles.card, className)} style={style}>
      {/* Title bar. Suppressed entirely on grid-backed sections (hideTitle) unless a
          badge or collapse control still needs it — so no empty header strip sits
          above the grid's own elevated header (round-8.2). */}
      {(!hideTitle || showBadge || collapsible) && (
        <div className={styles.titleBar}>
          <div className={styles.titleArea}>
            {!hideTitle && (titleContent ?? <Text className={styles.titleText}>{title}</Text>)}
            {showBadge && (
              <Badge appearance="filled" color="brand" size="small">
                {badgeCount}
              </Badge>
            )}
          </div>

          {/* Collapse/expand toggle */}
          {collapsible && (
            <Button
              appearance="subtle"
              size="small"
              icon={isOpen ? <ChevronUpRegular /> : <ChevronDownRegular />}
              onClick={handleToggle}
              aria-label={isOpen ? `Collapse ${title}` : `Expand ${title}`}
              aria-expanded={isOpen}
            />
          )}
        </div>
      )}

      {/* Toolbar row (rendered only when toolbar prop is provided) */}
      {toolbar && isOpen && (
        <div className={styles.toolbarRow} role="toolbar" aria-label={`${title} toolbar`}>
          <div className={styles.toolbarSpacer} />
          {toolbar}
        </div>
      )}

      {/* Content */}
      <div className={mergeClasses(styles.content, !isOpen && styles.contentCollapsed)} aria-hidden={!isOpen}>
        {children}
      </div>
    </div>
  );
};

SectionPanel.displayName = 'SectionPanel';
