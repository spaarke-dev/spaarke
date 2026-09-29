import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Toolbar,
  ToolbarButton,
  Tooltip,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Badge,
  TableRowId,
} from "@fluentui/react-components";
import {
  ArrowClockwise20Regular,
  ArrowUndo20Regular,
  Delete20Regular,
  CheckmarkCircle16Filled,
  DismissCircle16Filled,
} from "@fluentui/react-icons";
import { ConfirmModal } from "@spaarke/ui-components";
import type { RecycleBinItem, RecycleBinItemActionResult } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { describeApiError } from "../../services/speApiClient";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Formats a byte count.
 *
 * `null` renders as "—" and NEVER as "0 B". Graph not reporting a size and an item genuinely
 * occupying no bytes are different facts, and the whole reason this project exists is that the app
 * used to collapse "not reported" into a confident-looking value.
 */
function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes === undefined) return "—";
  if (bytes === 0) return "0 B";

  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

/** Formats an ISO timestamp, or "—" when Graph did not report one. */
function formatTimestamp(iso: string | null): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalM,
  },
  intro: {
    color: tokens.colorNeutralForeground3,
  },
  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    rowGap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    rowGap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },
  outcomeList: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalS,
  },
  outcomeRow: {
    display: "flex",
    alignItems: "flex-start",
    columnGap: tokens.spacingHorizontalXS,
  },
  outcomeOk: {
    color: tokens.colorPaletteGreenForeground1,
    flexShrink: 0,
    marginTop: "2px",
  },
  outcomeFail: {
    color: tokens.colorPaletteRedForeground1,
    flexShrink: 0,
    marginTop: "2px",
  },
  outcomeName: {
    fontFamily: tokens.fontFamilyMonospace,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Outcome report
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Renders the per-item outcome of a restore or permanent delete.
 *
 * ⚠️ This component is the acceptance criterion, not decoration. Graph reports both operations in
 * ways that hide failure — restore returns 207 listing only the ids that SUCCEEDED, and permanent
 * delete returns 204 whether it purged everything, some, or nothing. Showing only the summary line
 * would reintroduce the collapse the BFF works to prevent, one layer higher.
 *
 * Successes AND failures are both named, because "3 of 5 restored" without saying which three
 * leaves the admin no better off than a bare failure.
 */
const OutcomeReport: React.FC<{
  result: RecycleBinItemActionResult;
  onDismiss: () => void;
}> = ({ result, onDismiss }) => {
  const styles = useStyles();

  const allSucceeded = result.succeededCount === result.requestedCount;

  // Unverified outranks everything: we do not know what happened, and saying either "done" or
  // "failed" would assert something we did not observe.
  const intent = !result.verified ? "warning" : allSucceeded ? "success" : "warning";

  const title = !result.verified
    ? "Result could not be verified"
    : allSucceeded
      ? "Completed"
      : `Completed with differences — ${result.succeededCount} of ${result.requestedCount}`;

  return (
    <MessageBar intent={intent} onClick={undefined}>
      <MessageBarBody>
        <MessageBarTitle>{title}</MessageBarTitle>
        <div>{result.summary}</div>

        <div className={styles.outcomeList}>
          {result.outcomes.map(outcome => (
            <div key={outcome.id} className={styles.outcomeRow}>
              {outcome.succeeded ? (
                <CheckmarkCircle16Filled className={styles.outcomeOk} />
              ) : (
                <DismissCircle16Filled className={styles.outcomeFail} />
              )}
              <Text size={200}>
                <span className={outcome.name ? undefined : styles.outcomeName}>
                  {outcome.name ?? outcome.id}
                </span>
                {" — "}
                <span className={styles.muted}>{outcome.detail}</span>
              </Text>
            </div>
          ))}
        </div>

        <div style={{ marginTop: tokens.spacingVerticalS }}>
          <ToolbarButton onClick={onDismiss}>Dismiss</ToolbarButton>
        </div>
      </MessageBarBody>
    </MessageBar>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export interface ContainerItemRecycleBinProps {
  /** The container whose recycle bin is shown. */
  containerId: string;
  /** Container type config the container belongs to. */
  configId: string;
  /** Display name, used in the permanent-delete confirmation copy. */
  containerName?: string;
  /** Whether this surface is currently visible — gates the initial load. */
  isActive: boolean;
}

/**
 * The per-container ITEM recycle bin — deleted files and folders inside one container
 * (spec FR-E03, task 052).
 *
 * ⚠️ **Not** the deleted-CONTAINERS recycle bin (`RecycleBinPage`). Spec decision D3 keeps both:
 * a container-level restore cannot recover one deleted file, and an item-level restore cannot
 * recover a deleted container. Do not merge these surfaces.
 */
export const ContainerItemRecycleBin: React.FC<ContainerItemRecycleBinProps> = ({
  containerId,
  configId,
  containerName,
  isActive,
}) => {
  const styles = useStyles();

  const [items, setItems] = React.useState<RecycleBinItem[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [hasLoaded, setHasLoaded] = React.useState(false);

  const [selectedRows, setSelectedRows] = React.useState<Set<TableRowId>>(new Set());
  const [actionInProgress, setActionInProgress] = React.useState(false);
  const [actionError, setActionError] = React.useState<string | null>(null);
  const [result, setResult] = React.useState<RecycleBinItemActionResult | null>(null);
  const [confirmDelete, setConfirmDelete] = React.useState(false);

  const selectedIds = React.useMemo(
    () => Array.from(selectedRows).map(String),
    [selectedRows],
  );

  const selectedItems = React.useMemo(
    () => items.filter(i => selectedRows.has(i.id)),
    [items, selectedRows],
  );

  const load = React.useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    try {
      const loaded = await speApiClient.recycleBinItems.list(containerId, configId);
      setItems(loaded);
      // Drop any selection that no longer exists, so a stale id cannot be sent to an
      // irreversible endpoint.
      setSelectedRows(prev => new Set([...prev].filter(id => loaded.some(i => i.id === id))));
    } catch (err) {
      setItems([]);
      setLoadError(describeApiError(err));
    } finally {
      setIsLoading(false);
      setHasLoaded(true);
    }
  }, [containerId, configId]);

  React.useEffect(() => {
    // Reset when the container changes so one container's bin is never shown under another's name.
    setHasLoaded(false);
    setItems([]);
    setSelectedRows(new Set());
    setResult(null);
    setActionError(null);
  }, [containerId]);

  React.useEffect(() => {
    if (isActive && !hasLoaded && !isLoading) {
      void load();
    }
  }, [isActive, hasLoaded, isLoading, load]);

  const runRestore = React.useCallback(async () => {
    setActionInProgress(true);
    setActionError(null);
    setResult(null);
    try {
      const actionResult = await speApiClient.recycleBinItems.restore(
        containerId,
        selectedIds,
        configId,
      );
      setResult(actionResult);
      setSelectedRows(new Set());
      await load();
    } catch (err) {
      // A 409 lands here: Graph refused the whole batch and NOTHING was restored. The BFF's
      // ProblemDetails already says so and names the remediation, so it is surfaced verbatim
      // rather than re-explained by the client.
      setActionError(describeApiError(err));
    } finally {
      setActionInProgress(false);
    }
  }, [containerId, configId, selectedIds, load]);

  const runPermanentDelete = React.useCallback(async () => {
    setActionInProgress(true);
    setActionError(null);
    setResult(null);
    try {
      const actionResult = await speApiClient.recycleBinItems.permanentDelete(
        containerId,
        selectedIds,
        configId,
      );
      setResult(actionResult);
      setSelectedRows(new Set());
      setConfirmDelete(false);
      await load();
    } catch (err) {
      setActionError(describeApiError(err));
      setConfirmDelete(false);
    } finally {
      setActionInProgress(false);
    }
  }, [containerId, configId, selectedIds, load]);

  const columns: TableColumnDefinition<RecycleBinItem>[] = React.useMemo(
    () => [
      createTableColumn<RecycleBinItem>({
        columnId: "name",
        renderHeaderCell: () => "Name",
        renderCell: item => <TableCellLayout truncate>{item.name || "—"}</TableCellLayout>,
      }),
      createTableColumn<RecycleBinItem>({
        columnId: "deletedDateTime",
        renderHeaderCell: () => "Deleted",
        renderCell: item => (
          <TableCellLayout truncate>{formatTimestamp(item.deletedDateTime)}</TableCellLayout>
        ),
      }),
      createTableColumn<RecycleBinItem>({
        columnId: "deletedBy",
        renderHeaderCell: () => "Deleted by",
        renderCell: item => (
          <TableCellLayout truncate>
            {/* Null means Graph did not report it — shown as an explicit absent state so it stays
                distinguishable from a real name. */}
            {item.deletedByDisplayName ?? <span className={styles.muted}>Not reported</span>}
          </TableCellLayout>
        ),
      }),
      createTableColumn<RecycleBinItem>({
        columnId: "size",
        renderHeaderCell: () => "Size",
        renderCell: item => <TableCellLayout truncate>{formatBytes(item.size)}</TableCellLayout>,
      }),
      createTableColumn<RecycleBinItem>({
        columnId: "deletedFromLocation",
        renderHeaderCell: () => "Deleted from",
        renderCell: item => (
          <TableCellLayout truncate>
            {item.deletedFromLocation ?? <span className={styles.muted}>Not reported</span>}
          </TableCellLayout>
        ),
      }),
    ],
    [styles.muted],
  );

  return (
    <div className={styles.root}>
      <Text size={200} className={styles.intro}>
        Files and folders deleted from this container. This is separate from the{" "}
        <strong>Recycle Bin</strong> screen, which lists deleted containers.
      </Text>

      <Toolbar size="small">
        <Tooltip content="Reload the recycle bin" relationship="label">
          <ToolbarButton
            icon={<ArrowClockwise20Regular />}
            onClick={() => void load()}
            disabled={isLoading || actionInProgress}
          >
            Refresh
          </ToolbarButton>
        </Tooltip>

        <Tooltip content="Restore the selected items to their original location" relationship="label">
          <ToolbarButton
            icon={<ArrowUndo20Regular />}
            onClick={() => void runRestore()}
            disabled={selectedIds.length === 0 || isLoading || actionInProgress}
          >
            Restore
          </ToolbarButton>
        </Tooltip>

        <Tooltip content="Permanently delete the selected items — cannot be undone" relationship="label">
          <ToolbarButton
            icon={<Delete20Regular />}
            onClick={() => setConfirmDelete(true)}
            disabled={selectedIds.length === 0 || isLoading || actionInProgress}
          >
            Delete permanently
          </ToolbarButton>
        </Tooltip>

        {selectedIds.length > 0 && (
          <Badge appearance="tint" color="informative">
            {selectedIds.length} selected
          </Badge>
        )}
      </Toolbar>

      {actionError && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Action failed</MessageBarTitle>
            {actionError}
          </MessageBarBody>
        </MessageBar>
      )}

      {result && <OutcomeReport result={result} onDismiss={() => setResult(null)} />}

      {isLoading ? (
        <div className={styles.feedback}>
          <Spinner size="medium" label="Loading recycle bin…" />
        </div>
      ) : loadError ? (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not load the recycle bin</MessageBarTitle>
            {loadError}
          </MessageBarBody>
        </MessageBar>
      ) : items.length === 0 ? (
        /* An empty bin is a successful result. It must not look like the failure branch above —
           acceptance criterion 6. */
        <div className={styles.emptyState}>
          <Text weight="semibold">The recycle bin is empty</Text>
          <Text size={200}>Nothing has been deleted from this container, or it has all been purged.</Text>
        </div>
      ) : (
        <DataGrid
          items={items}
          columns={columns}
          getRowId={item => item.id}
          selectionMode="multiselect"
          selectedItems={selectedRows}
          onSelectionChange={(_, data) => setSelectedRows(new Set(data.selectedItems))}
          resizableColumns
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<RecycleBinItem>>
            {({ item, rowId }) => (
              <DataGridRow<RecycleBinItem> key={rowId}>
                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      )}

      {/*
        ── Permanent-delete confirmation (ADR-050 canonical ConfirmModal) ──

        The items are NAMED, not counted. "Delete 4 items?" is not a confirmation an admin can act
        on — it asks them to trust their own memory of what they selected, for an operation with no
        undo. The consequence is stated before the action and in plain words.
      */}
      <ConfirmModal
        open={confirmDelete}
        busy={actionInProgress}
        onClose={() => {
          if (!actionInProgress) setConfirmDelete(false);
        }}
        onConfirm={() => void runPermanentDelete()}
        title="Permanently delete these items?"
        destructive
        confirmLabel={actionInProgress ? "Deleting…" : "Delete permanently"}
        message={
          <>
            <Text weight="semibold">
              This cannot be undone. The following {selectedItems.length} item
              {selectedItems.length !== 1 ? "s" : ""} will be destroyed
              {containerName ? ` in ${containerName}` : ""}:
            </Text>
            <ul>
              {selectedItems.map(item => (
                <li key={item.id}>
                  {item.name || item.id}
                  <span className={styles.muted}> — {formatBytes(item.size)}</span>
                </li>
              ))}
            </ul>
            Once purged, these items cannot be restored from the recycle bin or recovered by
            Spaarke, Microsoft, or a support request.
          </>
        }
      />
    </div>
  );
};
