/**
 * IssueLine — one Work Item on one line, inside a MatterCard (not a second row component).
 *
 * v4 (HANDOFF section 1.2, binding): a headline, the rule's short name, and age (Decide) or due state (Do). The WHOLE
 * line is the one activatable element (a native button: Enter and Space work for free); there is no button, menu, rule
 * code, clause mark or badge inside it. Overdue and coming-due wording take the SmartTodo palette by text colour only.
 *
 * Task: spaarke-ontology-platform-r1, task 051.
 */

import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ChevronRightRegular } from '@fluentui/react-icons';
import type { IssueLineView, TimingTone } from './composeIssueLine';

export interface IssueLineProps {
  /** `sprk_signalid`: the object this line resolves to. */
  itemId: string;
  view: IssueLineView;
  onOpen: (itemId: string) => void;
}

const useStyles = makeStyles({
  line: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorTransparentBackground,
    borderTopWidth: '0',
    borderRightWidth: '0',
    borderBottomWidth: '0',
    borderLeftWidth: '0',
    borderTopStyle: 'none',
    borderRightStyle: 'none',
    borderBottomStyle: 'none',
    borderLeftStyle: 'none',
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyBase,
    textAlign: 'left',
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':active': { backgroundColor: tokens.colorNeutralBackground1Pressed },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
  },
  headline: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  rule: { flexShrink: 0, color: tokens.colorNeutralForeground3 },
  timing: { flexShrink: 0, color: tokens.colorNeutralForeground3 },
  chevron: { flexShrink: 0, color: tokens.colorNeutralForeground3 },
  // Tier -> text colour: SmartTodo's due palette (overdue red / 0-3d dark orange / 4-7d yellow / 8-10d grey, owner
  // decision 2026-10-05), as the Foreground tokens of the same Red / DarkOrange / Yellow families SmartTodo's
  // badge backgrounds use (a line has no badge, so the text takes the colour). Tokens only.
  overdue: { color: tokens.colorPaletteRedForeground1 },
  soon3d: { color: tokens.colorPaletteDarkOrangeForeground1 },
  soon7d: { color: tokens.colorPaletteYellowForeground1 },
  soon10d: { color: tokens.colorNeutralForeground2 },
});

export const IssueLine: React.FC<IssueLineProps> = ({ itemId, view, onOpen }) => {
  const styles = useStyles();
  const toneClass: Partial<Record<TimingTone, string>> = {
    overdue: styles.overdue,
    '3d': styles.soon3d,
    '7d': styles.soon7d,
    '10d': styles.soon10d,
  };
  return (
    <button
      type="button"
      className={styles.line}
      data-testid="issue-line"
      data-signal-id={itemId}
      onClick={() => onOpen(itemId)}
    >
      <Text className={styles.headline} size={300} title={view.headline}>
        {view.headline}
      </Text>
      {view.ruleShortName && (
        <Text className={styles.rule} size={200}>
          {view.ruleShortName}
        </Text>
      )}
      <Text
        className={mergeClasses(styles.timing, toneClass[view.timing.tone])}
        size={200}
        data-testid="issue-timing"
        data-tone={view.timing.tone}
      >
        {view.timing.text}
      </Text>
      <ChevronRightRegular className={styles.chevron} aria-hidden="true" />
    </button>
  );
};

IssueLine.displayName = 'IssueLine';
