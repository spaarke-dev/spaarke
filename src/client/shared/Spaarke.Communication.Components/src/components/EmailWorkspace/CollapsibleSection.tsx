/**
 * CollapsibleSection.tsx
 *
 * A single reusable collapsible section for the one-column reading pane
 * (email-communication-solution-r5, reading-pane MAIN-AREA redesign). Three
 * reading-pane sections — Attachments, Related to, and Association — share the
 * exact same collapse behavior: a full-width header row that toggles a body,
 * COLLAPSED by default, with a trailing indicator in the header. Rather than
 * three divergent inline implementations, this is the one component they all use.
 *
 * §11 justification:
 *   - Existing: no collapsible primitive exists in `@spaarke/communication-components`;
 *     Fluent's `Accordion` does not give the header a status-dot slot without wrapping.
 *   - Extension: n/a — nothing to extend.
 *   - Cost-of-doing-nothing: the three sections would each re-implement the
 *     expand/collapse + chevron + aria wiring, drifting apart visually.
 *
 * The header renders EITHER a count indicator ("Attachments (3)") OR a status
 * dot + one-line status ("🟢 All set") — the Association section uses the dot,
 * Attachments/Related-to use the count. Purely presentational; the host owns the
 * body content. Fluent v9 tokens only (ADR-021, dark-mode correct via the host
 * `FluentProvider`). `React.FC` + standard hooks — no `as React.ComponentType`
 * cast (NFR-05).
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text } from '@fluentui/react-components';
import { ChevronRight16Regular } from '@fluentui/react-icons';

/** Traffic-light tones for the optional header status dot. */
export type SectionStatusTone = 'red' | 'yellow' | 'green';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  // Header row hosts the toggle button + an optional interactive accessory
  // (e.g. the confirmed-association chip) as SIBLINGS — the accessory must not
  // nest inside the toggle <button> (invalid + swallows clicks).
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    width: '100%',
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalXL,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flex: '0 1 auto',
    minWidth: 0,
    background: 'none',
    border: 'none',
    cursor: 'pointer',
    textAlign: 'left',
    padding: 0,
    color: tokens.colorNeutralForeground1,
    ':hover': { color: tokens.colorNeutralForeground1Hover },
  },
  accessory: { display: 'flex', alignItems: 'center', minWidth: 0, gap: tokens.spacingHorizontalXS },
  chevron: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    transitionProperty: 'transform',
    transitionDuration: tokens.durationNormal,
  },
  chevronOpen: { transform: 'rotate(90deg)' },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  count: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  statusWrap: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginInlineStart: 'auto',
  },
  dot: { width: '10px', height: '10px', borderRadius: '50%', flexShrink: 0 },
  dotRed: { backgroundColor: tokens.colorPaletteRedForeground1 },
  dotYellow: { backgroundColor: tokens.colorPaletteMarigoldForeground1 },
  dotGreen: { backgroundColor: tokens.colorPaletteGreenForeground1 },
  statusLabel: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  body: {
    paddingInline: tokens.spacingHorizontalXL,
    paddingBottom: tokens.spacingVerticalL,
  },
});

export interface CollapsibleSectionProps {
  /** Section label, e.g. "Attachments" / "Related to" / "Association". */
  title: string;
  /** Optional count indicator — renders "{title} ({count})". */
  count?: number;
  /** Optional status dot + one-line label (Association section). Takes header-right slot. */
  status?: { tone: SectionStatusTone; label: string };
  /**
   * Optional interactive node rendered on the header row, BESIDE the toggle button
   * (not nested in it). Used by the merged "Related to" section to surface the
   * confirmed-association chip so it's visible while the section is collapsed.
   */
  headerAccessory?: React.ReactNode;
  /** Start expanded. Defaults to `false` (COLLAPSED by default — redesign requirement). */
  defaultOpen?: boolean;
  /**
   * Keep the body mounted (just visually hidden) while collapsed, instead of
   * unmounting it. Used by the Attachments section so its child can fetch and
   * report a header count ("Attachments (3)") WITHOUT the user first expanding
   * it. Default `false` (lazy — body mounts only when expanded).
   */
  keepMounted?: boolean;
  /** Stable id for the body region (aria wiring) + `data-testid` root suffix. */
  id: string;
  children: React.ReactNode;
}

export const CollapsibleSection: React.FC<CollapsibleSectionProps> = ({
  title,
  count,
  status,
  headerAccessory,
  defaultOpen = false,
  keepMounted = false,
  id,
  children,
}) => {
  const s = useStyles();
  const [open, setOpen] = React.useState<boolean>(defaultOpen);
  const dotClass =
    status?.tone === 'green' ? s.dotGreen : status?.tone === 'yellow' ? s.dotYellow : s.dotRed;

  return (
    <div className={s.root} data-testid={`section-${id}`}>
      <div className={s.headerRow}>
        <button
          type="button"
          className={s.header}
          aria-expanded={open}
          aria-controls={`section-body-${id}`}
          onClick={() => setOpen(o => !o)}
          data-testid={`section-toggle-${id}`}
        >
          <ChevronRight16Regular className={mergeClasses(s.chevron, open && s.chevronOpen)} aria-hidden="true" />
          <Text className={s.title}>{title}</Text>
          {typeof count === 'number' && <Text className={s.count}>({count})</Text>}
        </button>
        {headerAccessory && <div className={s.accessory}>{headerAccessory}</div>}
        {status && (
          <span className={s.statusWrap}>
            <span className={mergeClasses(s.dot, dotClass)} aria-hidden="true" />
            <Text className={s.statusLabel}>{status.label}</Text>
          </span>
        )}
      </div>
      {keepMounted ? (
        <div
          id={`section-body-${id}`}
          className={s.body}
          data-testid={`section-body-${id}`}
          hidden={!open}
          style={open ? undefined : { display: 'none' }}
        >
          {children}
        </div>
      ) : (
        open && (
          <div id={`section-body-${id}`} className={s.body} data-testid={`section-body-${id}`}>
            {children}
          </div>
        )
      )}
    </div>
  );
};

CollapsibleSection.displayName = 'CollapsibleSection';
