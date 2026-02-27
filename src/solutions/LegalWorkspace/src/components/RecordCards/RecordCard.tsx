/**
 * RecordCard — generic 2-row card for entity records (Matters, Projects, Invoices).
 *
 * Follows the same visual design as FeedItemCard:
 *   ┌──────────────────────────────────────────────────────────────────────────┐
 *   │▌  [Icon 40px]  [Type badge] Field1  Field2  Field3…        [⋮ More]   │
 *   │▌               [Status badge] Description…                             │
 *   └──────────────────────────────────────────────────────────────────────────┘
 *     ↑ 3px left border (brand accent)
 *
 * Card click opens a dialog main form via Xrm.Navigation.openForm (target: 2).
 * Overflow menu provides: Edit, Email, Teams, AI Summary actions.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
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
import type { FluentIcon } from "@fluentui/react-icons";
import { openRecordDialog } from "../../utils/navigation";

// ---------------------------------------------------------------------------
// Styles (matching FeedItemCard pattern)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    marginBottom: tokens.spacingVerticalS,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandStroke1,
    cursor: "pointer",
    transitionProperty: "background-color, box-shadow",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      boxShadow: tokens.shadow4,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },
  mainRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalL,
  },
  typeIconCircle: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    width: "40px",
    height: "40px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
  },
  contentColumn: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  primaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "nowrap",
    minWidth: 0,
  },
  title: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
    maxWidth: "50%",
  },
  fieldText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground3,
    flexShrink: 1,
    minWidth: 0,
  },
  secondaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  description: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground3,
    flex: "1 1 0",
    minWidth: 0,
  },
  actionsColumn: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
    marginLeft: tokens.spacingHorizontalL,
  },
});

// ---------------------------------------------------------------------------
// Badge sub-components (matching FeedItemCard)
// ---------------------------------------------------------------------------

const TypeBadge: React.FC<{ label: string }> = ({ label }) => (
  <span
    role="img"
    aria-label={`Type: ${label}`}
    style={{
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
      backgroundColor: tokens.colorNeutralBackground3,
      color: tokens.colorNeutralForeground2,
      flexShrink: 0,
    }}
  >
    {label}
  </span>
);

const StatusBadge: React.FC<{ label: string }> = ({ label }) => (
  <span
    role="img"
    aria-label={`Status: ${label}`}
    style={{
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
      backgroundColor: tokens.colorBrandBackground2,
      color: tokens.colorBrandForeground1,
      flexShrink: 0,
    }}
  >
    {label}
  </span>
);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRecordCardProps {
  /** The Fluent UI icon component to render in the 40px circle */
  icon: FluentIcon;
  /** Accessible label for the icon */
  iconLabel: string;
  /** Entity logical name for opening dialog (e.g. "sprk_matter") */
  entityName: string;
  /** Record GUID for opening dialog */
  entityId: string;
  /** Type badge text (e.g. "Litigation", "Trademark") — optional */
  typeBadge?: string;
  /** Primary title text */
  title: string;
  /** Additional fields to show on the primary row after the title */
  primaryFields?: string[];
  /** Status badge text (shown on secondary row) — optional */
  statusBadge?: string;
  /** Description / secondary text */
  description?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecordCard: React.FC<IRecordCardProps> = React.memo(
  ({
    icon: IconComponent,
    iconLabel,
    entityName,
    entityId,
    typeBadge,
    title,
    primaryFields,
    statusBadge,
    description,
  }) => {
    const styles = useStyles();

    const handleCardClick = React.useCallback(() => {
      openRecordDialog(entityName, entityId);
    }, [entityName, entityId]);

    const handleCardKeyDown = React.useCallback(
      (e: React.KeyboardEvent) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          openRecordDialog(entityName, entityId);
        }
      },
      [entityName, entityId]
    );

    const handleEdit = React.useCallback(() => {
      openRecordDialog(entityName, entityId);
    }, [entityName, entityId]);

    const handleEmail = React.useCallback(() => {
      console.info(`[RecordCard] Email action for ${entityName} ${entityId} (stub)`);
    }, [entityName, entityId]);

    const handleTeams = React.useCallback(() => {
      console.info(`[RecordCard] Teams action for ${entityName} ${entityId} (stub)`);
    }, [entityName, entityId]);

    const handleAISummary = React.useCallback(() => {
      console.info(`[RecordCard] AI Summary action for ${entityName} ${entityId} (stub)`);
    }, [entityName, entityId]);

    // Stop propagation on menu clicks so card click doesn't also fire
    const handleMenuClick = React.useCallback((e: React.MouseEvent) => {
      e.stopPropagation();
    }, []);

    const cardAriaLabel = [
      typeBadge ?? "",
      title,
      ...(primaryFields ?? []),
      statusBadge ? `Status: ${statusBadge}` : "",
      description ?? "",
    ]
      .filter(Boolean)
      .join(", ");

    return (
      <div
        className={styles.card}
        role="listitem"
        tabIndex={0}
        aria-label={cardAriaLabel}
        onClick={handleCardClick}
        onKeyDown={handleCardKeyDown}
      >
        <div className={styles.mainRow}>
          {/* Type icon in 40px circle */}
          <div
            className={styles.typeIconCircle}
            aria-label={iconLabel}
            role="img"
          >
            <IconComponent fontSize={20} />
          </div>

          {/* Content: 2 rows */}
          <div className={styles.contentColumn}>
            {/* Row 1: Type badge + Title + additional primary fields */}
            <div className={styles.primaryRow}>
              {typeBadge && <TypeBadge label={typeBadge} />}
              <Text as="span" size={400} className={styles.title}>
                {title}
              </Text>
              {primaryFields?.map((field, idx) => (
                <Text
                  key={idx}
                  as="span"
                  size={300}
                  className={styles.fieldText}
                >
                  {field}
                </Text>
              ))}
            </div>

            {/* Row 2: Status badge + Description */}
            <div className={styles.secondaryRow}>
              {statusBadge && <StatusBadge label={statusBadge} />}
              {description && (
                <Text as="span" size={200} className={styles.description}>
                  {description}
                </Text>
              )}
            </div>
          </div>

          {/* Actions: overflow menu */}
          {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
          <div className={styles.actionsColumn} onClick={handleMenuClick}>
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Tooltip content="More actions" relationship="label">
                  <Button
                    appearance="subtle"
                    size="medium"
                    icon={<MoreVerticalRegular aria-hidden="true" />}
                    aria-label="More actions"
                  />
                </Tooltip>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<EditRegular />} onClick={handleEdit}>
                    Edit
                  </MenuItem>
                  <MenuItem icon={<MailRegular />} onClick={handleEmail}>
                    Email
                  </MenuItem>
                  <MenuItem icon={<ChatRegular />} onClick={handleTeams}>
                    Teams Chat
                  </MenuItem>
                  <MenuItem icon={<SparkleRegular />} onClick={handleAISummary}>
                    AI Summary
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        </div>
      </div>
    );
  }
);

RecordCard.displayName = "RecordCard";
