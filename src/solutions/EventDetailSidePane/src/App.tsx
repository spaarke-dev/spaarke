/**
 * EventDetailSidePane - Root Application Component
 *
 * Renders the Event Detail side pane with theme support.
 * Integrates all sections with dirty field tracking, save functionality,
 * optimistic UI updates with error rollback, security role awareness,
 * and EventTypeService for field/section visibility based on Event Type.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/032-create-header-section.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/039-integrate-eventtypeservice.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/040-implement-save-webapi.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/041-add-optimistic-ui.poml
 * @see projects/events-workspace-apps-UX-r1/tasks/042-add-securityrole-awareness.poml
 */

import * as React from "react";
import {
  FluentProvider,
  tokens,
  makeStyles,
  shorthands,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { parseSidePaneParams } from "./utils/parseParams";
import {
  HeaderSection,
  StatusSection,
  KeyFieldsSection,
  DatesSection,
  DescriptionSection,
  RelatedEventSection,
  HistorySection,
  Footer,
  createSuccessMessage,
  createErrorMessage,
  createErrorMessageWithRollback,
  UnsavedChangesDialog,
  extractRelatedEventInfo,
  type FooterMessageWithActions,
  type UnsavedChangesAction,
  type PriorityValue,
  type StatusReasonValue,
  type OwnerInfo,
  type IRelatedEventInfo,
  type HistoryData,
} from "./components";
import { closeSidePane } from "./services/sidePaneService";
import { IEventRecord } from "./types/EventRecord";
import {
  saveEvent,
  getDirtyFields,
  hasDirtyFields,
  parseWebApiError,
  type DirtyFields,
} from "./services/eventService";
import {
  useOptimisticUpdate,
  useRecordAccess,
  useEventTypeConfig,
  isReadOnly as computeIsReadOnly,
  type GridUpdateCallback,
} from "./hooks";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  content: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("0"), // Sections handle their own borders
    flexGrow: 1,
    overflowY: "auto",
  },
  readOnlyBanner: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("8px", "16px"),
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// App Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for App component
 * Allows parent to register for grid update notifications
 */
export interface AppProps {
  /** Optional callback when event is saved (for grid updates) */
  onRowUpdated?: GridUpdateCallback;
}

export const App: React.FC<AppProps> = ({ onRowUpdated }) => {
  const styles = useStyles();
  const [theme, setTheme] = React.useState(resolveTheme);
  const params = React.useMemo(() => parseSidePaneParams(), []);

  // Track current edited values (accumulated from all sections)
  const [currentValues, setCurrentValues] = React.useState<Partial<IEventRecord>>({});

  // Save state
  const [isSaving, setIsSaving] = React.useState(false);
  const [footerMessage, setFooterMessage] = React.useState<FooterMessageWithActions | null>(null);

  // Unsaved changes dialog state
  const [showUnsavedDialog, setShowUnsavedDialog] = React.useState(false);

  // Track Event Type ID for field visibility configuration
  const [eventTypeId, setEventTypeId] = React.useState<string | undefined>(undefined);

  // Track related event info
  const [relatedEvent, setRelatedEvent] = React.useState<IRelatedEventInfo | null>(null);

  // Track owner info
  const [ownerInfo, setOwnerInfo] = React.useState<OwnerInfo | null>(null);

  // History section state (lazy loaded)
  const [historyData, setHistoryData] = React.useState<HistoryData | null>(null);
  const [isLoadingHistory, setIsLoadingHistory] = React.useState(false);

  // Section expanded states (controlled for Event Type config defaults)
  const [datesExpanded, setDatesExpanded] = React.useState<boolean | undefined>(undefined);
  const [relatedEventExpanded, setRelatedEventExpanded] = React.useState<boolean | undefined>(undefined);
  const [descriptionExpanded, setDescriptionExpanded] = React.useState<boolean | undefined>(undefined);
  const [historyExpanded, setHistoryExpanded] = React.useState<boolean | undefined>(undefined);

  // Optimistic update hook for original values, rollback, and grid notification
  const optimistic = useOptimisticUpdate(params.eventId ?? undefined);

  // Security role awareness: check if user has write access to this record
  const recordAccess = useRecordAccess(params.eventId);
  const isReadOnly = computeIsReadOnly(recordAccess);

  // Event Type configuration hook for field visibility
  const eventTypeConfig = useEventTypeConfig(eventTypeId);

  // Log access state for debugging
  React.useEffect(() => {
    if (recordAccess.isLoaded) {
      console.log(
        `[App] Record access check complete: canWrite=${recordAccess.canWrite}, isReadOnly=${isReadOnly}`
      );
    }
  }, [recordAccess.isLoaded, recordAccess.canWrite, isReadOnly]);

  // Register grid callback when provided
  React.useEffect(() => {
    optimistic.registerGridCallback(onRowUpdated ?? null);
  }, [onRowUpdated, optimistic]);

  // Calculate dirty fields by comparing original to current
  const dirtyFields: DirtyFields = React.useMemo(() => {
    if (!optimistic.originalEvent) return {};
    return getDirtyFields(optimistic.originalEvent, currentValues);
  }, [optimistic.originalEvent, currentValues]);

  const isDirty = hasDirtyFields(dirtyFields);

  // Listen for theme changes
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  // Apply section expand/collapse defaults from Event Type config
  React.useEffect(() => {
    if (eventTypeConfig.fieldStates && !eventTypeConfig.isLoading) {
      // Only set defaults if not already set by user
      if (datesExpanded === undefined) {
        setDatesExpanded(eventTypeConfig.getSectionCollapseState("dates") === "expanded");
      }
      if (relatedEventExpanded === undefined) {
        setRelatedEventExpanded(eventTypeConfig.getSectionCollapseState("relatedEvent") === "expanded");
      }
      if (descriptionExpanded === undefined) {
        setDescriptionExpanded(eventTypeConfig.getSectionCollapseState("description") === "expanded");
      }
      if (historyExpanded === undefined) {
        setHistoryExpanded(eventTypeConfig.getSectionCollapseState("history") === "expanded");
      }
    }
  }, [eventTypeConfig.fieldStates, eventTypeConfig.isLoading, eventTypeConfig.getSectionCollapseState,
      datesExpanded, relatedEventExpanded, descriptionExpanded, historyExpanded]);

  /**
   * Handle event loaded from HeaderSection
   * Stores original event data for dirty field comparison via optimistic hook
   * Also extracts Event Type ID for field visibility configuration
   */
  const handleEventLoaded = React.useCallback((loadedEvent: IEventRecord) => {
    // Store original via optimistic hook (for rollback support)
    optimistic.setOriginalEvent(loadedEvent);

    // Extract Event Type ID for field visibility
    const typeId = loadedEvent._sprk_eventtype_ref_value;
    setEventTypeId(typeId);

    // Extract related event info
    const related = extractRelatedEventInfo(loadedEvent as unknown as Record<string, unknown>);
    setRelatedEvent(related);

    // Extract owner info
    if (loadedEvent._ownerid_value) {
      setOwnerInfo({
        id: loadedEvent._ownerid_value,
        name: loadedEvent["_ownerid_value@OData.Community.Display.V1.FormattedValue"] ?? "Unknown",
      });
    }

    // Initialize current values with loaded values
    setCurrentValues({
      sprk_eventname: loadedEvent.sprk_eventname,
      sprk_description: loadedEvent.sprk_description,
      sprk_duedate: loadedEvent.sprk_duedate,
      sprk_basedate: loadedEvent.sprk_basedate,
      sprk_finalduedate: loadedEvent.sprk_finalduedate,
      sprk_completeddate: loadedEvent.sprk_completeddate,
      scheduledstart: loadedEvent.scheduledstart,
      scheduledend: loadedEvent.scheduledend,
      sprk_location: loadedEvent.sprk_location,
      sprk_remindat: loadedEvent.sprk_remindat,
      statuscode: loadedEvent.statuscode,
      sprk_priority: loadedEvent.sprk_priority,
      sprk_source: loadedEvent.sprk_source,
    });

    // Set initial history data placeholder
    setHistoryData({
      createdBy: null,
      createdOn: null,
      modifiedBy: null,
      modifiedOn: null,
      statusChanges: [],
    });
  }, [optimistic]);

  /**
   * Handle event name updated (inline edit in header)
   * Note: Header section handles its own immediate save for name field
   * This callback is for notifying other components of the change
   */
  const handleNameUpdated = React.useCallback((newName: string) => {
    console.log("[App] Event name updated:", newName);
    // Update current values (already saved by HeaderSection)
    setCurrentValues((prev) => ({ ...prev, sprk_eventname: newName }));
    // Also update original since the name is saved immediately
    // Use optimistic hook's handler for this (it also notifies the grid)
    if (optimistic.originalEvent) {
      optimistic.handleSaveSuccess(
        { sprk_eventname: newName },
        { ...currentValues, sprk_eventname: newName }
      );
    }
  }, [optimistic, currentValues]);

  /**
   * Update a field value (called by child sections)
   * This marks the field as dirty until saved
   */
  const updateFieldValue = React.useCallback(
    <K extends keyof IEventRecord>(field: K, value: IEventRecord[K] | undefined) => {
      setCurrentValues((prev) => ({ ...prev, [field]: value }));
    },
    []
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Field Change Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle status reason change from StatusSection
   */
  const handleStatusChange = React.useCallback((statuscode: StatusReasonValue) => {
    updateFieldValue("statuscode", statuscode);
  }, [updateFieldValue]);

  /**
   * Handle due date change from KeyFieldsSection
   */
  const handleDueDateChange = React.useCallback((date: Date | null) => {
    updateFieldValue("sprk_duedate", date?.toISOString() ?? undefined);
  }, [updateFieldValue]);

  /**
   * Handle priority change from KeyFieldsSection
   */
  const handlePriorityChange = React.useCallback((priority: PriorityValue) => {
    updateFieldValue("sprk_priority", priority);
  }, [updateFieldValue]);

  /**
   * Handle base date change from DatesSection
   */
  const handleBaseDateChange = React.useCallback((date: Date | null) => {
    updateFieldValue("sprk_basedate", date?.toISOString() ?? undefined);
  }, [updateFieldValue]);

  /**
   * Handle final due date change from DatesSection
   */
  const handleFinalDueDateChange = React.useCallback((date: Date | null) => {
    updateFieldValue("sprk_finalduedate", date?.toISOString() ?? undefined);
  }, [updateFieldValue]);

  /**
   * Handle remind at change from DatesSection
   */
  const handleRemindAtChange = React.useCallback((datetime: Date | null) => {
    updateFieldValue("sprk_remindat", datetime?.toISOString() ?? undefined);
  }, [updateFieldValue]);

  /**
   * Handle description change from DescriptionSection
   */
  const handleDescriptionChange = React.useCallback((value: string) => {
    updateFieldValue("sprk_description", value || undefined);
  }, [updateFieldValue]);

  /**
   * Handle related event lookup (placeholder - would open lookup dialog)
   */
  const handleRelatedEventLookup = React.useCallback(() => {
    console.log("[App] Related event lookup requested - would open lookup dialog");
    // TODO: Implement lookup dialog via Xrm.Utility.lookupObjects
  }, []);

  /**
   * Handle related event clear
   */
  const handleRelatedEventClear = React.useCallback(() => {
    setRelatedEvent(null);
    // TODO: Update field value for saving
  }, []);

  /**
   * Handle related event navigation
   */
  const handleRelatedEventNavigate = React.useCallback((eventId: string) => {
    console.log("[App] Navigate to related event:", eventId);
    // TODO: Navigate to event record
  }, []);

  /**
   * Handle history section lazy load
   */
  const handleLoadHistory = React.useCallback(async () => {
    if (historyData?.createdBy) return; // Already loaded

    setIsLoadingHistory(true);
    // TODO: Query audit history from Dataverse
    // For now, just simulate a load
    await new Promise((resolve) => setTimeout(resolve, 500));
    setIsLoadingHistory(false);
  }, [historyData]);

  // ─────────────────────────────────────────────────────────────────────────
  // Save Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle Save button click
   * Sends only dirty fields via PATCH request
   * Uses optimistic update hook for success/error handling
   */
  const handleSave = React.useCallback(async (fieldsToSave?: DirtyFields) => {
    const fields = fieldsToSave || dirtyFields;
    if (!params.eventId || !hasDirtyFields(fields)) return;

    setIsSaving(true);
    setFooterMessage(null);

    try {
      const result = await saveEvent(params.eventId, fields);

      if (result.success) {
        // Use optimistic hook for success handling (updates original, notifies grid)
        optimistic.handleSaveSuccess(fields, currentValues);

        // Show success message
        setFooterMessage(createSuccessMessage(result.savedFields || []));

        console.log("[App] Save successful:", result.savedFields);
      } else {
        // Show error message with rollback options via optimistic hook
        const errorMsg = parseWebApiError(new Error(result.error));
        optimistic.handleSaveError(errorMsg, fields, currentValues);
        setFooterMessage(createErrorMessageWithRollback(errorMsg));

        console.error("[App] Save failed:", result.error);
      }
    } catch (error) {
      const errorMsg = parseWebApiError(error);
      optimistic.handleSaveError(errorMsg, fields, currentValues);
      setFooterMessage(createErrorMessageWithRollback(errorMsg));
      console.error("[App] Save exception:", error);
    } finally {
      setIsSaving(false);
    }
  }, [params.eventId, dirtyFields, currentValues, optimistic]);

  /**
   * Dismiss footer message
   */
  const handleDismissMessage = React.useCallback(() => {
    setFooterMessage(null);
    optimistic.dismissError();
  }, [optimistic]);

  /**
   * Handle retry after save error
   * Re-attempts save with the failed fields
   */
  const handleRetry = React.useCallback(() => {
    const fieldsToRetry = optimistic.retryWithCurrentValues();
    if (hasDirtyFields(fieldsToRetry)) {
      handleSave(fieldsToRetry);
    }
  }, [optimistic, handleSave]);

  /**
   * Handle discard changes after save error
   * Rolls back to original values
   */
  const handleDiscard = React.useCallback(() => {
    const valuesToRestore = optimistic.rollbackToOriginal();

    // Apply rollback values to current values
    setCurrentValues((prev) => {
      const restored = { ...prev };
      for (const [field, value] of Object.entries(valuesToRestore)) {
        (restored as Record<string, unknown>)[field] = value;
      }
      return restored;
    });

    setFooterMessage(null);
    console.log("[App] Changes discarded, rolled back to original values");
  }, [optimistic]);

  /**
   * Handle close request from HeaderSection
   * Shows unsaved changes dialog if there are dirty fields
   */
  const handleCloseRequest = React.useCallback(() => {
    if (isDirty) {
      setShowUnsavedDialog(true);
    } else {
      closeSidePane();
    }
  }, [isDirty]);

  /**
   * Handle unsaved changes dialog action
   * Save: Save changes then close
   * Discard: Close without saving
   * Cancel: Return to editing
   */
  const handleUnsavedChangesAction = React.useCallback(
    async (action: UnsavedChangesAction) => {
      if (action === "cancel") {
        setShowUnsavedDialog(false);
        return;
      }

      if (action === "discard") {
        setShowUnsavedDialog(false);
        closeSidePane();
        return;
      }

      // action === "save"
      if (!params.eventId || !isDirty) {
        setShowUnsavedDialog(false);
        closeSidePane();
        return;
      }

      setIsSaving(true);
      try {
        const result = await saveEvent(params.eventId, dirtyFields);

        if (result.success) {
          console.log("[App] Save before close successful:", result.savedFields);
          setShowUnsavedDialog(false);
          closeSidePane();
        } else {
          // Show error, keep dialog open
          const errorMsg = parseWebApiError(new Error(result.error));
          setFooterMessage(createErrorMessage(errorMsg));
          setShowUnsavedDialog(false);
          console.error("[App] Save before close failed:", result.error);
        }
      } catch (error) {
        const errorMsg = parseWebApiError(error);
        setFooterMessage(createErrorMessage(errorMsg));
        setShowUnsavedDialog(false);
        console.error("[App] Save before close exception:", error);
      } finally {
        setIsSaving(false);
      }
    },
    [params.eventId, isDirty, dirtyFields]
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Compute disabled state (read-only OR saving)
  // ─────────────────────────────────────────────────────────────────────────
  const isDisabled = isReadOnly || isSaving;

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Header Section: Name, Type Badge, Parent Link, Close Button */}
        <HeaderSection
          eventId={params.eventId}
          onEventLoaded={handleEventLoaded}
          onNameUpdated={handleNameUpdated}
          onCloseRequest={handleCloseRequest}
          isReadOnly={isReadOnly}
        />

        {/* Read-Only Banner (when user lacks edit permission) */}
        {isReadOnly && (
          <div className={styles.readOnlyBanner}>
            Read-only: You do not have permission to edit this event
          </div>
        )}

        {/* Main Content Area with Sections */}
        <main className={styles.content}>
          {/* Status Section: Status Reason with segmented buttons */}
          <StatusSection
            value={(currentValues.statuscode as StatusReasonValue) ?? 1}
            onChange={handleStatusChange}
            disabled={isDisabled}
          />

          {/* Key Fields Section: Due Date, Priority, Owner (always visible) */}
          <KeyFieldsSection
            dueDate={currentValues.sprk_duedate ?? null}
            onDueDateChange={handleDueDateChange}
            priority={(currentValues.sprk_priority as PriorityValue) ?? null}
            onPriorityChange={handlePriorityChange}
            owner={ownerInfo}
            disabled={isDisabled}
          />

          {/* Dates Section: Base Date, Final Due Date, Remind At */}
          {/* Visibility controlled by Event Type configuration */}
          {eventTypeConfig.isSectionVisible("dates") && (
            <DatesSection
              baseDate={currentValues.sprk_basedate ?? null}
              onBaseDateChange={handleBaseDateChange}
              finalDueDate={currentValues.sprk_finalduedate ?? null}
              onFinalDueDateChange={handleFinalDueDateChange}
              remindAt={currentValues.sprk_remindat ?? null}
              onRemindAtChange={handleRemindAtChange}
              expanded={datesExpanded}
              onExpandedChange={setDatesExpanded}
              defaultExpanded={eventTypeConfig.getSectionCollapseState("dates") === "expanded"}
              disabled={isDisabled}
            />
          )}

          {/* Related Event Section: Link to related event */}
          {/* Visibility controlled by Event Type configuration */}
          {eventTypeConfig.isSectionVisible("relatedEvent") && (
            <RelatedEventSection
              relatedEvent={relatedEvent}
              onLookup={handleRelatedEventLookup}
              onClear={handleRelatedEventClear}
              onNavigate={handleRelatedEventNavigate}
              defaultCollapseState={eventTypeConfig.getSectionCollapseState("relatedEvent")}
              disabled={isDisabled}
              visible={true}
            />
          )}

          {/* Description Section: Multiline text editor */}
          {/* Visibility controlled by Event Type configuration */}
          {eventTypeConfig.isSectionVisible("description") && (
            <DescriptionSection
              value={currentValues.sprk_description ?? null}
              onChange={handleDescriptionChange}
              expanded={descriptionExpanded}
              onExpandedChange={setDescriptionExpanded}
              defaultExpanded={eventTypeConfig.getSectionCollapseState("description") === "expanded"}
              disabled={isDisabled}
              maxLength={4000}
              showCharCount
            />
          )}

          {/* History Section: Audit trail (lazy loaded) */}
          {/* Visibility controlled by Event Type configuration */}
          {eventTypeConfig.isSectionVisible("history") && (
            <HistorySection
              historyData={historyData}
              onLoadHistory={handleLoadHistory}
              isLoading={isLoadingHistory}
              expanded={historyExpanded}
              onExpandedChange={setHistoryExpanded}
              defaultExpanded={eventTypeConfig.getSectionCollapseState("history") === "expanded"}
            />
          )}
        </main>

        {/* Footer with Save Button and Messages (with rollback actions) */}
        <Footer
          isDirty={isDirty}
          isSaving={isSaving}
          onSave={() => handleSave()}
          message={footerMessage}
          onDismissMessage={handleDismissMessage}
          onRetry={handleRetry}
          onDiscard={handleDiscard}
          version="1.0.6"
          isReadOnly={isReadOnly}
          eventId={params.eventId}
        />

        {/* Unsaved Changes Dialog */}
        <UnsavedChangesDialog
          open={showUnsavedDialog}
          onAction={handleUnsavedChangesAction}
          isSaving={isSaving}
        />
      </div>
    </FluentProvider>
  );
};
