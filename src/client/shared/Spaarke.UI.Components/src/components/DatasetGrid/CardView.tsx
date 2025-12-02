/**
 * CardView - Tile/Card layout for visual content  
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  Card,
  CardHeader,
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  Checkbox
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from "../../types";
import { ColumnRendererService } from "../../services/ColumnRendererService";

export interface ICardViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
  scrollBehavior: ScrollBehavior;
  loading: boolean;
  hasNextPage: boolean;
  loadNextPage: () => void;
}

const useStyles = makeStyles({
  root: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column"
  },
  scrollContainer: {
    flex: 1,
    overflow: "auto",
    padding: tokens.spacingVerticalM
  },
  cardGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
    gap: tokens.spacingHorizontalM
  },
  card: {
    cursor: "pointer",
    height: "240px"
  },
  cardHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS
  },
  cardContent: {
    padding: tokens.spacingVerticalM,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS
  },
  fieldRow: {
    display: "flex",
    justifyContent: "space-between"
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200
  },
  fieldValue: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold
  },
  loadingOverlay: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL
  },
  loadMoreButton: {
    margin: tokens.spacingVerticalM
  },
  emptyState: {
    padding: tokens.spacingVerticalXXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground3
  }
});

export const CardView: React.FC<ICardViewProps> = (props) => {
  const styles = useStyles();

  const isInfiniteScroll = React.useMemo(() => {
    if (props.scrollBehavior === "Infinite") return true;
    if (props.scrollBehavior === "Paged") return false;
    return props.records.length > 100;
  }, [props.scrollBehavior, props.records.length]);

  const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
    if (!isInfiniteScroll || !props.hasNextPage || props.loading) return;
    const container = e.currentTarget;
    const scrollPercentage = (container.scrollTop + container.clientHeight) / container.scrollHeight;
    if (scrollPercentage > 0.9) props.loadNextPage();
  }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);

  const handleCardSelect = React.useCallback((recordId: string, checked: boolean) => {
    if (checked) {
      props.onSelectionChange([...props.selectedRecordIds, recordId]);
    } else {
      props.onSelectionChange(props.selectedRecordIds.filter(id => id !== recordId));
    }
  }, [props]);

  const handleCardClick = React.useCallback((record: IDatasetRecord, e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('input[type="checkbox"]')) return;
    props.onRecordClick(record);
  }, [props]);

  // Filter readable columns and take first 3 for card display
  const displayColumns = React.useMemo(() =>
    props.columns.filter(col => col.canRead !== false).slice(0, 3),
    [props.columns]
  );

  if (props.records.length === 0 && !props.loading) {
    return <div className={styles.emptyState}><p>No records to display</p></div>;
  }

  return (
    <div className={styles.root}>
      <div className={styles.scrollContainer} onScroll={handleScroll}>
        <div className={styles.cardGrid}>
          {props.records.map((record) => {
            const isSelected = props.selectedRecordIds.includes(record.id);
            const primaryField = displayColumns[0];
            const primaryValue = primaryField ? String(record[primaryField.name] || "") : record.id;

            return (
              <Card key={record.id} className={styles.card} onClick={(e) => handleCardClick(record, e)}>
                <CardHeader
                  header={
                    <div className={styles.cardHeader}>
                      <Checkbox checked={isSelected} onChange={(_e, data) => handleCardSelect(record.id, !!data.checked)} />
                      <Text weight="semibold" truncate>{primaryValue}</Text>
                    </div>
                  }
                />
                <div className={styles.cardContent}>
                  {displayColumns.slice(1).map((col) => {
                    const renderer = ColumnRendererService.getRenderer(col);
                    const renderedValue = renderer(record[col.name], record, col);

                    return (
                      <div key={col.name} className={styles.fieldRow}>
                        <Text className={styles.fieldLabel}>{col.displayName}:</Text>
                        <div className={styles.fieldValue}>{renderedValue}</div>
                      </div>
                    );
                  })}
                </div>
              </Card>
            );
          })}
        </div>
      </div>

      {isInfiniteScroll && props.loading && (
        <div className={styles.loadingOverlay}>
          <Spinner size="small" label="Loading more records..." />
        </div>
      )}

      {!isInfiniteScroll && props.hasNextPage && !props.loading && (
        <Button appearance="subtle" className={styles.loadMoreButton} onClick={props.loadNextPage}>
          Load More ({props.records.length} records loaded)
        </Button>
      )}

      {!isInfiniteScroll && props.loading && (
        <div className={styles.loadingOverlay}>
          <Spinner size="small" label="Loading..." />
        </div>
      )}
    </div>
  );
};
