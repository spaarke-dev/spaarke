/**
 * EventDetailSidePane - Root Application Component
 *
 * Renders the Event Detail side pane with theme support.
 * Uses Approach A dynamic form renderer — the JSON IS the form definition.
 *
 * Fixed chrome: Header, StatusReasonBar, Memo, ToDo, Footer.
 * Config-driven: Everything between StatusReasonBar and Memo/ToDo
 * is rendered dynamically from the sprk_fieldconfigjson on the Event Type.
 *
 * @see approach-a-dynamic-form-renderer.md
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
  Footer,
  createSuccessMessage,
  createErrorMessage,
  createErrorMessageWithRollback,
  UnsavedChangesDialog,
  type FooterMessageWithActions,
  type UnsavedChangesAction,
  type StatusReasonValue,
} from "./components";
import { FormRenderer } from "./components/form";
import { MemoSection } from "./components/MemoSection";
import { TodoSection } from "./components/TodoSection";
import { closeSidePane } from "./services/sidePaneService";
import { IEventRecord } from "./types/EventRecord";
import type { ILookupValue } from "./types/FormConfig";
import {
  saveEvent,
  hasDirtyFields,
  parseWebApiError,
  type DirtyFields,
} from "./services/eventService";
import {
  useOptimisticUpdate,
  useRecordAccess,
  isReadOnly as computeIsReadOnly,
  type GridUpdateCallback,
} from "./hooks";
import { useFormConfig } from "./hooks/useFormConfig";
import {
  sendEventSaved,
  sendEventOpened,
  sendEventClosed,
  sendDirtyStateChanged,
  closeChannel,
} from "./utils/broadcastChannel";
import {
  persistState,
  restoreState,
  clearPersistedState,
} from "./utils/sessionPersistence";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Get plural entity set name for @odata.bind format.
 * Used when saving lookup field values to Dataverse.
 */
function getEntityPluralName(entityType: string): string {
  const plurals: Record<string, string> = {
    contact: "contacts",
    systemuser: "systemusers",
    team: "teams",
    account: "accounts",
    sprk_event: "sprk_events",
    sprk_email: "sprk_emails",
    sprk_eventtype: "sprk_eventtypes",
  };
  return plurals[entityType] ?? `${entityType}s`;
}

/**
 * Static fallback map: column logical name → navigation property (SchemaName).
 * Used when the Dataverse JSON config doesn't include navigationProperty.
 * Navigation property names are CASE-SENSITIVE for @odata.bind.
 * @see .claude/patterns/dataverse/relationship-navigation.md
 */
const KNOWN_NAV_PROPERTIES: Record<string, string> = {
  sprk_completedby: "sprk_CompletedBy",
  sprk_assignedto: "sprk_AssignedTo",
  sprk_assignedattorney: "sprk_AssignedAttorney",
  sprk_assignedparalegal: "sprk_AssignedParalegal",
  sprk_approvedby: "sprk_ApprovedBy",
  sprk_regardingemail: "sprk_RegardingEmail",
};

/**
 * Map statuscode → required statecode for valid Dataverse state transitions.
 * Dataverse requires statecode + statuscode to be set together.
 *
 * Active (statecode 0): Draft, Open, On Hold
 * Inactive (statecode 1): Completed, Closed, Cancelled
 */
const STATUSCODE_STATECODE_MAP: Record<number, number> = {
  1:         0, // Draft → Active
  659490001: 0, // Open → Active
  659490006: 0, // On Hold → Active
  659490002: 1, // Completed → Inactive
  659490003: 1, // Closed → Inactive
  659490004: 1, // Cancelled → Inactive
};

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

  // Ref for content area scroll position persistence
  const contentRef = React.useRef<HTMLElement>(null);

  // Current record values (full record + user edits overlaid)
  const [currentValues, setCurrentValues] = React.useState<Record<string, unknown>>({});

  // Track which fields were explicitly edited by user (for dirty detection)
  const [editedFields, setEditedFields] = React.useState<Set<string>>(new Set());

  // Save state
  const [isSaving, setIsSaving] = React.useState(false);
  const [footerMessage, setFooterMessage] = React.useState<FooterMessageWithActions | null>(null);

  // Unsaved changes dialog state
  const [showUnsavedDialog, setShowUnsavedDialog] = React.useState(false);

  // Track Event Type ID for form configuration
  const [eventTypeId, setEventTypeId] = React.useState<string | undefined>(undefined);

  // Section expanded states (generic — keyed by section ID from config)
  const [sectionStates, setSectionStates] = React.useState<Record<string, boolean>>({});

  // Optimistic update hook for original values, rollback, and grid notification
  const optimistic = useOptimisticUpdate(params.eventId ?? undefined);

  // Security role awareness: check if user has write access to this record
  const recordAccess = useRecordAccess(params.eventId);
  const isReadOnly = computeIsReadOnly(recordAccess);

  // Form configuration (Approach A — JSON IS the form definition)
  const formConfig = useFormConfig(eventTypeId);

  // Log access state for debugging
  React.useEffect(() => {
    if (recordAccess.isLoaded) {
      console.log(
        `[App] Record access check: canWrite=${recordAccess.canWrite}, isReadOnly=${isReadOnly}`
      );
    }
  }, [recordAccess.isLoaded, recordAccess.canWrite, isReadOnly]);

  // Register grid callback when provided
  React.useEffect(() => {
    optimistic.registerGridCallback(onRowUpdated ?? null);
  }, [onRowUpdated, optimistic]);

  // ─────────────────────────────────────────────────────────────────────────
  // Dirty Field Tracking
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Compute dirty fields by comparing editedFields against original record.
   * Handles lookup fields specially (compares ILookupValue.id vs _fieldname_value).
   */
  const dirtyFields: DirtyFields = React.useMemo(() => {
    if (!optimistic.originalEvent || editedFields.size === 0) return {};
    const original = optimistic.originalEvent as unknown as Record<string, unknown>;
    const dirty: DirtyFields = {};

    for (const field of editedFields) {
      const currVal = currentValues[field];

      // Detect lookup field edits
      const origLookupKey = `_${field}_value`;
      const isLookupEdit = origLookupKey in original ||
        (currVal && typeof currVal === "object" && "id" in (currVal as Record<string, unknown>));

      if (isLookupEdit) {
        // Compare lookup IDs
        const currId = currVal && typeof currVal === "object" && "id" in (currVal as Record<string, unknown>)
          ? (currVal as ILookupValue).id
          : null;
        const origRaw = original[origLookupKey] as string | null | undefined;
        const origId = origRaw ? origRaw.replace(/[{}]/g, "").toLowerCase() : null;
        if (currId !== origId) {
          dirty[field] = currVal;
        }
      } else {
        // Regular field comparison
        const origVal = original[field];
        const origNorm = origVal === null ? undefined : origVal;
        const currNorm = currVal === null ? undefined : currVal;
        if (origNorm !== currNorm) {
          dirty[field] = currVal;
        }
      }
    }

    return dirty;
  }, [optimistic.originalEvent, currentValues, editedFields]);

  const isDirty = hasDirtyFields(dirtyFields);

  // Listen for theme changes
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  // ─────────────────────────────────────────────────────────────────────────
  // Session Persistence & BroadcastChannel
  // ─────────────────────────────────────────────────────────────────────────

  // Restore persisted state on mount (survives tab switching)
  React.useEffect(() => {
    if (params.eventId) {
      const persisted = restoreState(params.eventId);
      if (persisted) {
        setCurrentValues(persisted.currentValues);
        setSectionStates(persisted.sectionStates);
        if (persisted.editedFieldNames) {
          setEditedFields(new Set(persisted.editedFieldNames));
        }
        // Restore scroll position after render
        requestAnimationFrame(() => {
          if (contentRef.current && persisted.scrollPosition > 0) {
            contentRef.current.scrollTop = persisted.scrollPosition;
          }
        });
        console.log("[App] Restored persisted state for event:", params.eventId);
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Run once on mount only

  // Notify parent when event is loaded (BroadcastChannel)
  React.useEffect(() => {
    if (params.eventId && eventTypeId) {
      sendEventOpened(params.eventId, eventTypeId);
    }
  }, [params.eventId, eventTypeId]);

  // Notify parent of dirty state changes (BroadcastChannel)
  React.useEffect(() => {
    if (params.eventId) {
      sendDirtyStateChanged(params.eventId, isDirty);
    }
  }, [params.eventId, isDirty]);

  // Debounced persist to sessionStorage on field/section changes
  React.useEffect(() => {
    if (!params.eventId) return;
    const timer = setTimeout(() => {
      persistState({
        eventId: params.eventId!,
        currentValues,
        sectionStates,
        editedFieldNames: Array.from(editedFields),
        scrollPosition: contentRef.current?.scrollTop ?? 0,
        timestamp: Date.now(),
      });
    }, 300);
    return () => clearTimeout(timer);
  }, [params.eventId, currentValues, sectionStates, editedFields]);

  // Cleanup BroadcastChannel on unmount
  React.useEffect(() => {
    return () => {
      if (params.eventId) {
        sendEventClosed(params.eventId);
      }
      closeChannel();
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Run cleanup on unmount only

  // ─────────────────────────────────────────────────────────────────────────
  // Event Load Handler
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle event loaded from HeaderSection.
   * Stores original event data and initializes current values with the
   * full record (so FormRenderer can access any field dynamically).
   */
  const handleEventLoaded = React.useCallback((loadedEvent: IEventRecord) => {
    // Store original via optimistic hook (for rollback support)
    optimistic.setOriginalEvent(loadedEvent);

    // Extract Event Type ID for form configuration
    setEventTypeId(loadedEvent._sprk_eventtype_ref_value);

    // Store ALL record fields as current values (dynamic form needs all fields)
    setCurrentValues(loadedEvent as unknown as Record<string, unknown>);

    // Clear edited fields tracking (fresh load)
    setEditedFields(new Set());
  }, [optimistic]);

  /**
   * Handle event name updated (inline edit in header).
   * Header handles its own immediate save — this syncs local state.
   */
  const handleNameUpdated = React.useCallback((newName: string) => {
    console.log("[App] Event name updated:", newName);
    setCurrentValues((prev) => ({ ...prev, sprk_eventname: newName }));
    // Update original since name is saved immediately by HeaderSection
    if (optimistic.originalEvent) {
      optimistic.handleSaveSuccess(
        { sprk_eventname: newName },
        { sprk_eventname: newName } as Partial<IEventRecord>
      );
    }
  }, [optimistic]);

  // ─────────────────────────────────────────────────────────────────────────
  // Field Change Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle status reason change (fixed chrome — outside FormRenderer)
   */
  const handleStatusChange = React.useCallback((statuscode: StatusReasonValue) => {
    setCurrentValues((prev) => ({ ...prev, statuscode }));
    setEditedFields((prev) => new Set(prev).add("statuscode"));
  }, []);

  /**
   * Handle field change from FormRenderer (dynamic form).
   * Called for all config-driven fields: text, date, choice, lookup, url, etc.
   */
  const handleFieldChange = React.useCallback((fieldName: string, value: unknown) => {
    setCurrentValues((prev) => ({ ...prev, [fieldName]: value }));
    setEditedFields((prev) => new Set(prev).add(fieldName));
  }, []);

  /**
   * Handle section expanded state change from FormRenderer
   */
  const handleSectionExpandedChange = React.useCallback((sectionId: string, expanded: boolean) => {
    setSectionStates((prev) => ({ ...prev, [sectionId]: expanded }));
  }, []);

  // ─────────────────────────────────────────────────────────────────────────
  // Save Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Build a map from field logical name → navigation property name.
   * Navigation property names are CASE-SENSITIVE (uses SchemaName casing).
   * @see .claude/patterns/dataverse/relationship-navigation.md
   */
  const lookupNavMap = React.useMemo((): Map<string, string> => {
    const map = new Map<string, string>();
    if (!formConfig.formConfig) return map;

    for (const section of formConfig.formConfig.sections) {
      for (const field of section.fields) {
        if (field.type === "lookup" && field.navigationProperty) {
          map.set(field.name, field.navigationProperty);
        }
      }
    }
    return map;
  }, [formConfig.formConfig]);

  /**
   * Build the save payload, converting lookup ILookupValue objects
   * to @odata.bind format for Dataverse WebAPI.
   *
   * Also pairs statecode with statuscode for valid state transitions.
   * Uses navigationProperty from form config for correct SchemaName casing.
   * @see .claude/patterns/dataverse/relationship-navigation.md
   */
  const buildSavePayload = React.useCallback((fields: DirtyFields): DirtyFields => {
    const original = optimistic.originalEvent as unknown as Record<string, unknown>;
    const payload: DirtyFields = {};

    for (const [field, value] of Object.entries(fields)) {
      if (value && typeof value === "object" && "id" in (value as Record<string, unknown>)) {
        // Lookup field with selected value → @odata.bind format
        const lv = value as ILookupValue;
        const entityPlural = getEntityPluralName(lv.entityType);
        // Use navigation property name (SchemaName casing) for @odata.bind
        // Priority: JSON config → static fallback → logical name (last resort)
        const navProp = lookupNavMap.get(field) ?? KNOWN_NAV_PROPERTIES[field] ?? field;
        payload[`${navProp}@odata.bind`] = `/${entityPlural}(${lv.id})`;
      } else if (value === null) {
        // Check if this is a lookup field being cleared
        const origLookupKey = `_${field}_value`;
        if (original && origLookupKey in original) {
          const navProp = lookupNavMap.get(field) ?? KNOWN_NAV_PROPERTIES[field] ?? field;
          payload[`${navProp}@odata.bind`] = null;
        } else {
          payload[field] = value;
        }
      } else {
        payload[field] = value;
      }
    }

    // Auto-pair statecode when statuscode is changing.
    // Dataverse requires both fields for valid state transitions.
    if ("statuscode" in payload && typeof payload["statuscode"] === "number") {
      const requiredState = STATUSCODE_STATECODE_MAP[payload["statuscode"] as number];
      if (requiredState !== undefined) {
        payload["statecode"] = requiredState;
        console.log(
          `[App] Auto-paired statecode=${requiredState} for statuscode=${payload["statuscode"]}`
        );
      }
    }

    return payload;
  }, [optimistic.originalEvent, lookupNavMap]);

  /**
   * Handle Save button click.
   * Sends only dirty fields via PATCH request.
   * Uses optimistic update hook for success/error handling.
   */
  const handleSave = React.useCallback(async (fieldsToSave?: DirtyFields) => {
    const fields = fieldsToSave || dirtyFields;
    if (!params.eventId || !hasDirtyFields(fields)) return;

    setIsSaving(true);
    setFooterMessage(null);

    try {
      // Convert lookup fields to @odata.bind format for save
      const savePayload = buildSavePayload(fields);
      const result = await saveEvent(params.eventId, savePayload);

      if (result.success) {
        // Use optimistic hook for success handling (updates original, notifies grid)
        optimistic.handleSaveSuccess(
          fields,
          currentValues as unknown as Partial<IEventRecord>
        );

        // Notify parent via BroadcastChannel for grid refresh
        sendEventSaved(params.eventId, fields);

        // Clear persisted state — no more dirty data to preserve
        clearPersistedState();

        // Clear edited fields tracking for saved fields
        setEditedFields((prev) => {
          const next = new Set(prev);
          for (const key of Object.keys(fields)) {
            next.delete(key);
          }
          return next;
        });

        // Show success message
        setFooterMessage(createSuccessMessage(result.savedFields || []));
        console.log("[App] Save successful:", result.savedFields);
      } else {
        // Show error message with rollback options
        const errorMsg = parseWebApiError(new Error(result.error));
        optimistic.handleSaveError(
          errorMsg,
          fields,
          currentValues as unknown as Partial<IEventRecord>
        );
        setFooterMessage(createErrorMessageWithRollback(errorMsg));
        console.error("[App] Save failed:", result.error);
      }
    } catch (error) {
      const errorMsg = parseWebApiError(error);
      optimistic.handleSaveError(
        errorMsg,
        fields,
        currentValues as unknown as Partial<IEventRecord>
      );
      setFooterMessage(createErrorMessageWithRollback(errorMsg));
      console.error("[App] Save exception:", error);
    } finally {
      setIsSaving(false);
    }
  }, [params.eventId, dirtyFields, currentValues, optimistic, buildSavePayload]);

  /**
   * Dismiss footer message
   */
  const handleDismissMessage = React.useCallback(() => {
    setFooterMessage(null);
    optimistic.dismissError();
  }, [optimistic]);

  /**
   * Handle retry after save error
   */
  const handleRetry = React.useCallback(() => {
    const fieldsToRetry = optimistic.retryWithCurrentValues();
    if (hasDirtyFields(fieldsToRetry)) {
      handleSave(fieldsToRetry);
    }
  }, [optimistic, handleSave]);

  /**
   * Handle discard changes after save error
   */
  const handleDiscard = React.useCallback(() => {
    const valuesToRestore = optimistic.rollbackToOriginal();

    // Apply rollback values to current values
    setCurrentValues((prev) => {
      const restored = { ...prev };
      for (const [field, value] of Object.entries(valuesToRestore)) {
        restored[field] = value;
      }
      return restored;
    });

    // Clear edited fields that were rolled back
    setEditedFields((prev) => {
      const next = new Set(prev);
      for (const key of Object.keys(valuesToRestore)) {
        next.delete(key);
      }
      return next;
    });

    setFooterMessage(null);
    console.log("[App] Changes discarded, rolled back to original values");
  }, [optimistic]);

  /**
   * Handle close request from HeaderSection
   */
  const handleCloseRequest = React.useCallback(() => {
    if (isDirty) {
      setShowUnsavedDialog(true);
    } else {
      clearPersistedState();
      closeSidePane();
    }
  }, [isDirty]);

  /**
   * Handle unsaved changes dialog action
   */
  const handleUnsavedChangesAction = React.useCallback(
    async (action: UnsavedChangesAction) => {
      if (action === "cancel") {
        setShowUnsavedDialog(false);
        return;
      }

      if (action === "discard") {
        clearPersistedState();
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
        const savePayload = buildSavePayload(dirtyFields);
        const result = await saveEvent(params.eventId, savePayload);

        if (result.success) {
          console.log("[App] Save before close successful:", result.savedFields);
          sendEventSaved(params.eventId, dirtyFields);
          clearPersistedState();
          setShowUnsavedDialog(false);
          closeSidePane();
        } else {
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
    [params.eventId, isDirty, dirtyFields, buildSavePayload]
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
        <main className={styles.content} ref={contentRef}>
          {/* Fixed Chrome: Status Section */}
          <StatusSection
            value={(currentValues["statuscode"] as StatusReasonValue) ?? 1}
            onChange={handleStatusChange}
            disabled={isDisabled}
          />

          {/* Config-Driven: Dynamic Form Sections from Event Type JSON */}
          <FormRenderer
            config={formConfig.formConfig}
            entityName="sprk_event"
            values={currentValues}
            onChange={handleFieldChange}
            disabled={isDisabled}
            isLoading={formConfig.isLoading}
            sectionStates={sectionStates}
            onSectionExpandedChange={handleSectionExpandedChange}
          />

          {/* Fixed Chrome: Memo Section */}
          <MemoSection
            eventId={params.eventId}
            disabled={isDisabled}
          />

          {/* Fixed Chrome: To Do Section */}
          <TodoSection
            eventId={params.eventId}
            disabled={isDisabled}
          />
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
          version="2.1.0"
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
