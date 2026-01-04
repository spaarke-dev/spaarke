/**
 * MiniTable Component
 * Compact ranked table using Fluent UI v9 Table
 * Supports click-to-drill for viewing underlying records
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Table,
  TableHeader,
  TableRow,
  TableHeaderCell,
  TableBody,
  TableCell,
  TableCellLayout,
  mergeClasses,
} from "@fluentui/react-components";
import type { DrillInteraction } from "../../types";

export interface IMiniTableColumn {
  /** Column key */
  key: string;
  /** Column header */
  header: string;
  /** Column width (optional) */
  width?: string;
  /** Whether this is the value column (will be right-aligned) */
  isValue?: boolean;
}

export interface IMiniTableItem {
  /** Unique identifier */
  id: string;
  /** Display values by column key */
  values: Record<string, string | number>;
  /** Field value for drill interaction */
  fieldValue: unknown;
}

export interface IMiniTableProps {
  /** Items to display */
  items: IMiniTableItem[];
  /** Column definitions */
  columns: IMiniTableColumn[];
  /** Table title */
  title?: string;
  /** Maximum number of items to show */
  topN?: number;
  /** Whether to show rank numbers */
  showRank?: boolean;
  /** Callback when a row is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Whether rows should be interactive */
  interactive?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    gap: tokens.spacingVerticalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    marginBottom: tokens.spacingVerticalXS,
  },
  tableWrapper: {
    overflowX: "auto",
  },
  table: {
    minWidth: "100%",
    tableLayout: "auto",
  },
  headerCell: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  row: {
    cursor: "default",
  },
  rowInteractive: {
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    "&:active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  rankCell: {
    width: "40px",
    textAlign: "center",
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightMedium,
  },
  valueCell: {
    textAlign: "right",
    fontWeight: tokens.fontWeightMedium,
    color: tokens.colorNeutralForeground1,
  },
  cell: {
    fontSize: tokens.fontSizeBase200,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
  },
  placeholder: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * MiniTable - Renders a compact ranked table
 */
export const MiniTable: React.FC<IMiniTableProps> = ({
  items,
  columns,
  title,
  topN = 5,
  showRank = true,
  onDrillInteraction,
  drillField,
  interactive = true,
}) => {
  const styles = useStyles();

  const handleRowClick = (item: IMiniTableItem, rank: number) => {
    if (interactive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: item.fieldValue,
        label: `#${rank} - ${item.values[columns[0]?.key] || item.id}`,
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent, item: IMiniTableItem, rank: number) => {
    if (interactive && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      handleRowClick(item, rank);
    }
  };

  if (!items || items.length === 0) {
    return (
      <div className={styles.container}>
        {title && <Text className={styles.title}>{title}</Text>}
        <div className={styles.placeholder}>
          <Text>No data available</Text>
        </div>
      </div>
    );
  }

  const displayItems = items.slice(0, topN);
  const isInteractive = interactive && onDrillInteraction && drillField;

  return (
    <div className={styles.container}>
      {title && <Text className={styles.title}>{title}</Text>}
      <div className={styles.tableWrapper}>
        <Table className={styles.table} size="small">
          <TableHeader>
            <TableRow>
              {showRank && (
                <TableHeaderCell className={mergeClasses(styles.headerCell, styles.rankCell)}>
                  #
                </TableHeaderCell>
              )}
              {columns.map((column) => (
                <TableHeaderCell
                  key={column.key}
                  className={mergeClasses(
                    styles.headerCell,
                    column.isValue && styles.valueCell
                  )}
                  style={column.width ? { width: column.width } : undefined}
                >
                  {column.header}
                </TableHeaderCell>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {displayItems.map((item, index) => {
              const rank = index + 1;
              return (
                <TableRow
                  key={item.id}
                  className={mergeClasses(
                    styles.row,
                    isInteractive && styles.rowInteractive
                  )}
                  onClick={isInteractive ? () => handleRowClick(item, rank) : undefined}
                  onKeyDown={isInteractive ? (e) => handleKeyDown(e, item, rank) : undefined}
                  tabIndex={isInteractive ? 0 : undefined}
                  aria-label={`Rank ${rank}: ${item.values[columns[0]?.key] || item.id}`}
                >
                  {showRank && (
                    <TableCell className={mergeClasses(styles.cell, styles.rankCell)}>
                      {rank}
                    </TableCell>
                  )}
                  {columns.map((column) => (
                    <TableCell
                      key={column.key}
                      className={mergeClasses(
                        styles.cell,
                        column.isValue && styles.valueCell
                      )}
                    >
                      <TableCellLayout>
                        {item.values[column.key] ?? "-"}
                      </TableCellLayout>
                    </TableCell>
                  ))}
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};
