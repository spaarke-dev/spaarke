/**
 * WorkspaceLauncherCard — a single proactive "open this workspace surface" CARD
 * (spaarkeai-assistant-enhancements-r4 task 023, FR-06).
 *
 * Per docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md a persistent act-on launcher
 * is a CARD (not a chip): it survives across turns and has its own clickable
 * affordance. This is the presentational half — a dumb, self-contained clickable
 * card (icon + title + one-line description + a trailing "opens" glyph). The gating
 * (arm-after-agenda + suppress-when-tab-already-open) and the launch dispatch live
 * in the host (`agendaFollowOnCards.tsx` + `ConversationPane`), so this component
 * takes NO host types and is trivially unit-testable in isolation (ADR-012 spirit —
 * context-agnostic presentation).
 *
 * Rules honored (ASSISTANT-UI-ELEMENT-CRITERIA §5):
 *  - Hover highlight ONLY on the clickable region (the whole card is the button).
 *  - No internal code in the label — plain human title/description only.
 *  - ADR-021: Fluent v9 semantic tokens only; no hardcoded colors — dark-mode safe.
 */
import * as React from 'react';
import { Button, makeStyles, shorthands, tokens, Text } from '@fluentui/react-components';
import { ArrowRightRegular } from '@fluentui/react-icons';

export interface WorkspaceLauncherCardProps {
  /** Card heading, e.g. "Open Daily Briefing". Plain text — never an internal id/code. */
  readonly title: string;
  /** One-line supporting description, e.g. "Review today's priorities and deadlines." */
  readonly description: string;
  /** Leading icon (presentation only). */
  readonly icon: React.ReactElement;
  /** Invoked on click — the host routes it to the surface launch (registry-driven). */
  readonly onOpen: () => void;
  /** Stable test id suffix (e.g. the consumerType). */
  readonly testId: string;
}

const useStyles = makeStyles({
  // The whole card IS the clickable region — hover affordance lives here only.
  card: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    justifyContent: 'flex-start',
    width: 'auto',
    textAlign: 'left',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':hover:active': { backgroundColor: tokens.colorNeutralBackground1Pressed },
  },
  leadingIcon: {
    display: 'flex',
    alignItems: 'center',
    fontSize: tokens.fontSizeBase500,
    color: tokens.colorNeutralForeground2,
  },
  textCol: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    flexGrow: 1,
    minWidth: 0,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
  },
  description: {
    color: tokens.colorNeutralForeground2,
  },
  trailing: {
    display: 'flex',
    alignItems: 'center',
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * A lone clickable launcher card. Content-agnostic — the caller supplies the
 * title/description/icon and the click handler; this component owns only the
 * card chrome + the "opens a surface" affordance (the trailing arrow).
 */
export function WorkspaceLauncherCard(props: WorkspaceLauncherCardProps): React.ReactElement {
  const styles = useStyles();
  const { title, description, icon, onOpen, testId } = props;
  return (
    <Button
      className={styles.card}
      appearance="transparent"
      onClick={onOpen}
      data-testid={`workspace-launcher-card-${testId}`}
    >
      <span className={styles.leadingIcon} aria-hidden="true">
        {icon}
      </span>
      <span className={styles.textCol}>
        <Text className={styles.title}>{title}</Text>
        <Text size={200} className={styles.description}>
          {description}
        </Text>
      </span>
      <span className={styles.trailing} aria-hidden="true">
        <ArrowRightRegular />
      </span>
    </Button>
  );
}

export default WorkspaceLauncherCard;
