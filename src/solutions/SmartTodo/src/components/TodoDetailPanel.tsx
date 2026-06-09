/**
 * TodoDetailPanel — Wrapper that loads a single `sprk_todo` record and wires
 * save / dismiss / regarding-edit callbacks to the shared TodoDetail component
 * via TodoContext.
 *
 * Single-entity model (R3 FR-09 / task 011): the legacy two-entity load
 * (sprk_event + sprk_eventtodo) is removed (OS-1). The new `TodoDetail` API
 * exposes `onSaveTodo(todoId, ITodoFieldUpdates)` + `onDismissTodo(todoId)`
 * — see `projects/smart-todo-decoupling-r3/notes/tododetail-consumer-breakage-task011.md`.
 *
 * Regarding edit (FR-13, task 022): when the user clicks "Change" / "Set
 * Regarding…" / "Clear" on the regarding row, the host opens an inline dialog
 * containing the shared `AssociateToStep` (with the canonical 11-target
 * preset). On selection, the host calls `buildTodoRegardingUpdate` to produce
 * a payload that atomically sets the four resolver fields + the chosen
 * entity-specific lookup AND clears the other ten lookups (per ADR-024 +
 * the spec's mutual-exclusion rule). The payload is then persisted via the
 * existing save path.
 *
 * Responsibilities:
 *   - Consume the selected todo ID from TodoContext (`selectedEventId` —
 *     property name retained; value is a sprk_todoid).
 *   - Load one ITodoRecord from Dataverse.
 *   - Wrap shared TodoDetail with save / dismiss / regarding-edit callbacks
 *     that write to Dataverse AND call TodoContext.updateItem for optimistic
 *     Kanban refresh.
 *   - Render header bar with todo name + close (X) button.
 *   - Host the AssociateToStep dialog for regarding edit (FR-13).
 *
 * Per ADR-006: no BroadcastChannel — all communication goes through TodoContext.
 * Per ADR-012: TodoDetail comes from @spaarke/ui-components (context-agnostic).
 * Per ADR-021: Fluent UI v9 design system (makeStyles + tokens only).
 * Per ADR-024: regarding edits go through PolymorphicResolverService —
 *              wrapped here in buildTodoRegardingUpdate which adds the
 *              clear-and-set semantic across all 11 lookups (FR-13).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  DialogTrigger,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { TodoDetail } from "@spaarke/ui-components/TodoDetail";
import type {
  ITodoRecord,
  ITodoFieldUpdates,
} from "@spaarke/ui-components/TodoDetail";
import { AssociateToStep, TODO_REGARDING_TARGETS } from "@spaarke/ui-components/AssociateToStep";
import type {
  AssociationResult,
  EntityTypeOption,
  INavigationService,
} from "@spaarke/ui-components/AssociateToStep";
import type {
  LookupOptions,
  LookupResult,
} from "@spaarke/ui-components";
import {
  buildTodoRegardingUpdate,
  buildTodoRegardingClear,
  discoverTodoNavProps,
} from "@spaarke/ui-components/services";
import { useTodoContext } from "../context/TodoContext";
import {
  loadTodoRecord,
  saveTodoFields,
  dismissTodo,
  searchContacts,
} from "../services/todoDetailService";
import { getXrm, getWebApi } from "../services/xrmProvider";

// ---------------------------------------------------------------------------
// statuscode constants (sprk_todo per task 009)
// ---------------------------------------------------------------------------

const STATUSCODE_DISMISSED = 659490002;
const STATECODE_INACTIVE = 1;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    gap: tokens.spacingHorizontalS,
  },
  headerTitle: {
    flex: "1 1 0",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  body: {
    flex: "1 1 0",
    overflow: "auto",
    minHeight: 0,
  },
  emptyState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorNeutralForeground3,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  loadingState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
  },
  errorState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorPaletteRedForeground1,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Inline INavigationService adapter (Xrm.Utility.lookupObjects-backed)
//
// AssociateToStep requires an INavigationService for the lookup dialog. We
// build one inline (rather than importing a global adapter) so SmartTodo
// doesn't take on a transitive dependency on a navigation-service module —
// the only method we use is `openLookup`.
// ---------------------------------------------------------------------------

function createXrmNavigationService(): INavigationService {
  return {
    openRecord: async (entityName: string, entityId: string) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = getXrm() as any;
      await xrm?.Navigation?.navigateTo?.(
        { pageType: "entityrecord", entityName, entityId },
        { target: 1 },
      );
    },
    openDialog: async () => ({ confirmed: false }),
    closeDialog: () => {
      /* no-op */
    },
    openLookup: async (options: LookupOptions): Promise<LookupResult[]> => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = getXrm() as any;
      if (!xrm?.Utility?.lookupObjects) {
        console.warn("[TodoDetailPanel] Xrm.Utility.lookupObjects unavailable");
        return [];
      }
      const lookupOptions = {
        defaultEntityType: options.defaultEntityType ?? options.entityType,
        entityTypes: options.entityTypes ?? [options.entityType],
        allowMultiSelect: options.allowMultiSelect ?? false,
        defaultViewId: (options as { defaultViewId?: string }).defaultViewId,
      };
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const results = await xrm.Utility.lookupObjects(lookupOptions);
        if (!results || !Array.isArray(results)) return [];
        return results.map(
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          (r: any): LookupResult => ({
            id: String(r.id ?? "")
              .replace(/[{}]/g, "")
              .toLowerCase(),
            name: String(r.name ?? "Unknown"),
            entityType: String(r.entityType ?? options.entityType),
          }),
        );
      } catch (err) {
        console.error("[TodoDetailPanel] Lookup dialog error:", err);
        return [];
      }
    },
  };
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function TodoDetailPanel(): React.ReactElement {
  const styles = useStyles();
  const { selectedEventId, selectItem, updateItem, handleRemove, items } =
    useTodoContext();

  // ---------------------------------------------------------------------------
  // Data state
  // ---------------------------------------------------------------------------
  const [record, setRecord] = React.useState<ITodoRecord | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Regarding-edit dialog state (FR-13)
  // ---------------------------------------------------------------------------
  const [isRegardingDialogOpen, setIsRegardingDialogOpen] = React.useState(false);
  const [regardingSelection, setRegardingSelection] =
    React.useState<AssociationResult | null>(null);
  const [regardingDialogError, setRegardingDialogError] = React.useState<string | null>(null);
  const [isApplyingRegarding, setIsApplyingRegarding] = React.useState(false);

  // Inline navigation-service singleton (stable identity across renders)
  const navigationService = React.useMemo<INavigationService>(
    () => createXrmNavigationService(),
    [],
  );

  // ---------------------------------------------------------------------------
  // Load record when selectedEventId (sprk_todoid) changes
  // ---------------------------------------------------------------------------
  React.useEffect(() => {
    if (!selectedEventId) {
      setRecord(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    loadTodoRecord(selectedEventId).then((result) => {
      if (cancelled) return;
      if (result.success && result.data) {
        setRecord(result.data);
      } else {
        setError(result.error ?? "Failed to load record");
      }
      setIsLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [selectedEventId]);

  // ---------------------------------------------------------------------------
  // Close handler — deselects the item
  // ---------------------------------------------------------------------------
  const handleClose = React.useCallback(() => {
    selectItem(null);
  }, [selectItem]);

  // ---------------------------------------------------------------------------
  // Save sprk_todo fields + optimistic Kanban update
  // ---------------------------------------------------------------------------
  const handleSaveTodo = React.useCallback(
    async (todoId: string, fields: ITodoFieldUpdates) => {
      // Capture previous item state for rollback
      const previousItem = items.find((i) => i.sprk_todoid === todoId);

      // Optimistic update — Kanban card re-renders immediately (NFR-02)
      updateItem(todoId, fields as Partial<Record<string, unknown>>);
      setRecord((prev: ITodoRecord | null) =>
        prev ? { ...prev, ...fields } : prev,
      );

      // Persist to Dataverse in background
      const result = await saveTodoFields(todoId, fields);
      if (!result.success) {
        // Rollback optimistic update on failure
        if (previousItem) {
          updateItem(todoId, previousItem);
          setRecord((prev: ITodoRecord | null) =>
            prev ? { ...prev, ...previousItem } : prev,
          );
        }
        console.error(
          "[TodoDetailPanel] Todo save failed, reverted:",
          result.error,
        );
      }
      return result;
    },
    [updateItem, items],
  );

  // ---------------------------------------------------------------------------
  // Dismiss handler — set statecode=1 / statuscode=Dismissed + remove from
  // Kanban + close panel. Preserves the record so it can be recovered from
  // the DismissedSection (per task 011 follow-up note default behaviour).
  // ---------------------------------------------------------------------------
  const handleDismissTodo = React.useCallback(
    async (todoId: string) => {
      // Optimistic: drop from Kanban + close panel immediately
      const previousItem = items.find((i) => i.sprk_todoid === todoId);

      const result = await dismissTodo(todoId);
      if (result.success) {
        handleRemove(todoId);
        selectItem(null);
      } else if (previousItem) {
        // No rollback needed for the item itself (we hadn't removed it yet),
        // but log the failure for the host so it can surface an error.
        console.error(
          "[TodoDetailPanel] Dismiss failed:",
          result.error,
        );
      }
      return result;
    },
    [handleRemove, selectItem, items],
  );

  // ---------------------------------------------------------------------------
  // Search systemusers for the Assigned To picker
  // ---------------------------------------------------------------------------
  const handleSearchContacts = React.useCallback(
    async (query: string) => searchContacts(query),
    [],
  );

  // ---------------------------------------------------------------------------
  // Open regarding record — uses sprk_regardingrecordurl per FR-13.
  // Falls back to Xrm.Navigation.navigateTo only if URL is absent.
  // ---------------------------------------------------------------------------
  const handleOpenRegardingUrl = React.useCallback((url: string) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = getXrm() as any;
    // Prefer Xrm.Navigation.openUrl when available (uses host browser hooks)
    if (xrm?.Navigation?.openUrl) {
      try {
        xrm.Navigation.openUrl(url);
        return;
      } catch (err) {
        console.warn("[TodoDetailPanel] Xrm.Navigation.openUrl failed:", err);
      }
    }
    // Fallback: window.open in a new tab
    try {
      window.open(url, "_blank", "noopener,noreferrer");
    } catch (err) {
      console.warn("[TodoDetailPanel] window.open failed:", err);
    }
  }, []);

  // Legacy fallback (kept for hosts that haven't migrated to sprk_regardingrecordurl)
  const handleOpenRegardingRecord = React.useCallback(
    (entityName: string, recordId: string) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = getXrm() as any;
      if (xrm?.Navigation?.navigateTo) {
        xrm.Navigation.navigateTo(
          {
            pageType: "entityrecord",
            entityName,
            entityId: recordId,
          },
          { target: 1 }, // 1 = new window/tab
        ).catch(() => {
          // Fallback: silent — Xrm navigation failed
        });
      }
    },
    [],
  );

  // ---------------------------------------------------------------------------
  // Regarding edit handlers (FR-13)
  // ---------------------------------------------------------------------------

  /** Open the AssociateToStep dialog for change / set. */
  const handleChangeRegarding = React.useCallback(() => {
    setRegardingSelection(null);
    setRegardingDialogError(null);
    setIsRegardingDialogOpen(true);
  }, []);

  /** Clear regarding entirely (all 15 fields → null). */
  const handleClearRegarding = React.useCallback(async () => {
    if (!record) return;
    setRegardingDialogError(null);
    setIsApplyingRegarding(true);
    try {
      const webApi = getWebApi();
      if (!webApi) {
        console.error("[TodoDetailPanel] Xrm.WebApi unavailable for clear-regarding");
        setIsApplyingRegarding(false);
        return;
      }
      const navProps = await discoverTodoNavProps();
      const payload = buildTodoRegardingClear(navProps);
      await handleSaveTodo(record.sprk_todoid, payload as ITodoFieldUpdates);
    } finally {
      setIsApplyingRegarding(false);
    }
  }, [record, handleSaveTodo]);

  /** Apply the dialog selection: applyResolverFields + save (FR-13). */
  const handleApplyRegardingSelection = React.useCallback(async () => {
    if (!record || !regardingSelection) return;
    setRegardingDialogError(null);
    setIsApplyingRegarding(true);
    try {
      const webApi = getWebApi();
      if (!webApi) {
        setRegardingDialogError("Dataverse Web API unavailable");
        return;
      }
      const navProps = await discoverTodoNavProps();
      const payload = await buildTodoRegardingUpdate(
        webApi,
        navProps,
        { entityType: regardingSelection.entityType },
        regardingSelection.recordId,
        regardingSelection.recordName,
      );
      const result = await handleSaveTodo(
        record.sprk_todoid,
        payload as ITodoFieldUpdates,
      );
      if (!result.success) {
        setRegardingDialogError(result.error ?? "Failed to save regarding");
        return;
      }
      setIsRegardingDialogOpen(false);
      setRegardingSelection(null);
    } catch (err) {
      console.error("[TodoDetailPanel] applyResolverFields failed:", err);
      setRegardingDialogError(
        err instanceof Error ? err.message : "Failed to apply regarding",
      );
    } finally {
      setIsApplyingRegarding(false);
    }
  }, [record, regardingSelection, handleSaveTodo]);

  const handleRegardingDialogOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (!data.open) {
        // Reset transient state when dialog closes
        setRegardingSelection(null);
        setRegardingDialogError(null);
      }
      setIsRegardingDialogOpen(data.open);
    },
    [],
  );

  // ---------------------------------------------------------------------------
  // Render: empty state (no selection)
  // ---------------------------------------------------------------------------
  if (!selectedEventId) {
    return (
      <div
        className={styles.emptyState}
        role="complementary"
        aria-label="To-do detail panel"
      >
        <Text size={200}>Select a to-do item to view details</Text>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: loading
  // ---------------------------------------------------------------------------
  if (isLoading) {
    return (
      <div className={styles.root} role="complementary" aria-label="To-do detail panel">
        <div className={styles.header}>
          <Text weight="semibold" size={400} className={styles.headerTitle}>
            Loading...
          </Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label="Close detail panel"
            onClick={handleClose}
          />
        </div>
        <div className={styles.loadingState}>
          <Spinner size="medium" label="Loading..." />
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: error
  // ---------------------------------------------------------------------------
  if (error) {
    return (
      <div className={styles.root} role="complementary" aria-label="To-do detail panel">
        <div className={styles.header}>
          <Text weight="semibold" size={400} className={styles.headerTitle}>
            Error
          </Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label="Close detail panel"
            onClick={handleClose}
          />
        </div>
        <div className={styles.errorState}>
          <Text>{error}</Text>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: full detail panel
  // ---------------------------------------------------------------------------
  // Suppress unused-warning for STATUSCODE_DISMISSED / STATECODE_INACTIVE —
  // they're declared at module scope for documentation of the semantic chosen
  // by `dismissTodo` (statecode=1 / statuscode=659490002).
  void STATUSCODE_DISMISSED;
  void STATECODE_INACTIVE;

  const hasExistingRegarding = Boolean(
    record?.sprk_regardingrecordid && record?.sprk_regardingrecordname,
  );

  return (
    <div
      className={styles.root}
      role="complementary"
      aria-label="To-do detail panel"
    >
      {/* Header with todo name and close button */}
      <div className={styles.header}>
        <Text weight="semibold" size={400} className={styles.headerTitle}>
          {record?.sprk_name ?? "To Do Details"}
        </Text>
        <Button
          appearance="subtle"
          icon={<DismissRegular />}
          aria-label="Close detail panel"
          onClick={handleClose}
        />
      </div>

      {/* TodoDetail body */}
      <div className={styles.body}>
        <TodoDetail
          record={record}
          isLoading={false}
          error={null}
          onSaveTodo={handleSaveTodo}
          onDismissTodo={handleDismissTodo}
          onClose={handleClose}
          onSearchContacts={handleSearchContacts}
          onOpenRegardingRecord={handleOpenRegardingRecord}
          onOpenRegardingUrl={handleOpenRegardingUrl}
          onChangeRegarding={handleChangeRegarding}
          onClearRegarding={hasExistingRegarding ? handleClearRegarding : undefined}
        />
      </div>

      {/* Regarding-edit dialog (FR-13) — hosts AssociateToStep */}
      <Dialog
        open={isRegardingDialogOpen}
        onOpenChange={handleRegardingDialogOpenChange}
        modalType="modal"
      >
        <DialogSurface aria-label="Change regarding record">
          <DialogBody>
            <DialogTitle>Change Regarding Record</DialogTitle>
            <DialogContent>
              <AssociateToStep
                entityTypes={[...TODO_REGARDING_TARGETS] as EntityTypeOption[]}
                navigationService={navigationService}
                value={regardingSelection}
                onChange={setRegardingSelection}
                disabled={isApplyingRegarding}
              />
              {regardingDialogError && (
                <Text style={{ color: tokens.colorPaletteRedForeground1 }}>
                  {regardingDialogError}
                </Text>
              )}
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement action="close">
                <Button appearance="secondary" disabled={isApplyingRegarding}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={handleApplyRegardingSelection}
                disabled={!regardingSelection || isApplyingRegarding}
              >
                {isApplyingRegarding ? "Applying…" : "Apply"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
