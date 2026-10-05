/**
 * FeedItemCard — card for a single Updates Feed event.
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Handles event-specific content (urgency accent, priority, To Do toggle,
 * regarding record link, due date) and overflow menu.
 *
 * Accent border color varies by due-date tier (see ./feedDueAccent.ts): red=overdue,
 * dark orange=0-3 days, yellow=4-7 days, neutral otherwise.
 * Tools: To Do toggle (with pending spinner). Overflow: Email, Teams, Edit, AI Summary.
 */

import * as React from "react";
import {
  tokens,
  Text,
  Button,
  Tooltip,
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
import { IEvent } from "../../types/entities";
import { PriorityLevel } from "../../types/enums";
import { getTypeIcon, getTypeIconLabel } from "../../utils/typeIconMap";
import { RecordCardShell, CardIcon, createXrmNavigationService } from "@spaarke/ui-components";
import { formatDueDate } from "@spaarke/daily-briefing-components/utils";
import { FEED_DUE_ACCENT, feedDueUrgency } from "./feedDueAccent";

// R3 FR-14 / OS-1 note:
//   The legacy "Flag as To Do" button on the FeedItemCard wrote
//   `sprk_event.sprk_todoflag = true` and used FeedTodoSyncContext to debounce
//   + persist + propagate. R3 removes that column entirely and makes
//   `sprk_todo` a first-class entity. The previous wiring (isFlagged /
//   toggleFlag / pending / error reads from useFeedTodoSync) is therefore
//   removed below. A future task will add a "Create To Do regarding this
//   event" affordance that opens the CreateTodo wizard with the event
//   pre-bound as `sprk_regardingevent` — see project plan Phase 4 / FR-16.

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

    // R3 FR-14: flag-state reads removed — see file-level note. The feed card
    // no longer displays per-event flag state; todos are tracked on the
    // `sprk_todo` entity and surfaced via the SmartToDo block.

    const priorityLevel = derivePriorityLevel(event.sprk_priority);
    const TypeIconComponent = getTypeIcon(event.eventTypeName);
    const typeIconLabel = getTypeIconLabel(event.eventTypeName);

    // `sprk_duedate` is a DateOnly calendar day, not an instant: day-granular
    // label ("Due today" / "Due tomorrow" / "Due in 3d" / "Overdue by 2d" /
    // "Due Oct 20") from the shared `formatDueDate`, never an elapsed-time
    // phrase (task 081 / F4 — the elapsed formatter read a due date of today
    // as "Due: 14 hours ago" in US zones). Tier from the shared
    // `dueUrgencyForDays` (see ./feedDueAccent.ts).
    const dueDateText = formatDueDate(event.sprk_duedate);
    const dueUrgency = feedDueUrgency(event.sprk_duedate);
    const isDueOverdue = dueUrgency === "overdue";

    // ── Handlers ──

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
        createXrmNavigationService().openRecord(entityName, event.sprk_regardingrecordid).catch((err) => {
          console.error("[FeedItemCard] openRecord failed:", err);
        });
      }
    }, [event.regardingRecordTypeName, event.sprk_regardingrecordid]);

    const handleEdit = React.useCallback(() => {
      if (onEdit) {
        onEdit(event.sprk_eventid);
      } else {
        createXrmNavigationService().openRecordModal?.("sprk_event", event.sprk_eventid).catch((err) => {
          console.error("[FeedItemCard] openRecordModal failed:", err);
        });
      }
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
    ].filter(Boolean).join(" ");

    // R3 FR-14: the legacy "Add to To Do" flag-toggle tool is removed. The
    // `tools` slot is left empty; a future task will reintroduce a
    // "Create To Do regarding this event" affordance once the CreateTodo
    // wizard is repointed at `sprk_todo` (Phase 4 / FR-16).
    const todoTool: React.ReactNode = undefined;

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
        accentColor={FEED_DUE_ACCENT[dueUrgency]}
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
