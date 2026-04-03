/**
 * RecordCard — generic card for entity records (Matters, Projects, Invoices).
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Handles entity-specific content (badges, fields) and overflow menu.
 *
 * Card click opens a dialog main form via Xrm.Navigation.openForm (target: 2).
 */

import * as React from "react";
import {
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
import { RecordCardShell, CardIcon } from "@spaarke/ui-components";
import { openRecordDialog } from "../../utils/navigation";

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

const TypeBadge: React.FC<{ label: string }> = ({ label }) => (
  <span
    role="img"
    aria-label={`Type: ${label}`}
    style={{
      ...badgeBase,
      backgroundColor: tokens.colorNeutralBackground3,
      color: tokens.colorNeutralForeground2,
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
      ...badgeBase,
      backgroundColor: tokens.colorBrandBackground2,
      color: tokens.colorBrandForeground1,
    }}
  >
    {label}
  </span>
);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRecordCardProps {
  icon: FluentIcon;
  iconLabel: string;
  entityName: string;
  entityId: string;
  typeBadge?: string;
  title: string;
  primaryFields?: string[];
  statusBadge?: string;
  description?: string;
}

// ---------------------------------------------------------------------------
// Styles (content-specific only — layout handled by RecordCardShell)
// ---------------------------------------------------------------------------

const titleStyle: React.CSSProperties = {
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  color: tokens.colorNeutralForeground1,
  fontWeight: tokens.fontWeightSemibold,
  flexShrink: 0,
  maxWidth: "50%",
};

const fieldStyle: React.CSSProperties = {
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  color: tokens.colorNeutralForeground3,
  flexShrink: 1,
  minWidth: 0,
};

const descriptionStyle: React.CSSProperties = {
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  color: tokens.colorNeutralForeground3,
  flex: "1 1 0",
  minWidth: 0,
};

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
    const handleClick = React.useCallback(() => {
      openRecordDialog(entityName, entityId);
    }, [entityName, entityId]);

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

    const ariaLabel = [
      typeBadge ?? "",
      title,
      ...(primaryFields ?? []),
      statusBadge ? `Status: ${statusBadge}` : "",
      description ?? "",
    ]
      .filter(Boolean)
      .join(", ");

    return (
      <RecordCardShell
        icon={
          <CardIcon>
            <IconComponent fontSize={20} aria-label={iconLabel} />
          </CardIcon>
        }
        primaryContent={
          <>
            {typeBadge && <TypeBadge label={typeBadge} />}
            <Text as="span" size={400} style={titleStyle}>
              {title}
            </Text>
            {primaryFields?.map((field, idx) => (
              <Text key={idx} as="span" size={300} style={fieldStyle}>
                {field}
              </Text>
            ))}
          </>
        }
        secondaryContent={
          (statusBadge || description) ? (
            <>
              {statusBadge && <StatusBadge label={statusBadge} />}
              {description && (
                <Text as="span" size={200} style={descriptionStyle}>
                  {description}
                </Text>
              )}
            </>
          ) : undefined
        }
        overflowMenu={
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
                <MenuItem icon={<EditRegular />} onClick={handleEdit}>Edit</MenuItem>
                <MenuItem icon={<MailRegular />} onClick={handleEmail}>Email</MenuItem>
                <MenuItem icon={<ChatRegular />} onClick={handleTeams}>Teams Chat</MenuItem>
                <MenuItem icon={<SparkleRegular />} onClick={handleAISummary}>AI Summary</MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        }
        onClick={handleClick}
        ariaLabel={ariaLabel}
      />
    );
  }
);

RecordCard.displayName = "RecordCard";
