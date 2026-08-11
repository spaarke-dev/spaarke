/**
 * TabStrip — the workspace tab strip for the module-host shell (task 011 §3).
 *
 * Ported (Xrm-free, token-only) from the owner-approved P0 prototype
 * (`spaarke-prototype/projects/2026-08-external-access-module-host/src/components/TabStrip.tsx`),
 * the sign-off visual target for this shell.
 *
 * §11 REUSE JUSTIFICATION (the ONE sanctioned bespoke shell primitive):
 *   - existing: Fluent v9 `TabList`/`Tab`, and the SpaarkeAi `WorkspaceTabManagerComponent`
 *     (`src/solutions/SpaarkeAi/...`) — but the latter is Xrm/`@spaarke/ai-widgets`-coupled and
 *     not a shared-library export, so it cannot be consumed from the external SPA.
 *   - extension: No. The requested UX is a SpaarkeAi-style per-tab CLOSE CONTROL — a small
 *     circular "×" in the SELECTED tab's upper corner — which Fluent's `Tab` cannot nest as an
 *     interactive child without a button-in-button a11y violation.
 *   - cost-of-doing-nothing: without it there is no non-closable pinned Quick Start tab + corner-×
 *     closable widget tabs (acceptance criteria 1 + 3). Built from Fluent v9 primitives + semantic
 *     tokens only (zero hex, ADR-021); keeps `tablist`/`tab` roles, keyboard selection, focus rings.
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, mergeClasses, Text } from '@fluentui/react-components';
import { DismissRegular, type FluentIcon } from '@fluentui/react-icons';

const useStyles = makeStyles({
  strip: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    // Room for the close circle that overhangs the selected tab's top edge.
    paddingTop: tokens.spacingVerticalS,
  },
  tab: {
    position: 'relative',
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    cursor: 'pointer',
    color: tokens.colorNeutralForeground2,
    backgroundColor: tokens.colorTransparentBackground,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusNone,
    borderBottomRightRadius: tokens.borderRadiusNone,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', 'transparent'),
    borderBottomColor: 'transparent',
    // Sit on top of the strip's bottom divider (owned by the tab bar).
    marginBottom: `calc(-1 * ${tokens.strokeWidthThin})`,
    transitionProperty: 'background-color, color, border-color',
    transitionDuration: tokens.durationFaster,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
      color: tokens.colorNeutralForeground1,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '2px',
    },
  },
  tabActive: {
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderBottomColor: tokens.colorNeutralBackground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  icon: { display: 'inline-flex', fontSize: '16px', flexShrink: 0 },
  label: { whiteSpace: 'nowrap' },
  // The SpaarkeAi-style close circle in the selected tab's upper-right corner.
  closeCircle: {
    position: 'absolute',
    top: '-8px',
    right: '-6px',
    width: '18px',
    height: '18px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 0,
    borderRadius: tokens.borderRadiusCircular,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    cursor: 'pointer',
    fontSize: '12px',
    lineHeight: '1',
    boxShadow: tokens.shadow2,
    ':hover': {
      backgroundColor: tokens.colorStatusDangerBackground1,
      ...shorthands.borderColor(tokens.colorStatusDangerBorder1),
      color: tokens.colorStatusDangerForeground1,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '1px',
    },
  },
});

export interface TabItem {
  id: string;
  title: string;
  icon: FluentIcon;
  /** Closable tabs show the corner close circle when active; pinned tabs never do. */
  closable: boolean;
}

export interface TabStripProps {
  tabs: TabItem[];
  activeId: string | null;
  onSelect: (id: string) => void;
  onClose: (id: string) => void;
}

export const TabStrip: React.FC<TabStripProps> = ({ tabs, activeId, onSelect, onClose }) => {
  const s = useStyles();
  return (
    <div className={s.strip} role="tablist" aria-label="Open widgets">
      {tabs.map(({ id, title, icon: Icon, closable }) => {
        const isActive = id === activeId;
        return (
          <div
            key={id}
            role="tab"
            aria-selected={isActive}
            tabIndex={isActive ? 0 : -1}
            className={mergeClasses(s.tab, isActive && s.tabActive)}
            onClick={() => onSelect(id)}
            onKeyDown={e => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onSelect(id);
              }
            }}
          >
            <span className={s.icon} aria-hidden="true">
              <Icon />
            </span>
            <Text className={s.label}>{title}</Text>
            {isActive && closable && (
              <span
                role="button"
                tabIndex={0}
                aria-label={`Close ${title} tab`}
                className={s.closeCircle}
                onClick={e => {
                  e.stopPropagation();
                  onClose(id);
                }}
                onKeyDown={e => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    e.stopPropagation();
                    onClose(id);
                  }
                }}
              >
                <DismissRegular />
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
};

export default TabStrip;
