import React from "react";
import {
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  Open24Regular,
  Share24Regular,
  Mail24Regular,
  People24Regular,
  Calendar24Regular,
} from "@fluentui/react-icons";
import type { IFollowUpAction } from "./types";

// ---------------------------------------------------------------------------
// Icon map — maps icon string keys to Fluent icon components
// ---------------------------------------------------------------------------

const ICON_MAP: Record<string, React.ReactElement> = {
  open: <Open24Regular />,
  share: <Share24Regular />,
  mail: <Mail24Regular />,
  people: <People24Regular />,
  calendar: <Calendar24Regular />,
};

function resolveIcon(iconKey: string): React.ReactElement {
  return ICON_MAP[iconKey] ?? <Open24Regular />;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
  subtitle: {
    color: tokens.colorNeutralForeground2,
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(2, 1fr)",
    gap: tokens.spacingHorizontalM,
    "@media (max-width: 480px)": {
      gridTemplateColumns: "1fr",
    },
  },
  card: {
    cursor: "pointer",
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground1,
    transition: "background-color 0.15s ease",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  cardIconRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
  },
  cardLabel: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  cardDescription: {
    color: tokens.colorNeutralForeground2,
  },
  divider: {
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    marginTop: tokens.spacingVerticalS,
  },
  openWorkspaceCard: {
    cursor: "pointer",
    border: `1px solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorBrandBackground2,
    transition: "background-color 0.15s ease",
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  openWorkspaceIconRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
  },
  openWorkspaceLabel: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  openWorkspaceDescription: {
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Sub-component: ActionCard
// ---------------------------------------------------------------------------

interface IActionCardProps {
  action: IFollowUpAction;
  analysisId: string;
}

const ActionCard: React.FC<IActionCardProps> = ({ action, analysisId }) => {
  const styles = useStyles();

  const handleClick = () => {
    action.onClick(analysisId);
  };

  return (
    <div className={styles.card} onClick={handleClick} role="button" tabIndex={0}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") handleClick(); }}>
      <div className={styles.cardIconRow}>
        {resolveIcon(action.icon)}
        <Text className={styles.cardLabel}>{action.label}</Text>
      </div>
      <Text size={200} className={styles.cardDescription}>
        {action.description}
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Sub-component: OpenWorkspaceCard
// ---------------------------------------------------------------------------

interface IOpenWorkspaceCardProps {
  analysisId: string;
  onOpenWorkspace?: (analysisId: string) => void;
}

const OpenWorkspaceCard: React.FC<IOpenWorkspaceCardProps> = ({
  analysisId,
  onOpenWorkspace,
}) => {
  const styles = useStyles();

  const handleClick = () => {
    onOpenWorkspace?.(analysisId);
  };

  return (
    <div
      className={styles.openWorkspaceCard}
      onClick={handleClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") handleClick(); }}
    >
      <div className={styles.openWorkspaceIconRow}>
        <Open24Regular />
        <Text className={styles.openWorkspaceLabel}>Open Analysis Workspace</Text>
      </div>
      <Text size={200} className={styles.openWorkspaceDescription}>
        Open the full Analysis Workspace to review results, refine settings, and
        explore deeper insights.
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Main component: FollowUpActionsStep
// ---------------------------------------------------------------------------

export interface IFollowUpActionsStepProps {
  analysisId: string;
  availableActions: IFollowUpAction[];
  onOpenWorkspace?: (analysisId: string) => void;
}

export const FollowUpActionsStep: React.FC<IFollowUpActionsStepProps> = ({
  analysisId,
  availableActions,
  onOpenWorkspace,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <div className={styles.header}>
        <Text size={600} weight="semibold" className={styles.title}>
          What&apos;s Next?
        </Text>
        <Text size={300} className={styles.subtitle}>
          Analysis complete! Here&apos;s what you can do:
        </Text>
      </div>

      {availableActions.length > 0 && (
        <div className={styles.grid}>
          {availableActions.map((action) => (
            <ActionCard key={action.id} action={action} analysisId={analysisId} />
          ))}
        </div>
      )}

      <div className={styles.divider} />

      <OpenWorkspaceCard analysisId={analysisId} onOpenWorkspace={onOpenWorkspace} />
    </div>
  );
};

export default FollowUpActionsStep;
