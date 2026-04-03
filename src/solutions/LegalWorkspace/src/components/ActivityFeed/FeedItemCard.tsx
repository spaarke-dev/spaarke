/**
 * FeedItemCard — card for a single Updates Feed event.
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Handles event-specific content (urgency accent, priority, To Do toggle,
 * regarding record link, due date) and overflow menu.
 *
 * Accent border color varies by urgency: red=overdue, amber=soon, green=on track.
 * Tools: To Do toggle (with pending spinner). Overflow: Email, Teams, Edit, AI Summary.
 */

import * as React from "react";
import {
  tokens,
  Text,
  Button,
  Tooltip,
  Spinner,
  mergeClasses,
  makeStyles,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
} from "@fluentui/react-components";
import {
  MoreVerticalRegular,
  SparkleRegular,
  MailRegular,
  ChatRegular,
  EditRegular,
} from "@fluentui/react-icons";
import { MicrosoftToDoIcon } from "../../icons/MicrosoftToDoIcon";
import { IEvent } from "../../types/entities";
import { PriorityLevel } from "../../types/enums";
import { formatRelativeTime } from "../../utils/formatRelativeTime";
import { getTypeIcon, getTypeIconLabel } from "../../utils/typeIconMap";
import { useFeedTodoSync } from "../../hooks/useFeedTodoSync";
import { navigateToEntity, openRecordDialog } from "../../utils/navigation";
import { RecordCardShell, CardIcon } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Priority / urgency helpers
// ---------------------------------------------------------------------------

const PRIORITY_BADGE_STYLES: Record<PriorityLevel, React.CSSProperties> = {
  Urgent: { backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand },
  High: { backgroundColor: tokens.colorPaletteYellowBackground3, color: tokens.colorNeutralForeground1 },
  Normal: { backgroundColor: tokens.colorPaletteBlueBorderActive, color: tokens.colorNeutralForegroundOnBrand },
  Low: { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground1 },
};

function derivePriorityLevel(priority: number | undefined): PriorityLevel | null {
  switch (priority) {
    case 0: return "Low";
    case 1: return "Normal";
    case 2: return "High";
    case 3: return "Urgent";
    default: return null;
  }
}

type UrgencyTier = "overdue" | "dueSoon" | "onTrack" | "neutral";

function deriveUrgencyTier(dueDate: string | undefined): UrgencyTier {
  if (!dueDate) return "neutral";
  const due = new Date(dueDate);
  if (isNaN(due.getTime())) return "neutral";
  const diffDays = Math.ceil((due.getTime() - Date.now()) / 86400000);
  if (diffDays < 0) return "overdue";
  if (diffDays <= 3) return "dueSoon";
  if (diffDays <= 10) return "onTrack";
  return "neutral";
}

const URGENCY_ACCENT: Record<UrgencyTier, string> = {
  overdue: tokens.colorPaletteRedBorder2,
  dueSoon: tokens.colorPaletteDarkOrangeBorder2,
  onTrack: tokens.colorPaletteGreenBorder2,
  neutral: tokens.colorNeutralStroke2,
};

// ---------------------------------------------------------------------------
// Regarding entity mapping
// ---------------------------------------------------------------------------

function resolveRegardingEntityName(displayName: string | undefined): string | null {
  if (!displayName) return null;
  const lower = displayName.toLowerCase();
  if (lower === "matter") return "sprk_matter";
  if (lower === "project") return "sprk_project";
  return `sprk_${lower}`;
}

// ---------------------------------------------------------------------------
// Badge sub-components
// ---------------------------------------------------------------------------

const badgeBase: React.CSSProperties = {
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  borderRadius: tokens.borderRadiusSmall,
  paddingTop: "1px",
  paddingBottom: "1px",
  paddingLeft: tokens.spacingHorizontalXS,
  paddingRight: tokens.spacingHorizontalXS,
  fontSize: tokens.fontSizeBase100,
  fontWeight: tokens.fontWeightSemibold,
  lineHeight: tokens.lineHeightBase100,
  whiteSpace: "nowrap",
  flexShrink: 0,
};

const PriorityBadge: React.FC<{ level: PriorityLevel }> = ({ level }) => (
  <span role="img" aria-label={`Priority: ${level}`} style={{ ...badgeBase, ...PRIORITY_BADGE_STYLES[level] }}>
    {level}
  </span>
);

const TypeBadge: React.FC<{ typeName: string }> = ({ typeName }) => (
  <span role="img" aria-label={`Type: ${typeName}`} style={{ ...badgeBase, backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2 }}>
    {typeName}
  </span>
);

const RecordTypeBadge: React.FC<{ typeName: string }> = ({ typeName }) => (
  <span role="img" aria-label={`Record type: ${typeName}`} style={{ ...badgeBase, backgroundColor: tokens.colorBrandBackground2, color: tokens.colorBrandForeground1 }}>
    {typeName}
  </span>
);

// ---------------------------------------------------------------------------
// Content-specific styles (layout handled by RecordCardShell)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  title: {
    overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1, fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0, maxWidth: "50%",
  },
  description: {
    overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground3, flex: "1 1 0", minWidth: 0,
  },
  metaText: { color: tokens.colorNeutralForeground3, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" },
  regardingLink: {
    color: tokens.colorBrandForeground1, whiteSpace: "nowrap", overflow: "hidden",
    textOverflow: "ellipsis", cursor: "pointer", textDecorationLine: "none",
    ":hover": { textDecorationLine: "underline" },
  },
  metaDivider: { color: tokens.colorNeutralForeground4 },
  timestamp: { color: tokens.colorNeutralForeground3, whiteSpace: "nowrap" },
  dueDateOverdue: { color: tokens.colorPaletteRedForeground3, fontWeight: tokens.fontWeightSemibold, whiteSpace: "nowrap" },
  todoButtonActive: { color: tokens.colorBrandForeground1 },
  todoButtonWrapper: { position: "relative", display: "inline-flex", alignItems: "center", justifyContent: "center" },
  todoPendingSpinner: { position: "absolute", pointerEvents: "none" },
  todoButtonError: { color: tokens.colorPaletteRedForeground3 },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFeedItemCardProps {
  event: IEvent;
  onAISummary: (eventId: string) => void;
  onEmail?: (eventId: string) => void;
  onTeams?: (eventId: string) => void;
  onEdit?: (eventId: string) => void;
  hideOverflowMenu?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FeedItemCard: React.FC<IFeedItemCardProps> = React.memo(
  ({ event, onAISummary, onEmail, onTeams, onEdit, hideOverflowMenu }) => {
    const styles = useStyles();

    // Flag state from shared context
    const { isFlagged, toggleFlag, isPending, getError } = useFeedTodoSync();
    const flagged = isFlagged(event.sprk_eventid);
    const pending = isPending(event.sprk_eventid);
    const flagError = getError(event.sprk_eventid);

    const priorityLevel = derivePriorityLevel(event.sprk_priority);
    const TypeIconComponent = getTypeIcon(event.eventTypeName);
    const typeIconLabel = getTypeIconLabel(event.eventTypeName);

    const dueDateText = event.sprk_duedate ? `Due: ${formatRelativeTime(event.sprk_duedate)}` : null;
    const isDueOverdue = event.sprk_duedate ? new Date(event.sprk_duedate) < new Date() : false;
    const urgencyTier = deriveUrgencyTier(event.sprk_duedate);

    // ── Handlers ──

    const handleFlagToggle = React.useCallback(() => {
      void toggleFlag(event.sprk_eventid);
    }, [toggleFlag, event.sprk_eventid]);

    const handleAISummary = React.useCallback(() => onAISummary(event.sprk_eventid), [onAISummary, event.sprk_eventid]);

    const handleEmail = React.useCallback(() => {
      onEmail ? onEmail(event.sprk_eventid) : console.info(`[FeedItemCard] Email stub ${event.sprk_eventid}`);
    }, [onEmail, event.sprk_eventid]);

    const handleTeams = React.useCallback(() => {
      onTeams ? onTeams(event.sprk_eventid) : console.info(`[FeedItemCard] Teams stub ${event.sprk_eventid}`);
    }, [onTeams, event.sprk_eventid]);

    const handleRegardingClick = React.useCallback(() => {
      const entityName = resolveRegardingEntityName(event.regardingRecordTypeName);
      if (entityName && event.sprk_regardingrecordid) {
        navigateToEntity({ action: "openRecord", entityName, entityId: event.sprk_regardingrecordid });
      }
    }, [event.regardingRecordTypeName, event.sprk_regardingrecordid]);

    const handleEdit = React.useCallback(() => {
      onEdit ? onEdit(event.sprk_eventid) : openRecordDialog("sprk_event", event.sprk_eventid);
    }, [onEdit, event.sprk_eventid]);

    const handleCardClick = React.useCallback((e: React.MouseEvent | React.KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target.closest("button, [role='link'], [role='menuitem'], a")) return;
      handleEdit();
    }, [handleEdit]);

    // ── Accessibility ──

    const cardAriaLabel = [
      event.eventTypeName || "",
      event.sprk_eventname,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      event.regardingRecordTypeName || "",
      event.sprk_regardingrecordname || "",
      dueDateText || "",
      event.assignedToName ? `Assigned to: ${event.assignedToName}.` : "",
      flagged ? "Flagged as to-do." : "",
    ].filter(Boolean).join(" ");

    const todoTooltip = flagError ? `Error: ${flagError} — click to retry` : flagged ? "Remove from To Do" : "Add to To Do";
    const todoButtonClass = flagError ? styles.todoButtonError : flagged ? styles.todoButtonActive : undefined;

    // ── To Do toggle tool ──

    const todoTool = (
      <Tooltip content={todoTooltip} relationship="label">
        <div className={styles.todoButtonWrapper}>
          <Button
            appearance="subtle"
            size="medium"
            icon={<MicrosoftToDoIcon size={20} active={flagged} />}
            aria-label={todoTooltip}
            aria-pressed={flagged}
            aria-busy={pending}
            className={todoButtonClass}
            onClick={handleFlagToggle}
            disabled={pending}
          />
          {pending && (
            <span className={styles.todoPendingSpinner} aria-hidden="true">
              <Spinner size="extra-tiny" />
            </span>
          )}
        </div>
      </Tooltip>
    );

    // ── Overflow menu ──

    const overflowMenu = hideOverflowMenu ? undefined : (
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Tooltip content="More actions" relationship="label">
            <Button appearance="subtle" size="medium" icon={<MoreVerticalRegular aria-hidden="true" />} aria-label="More actions" />
          </Tooltip>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            <MenuItem icon={<MailRegular />} onClick={handleEmail}>Email</MenuItem>
            <MenuItem icon={<ChatRegular />} onClick={handleTeams}>Teams Chat</MenuItem>
            <MenuItem icon={<EditRegular />} onClick={handleEdit}>Edit</MenuItem>
            <MenuItem icon={<SparkleRegular />} onClick={handleAISummary}>AI Summary</MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>
    );

    return (
      <RecordCardShell
        icon={
          <CardIcon>
            <TypeIconComponent fontSize={20} aria-label={typeIconLabel} />
          </CardIcon>
        }
        accentColor={URGENCY_ACCENT[urgencyTier]}
        primaryContent={
          <>
            {event.eventTypeName && <TypeBadge typeName={event.eventTypeName} />}
            <Text as="span" size={400} className={styles.title}>{event.sprk_eventname}</Text>
            {event.sprk_description && (
              <Text as="span" size={300} className={styles.description}>{event.sprk_description}</Text>
            )}
          </>
        }
        secondaryContent={
          <>
            {priorityLevel && <PriorityBadge level={priorityLevel} />}
            {event.regardingRecordTypeName && <RecordTypeBadge typeName={event.regardingRecordTypeName} />}
            {event.sprk_regardingrecordname && event.sprk_regardingrecordid && (
              <Text
                as="span" size={200} className={styles.regardingLink}
                role="link" tabIndex={0}
                onClick={handleRegardingClick}
                onKeyDown={(e: React.KeyboardEvent) => {
                  if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleRegardingClick(); }
                }}
                aria-label={`Open ${event.regardingRecordTypeName ?? "record"}: ${event.sprk_regardingrecordname}`}
              >
                {event.sprk_regardingrecordname}
              </Text>
            )}
            {event.sprk_regardingrecordname && !event.sprk_regardingrecordid && (
              <Text size={200} className={styles.metaText}>{event.sprk_regardingrecordname}</Text>
            )}
            {dueDateText && (
              <>
                <Text size={200} className={styles.metaDivider} aria-hidden="true">·</Text>
                <Text size={200} className={isDueOverdue ? styles.dueDateOverdue : styles.timestamp}>{dueDateText}</Text>
              </>
            )}
            {event.assignedToName && (
              <>
                <Text size={200} className={styles.metaDivider} aria-hidden="true">·</Text>
                <Text size={200} className={styles.metaText}>{event.assignedToName}</Text>
              </>
            )}
          </>
        }
        tools={todoTool}
        overflowMenu={overflowMenu}
        onClick={handleCardClick}
        ariaLabel={cardAriaLabel}
      />
    );
  }
);

FeedItemCard.displayName = "FeedItemCard";
