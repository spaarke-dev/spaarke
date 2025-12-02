import * as React from "react";
import { FixedSizeList as List } from "react-window";
import {
  makeStyles,
  tokens,
  shorthands
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types/DatasetTypes";
import { ColumnRendererService } from "../../services/ColumnRendererService";

const useStyles = makeStyles({
  listContainer: {
    width: "100%",
    height: "100%"
  },
  row: {
    display: "flex",
    alignItems: "center",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    ...shorthands.padding("8px", "16px"),
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover
    }
  },
  rowSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected
    }
  },
  primaryColumn: {
    flex: 1,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1
  },
  secondaryColumn: {
    flex: 1,
    color: tokens.colorNeutralForeground2
  }
});

export interface VirtualizedListViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  itemHeight: number;
  overscanCount: number;
  onRecordClick?: (recordId: string) => void;
}

export const VirtualizedListView: React.FC<VirtualizedListViewProps> = (props) => {
  const styles = useStyles();
  const listRef = React.useRef<List>(null);

  // Get primary and first secondary column
  const primaryColumn = props.columns.find(c => c.isPrimary) ?? props.columns[0];
  const secondaryColumn = props.columns.find(c => !c.isPrimary && c.name !== primaryColumn?.name);

  const renderRow = React.useCallback(({ index, style }: { index: number; style: React.CSSProperties }) => {
    const record = props.records[index];
    const isSelected = props.selectedRecordIds.includes(record.id);

    const primaryRenderer = ColumnRendererService.getRenderer(primaryColumn);
    const primaryValue = primaryRenderer(record[primaryColumn.name], record, primaryColumn);

    let secondaryValue: React.ReactElement | string | null = null;
    if (secondaryColumn) {
      const secondaryRenderer = ColumnRendererService.getRenderer(secondaryColumn);
      secondaryValue = secondaryRenderer(record[secondaryColumn.name], record, secondaryColumn);
    }

    return (
      <div
        style={style}
        className={`${styles.row} ${isSelected ? styles.rowSelected : ""}`}
        onClick={() => props.onRecordClick?.(record.id)}
      >
        <div className={styles.primaryColumn}>{primaryValue}</div>
        {secondaryValue && (
          <div className={styles.secondaryColumn}>{secondaryValue}</div>
        )}
      </div>
    );
  }, [props.records, props.selectedRecordIds, props.onRecordClick, primaryColumn, secondaryColumn, styles]);

  return (
    <div className={styles.listContainer}>
      <List
        ref={listRef}
        height={600} // Will be calculated from parent container
        itemCount={props.records.length}
        itemSize={props.itemHeight}
        width="100%"
        overscanCount={props.overscanCount}
      >
        {renderRow}
      </List>
    </div>
  );
};
