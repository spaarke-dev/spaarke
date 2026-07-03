/**
 * CreatedByPopover — Fluent v9 Popover triggered by an "i" info icon.
 *
 * Per FR-18: subtle icon-only trigger with tooltip; popover surface shows
 * "Created by {name}" + "on {formattedDate}". Date formatted via
 * Intl.DateTimeFormat(undefined, …) so browser locale wins. Fallbacks:
 * "Unknown user" (missing createdBy) and "on unknown date" (missing/invalid
 * createdOn). Fluent v9 defaults handle Escape / outside-click close.
 *
 * @see spec.md FR-18
 * @see ADR-021 Fluent UI v9 design system
 */

import * as React from "react";
import {
  Button, Popover, PopoverSurface, PopoverTrigger, Text, Tooltip,
  makeStyles, tokens,
} from "@fluentui/react-components";
import { Info24Regular } from "@fluentui/react-icons";

export interface ICreatedByPopoverProps {
  createdBy: { id: string; name: string } | null | undefined;
  createdOn: string | null | undefined;
  disabled?: boolean;
}

const useStyles = makeStyles({
  surface: {
    minWidth: "220px", maxWidth: "320px",
    display: "flex", flexDirection: "column",
    rowGap: tokens.spacingVerticalXS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  label: { fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold },
  value: { fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightRegular },
});

function formatDate(iso: string | null | undefined): string {
  if (!iso) return "unknown date";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "unknown date";
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(d);
  } catch { return "unknown date"; }
}

export const CreatedByPopover: React.FC<ICreatedByPopoverProps> = ({ createdBy, createdOn, disabled = false }) => {
  const styles = useStyles();
  const nameText = createdBy?.name?.trim() ? createdBy.name : "Unknown user";
  const dateText = formatDate(createdOn);
  return (
    <Popover>
      <PopoverTrigger disableButtonEnhancement>
        <Tooltip content="Memo info" relationship="label" withArrow>
          <Button appearance="subtle" icon={<Info24Regular />} disabled={disabled}
            aria-label="Memo info" data-testid="created-by-trigger" />
        </Tooltip>
      </PopoverTrigger>
      <PopoverSurface className={styles.surface} data-testid="created-by-surface">
        <Text className={styles.label}>Created by {nameText}</Text>
        <Text className={styles.value}>on {dateText}</Text>
      </PopoverSurface>
    </Popover>
  );
};
