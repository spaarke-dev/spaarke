/**
 * useEventTypeConfig - Hook for fetching Event Type field configuration
 *
 * Queries the EventTypeService to determine which fields/sections should be
 * visible based on the Event Type of the current event.
 *
 * This hook bridges the shared EventTypeService with the Custom Page context,
 * wrapping Xrm.WebApi to work with the context-agnostic service interface.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/039-integrate-eventtypeservice.poml
 * @see EventTypeService from @spaarke/ui-components
 * @see ADR-012 - Shared Component Library
 */

import * as React from "react";

// Import from shared library - types are inlined since workspace link may not be available
// In a production build, these would be imported from @spaarke/ui-components

// ─────────────────────────────────────────────────────────────────────────────
// Types (inlined from shared library for Custom Page isolation)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Section collapse state options
 */
export type SectionCollapseState = "expanded" | "collapsed";

/**
 * Default collapse states for form sections
 */
export interface ISectionDefaults {
  dates?: SectionCollapseState;
  relatedEvent?: SectionCollapseState;
  description?: SectionCollapseState;
  history?: SectionCollapseState;
}

/**
 * Event Type field configuration
 */
export interface IEventTypeFieldConfig {
  visibleFields?: string[];
  hiddenFields?: string[];
  requiredFields?: string[];
  optionalFields?: string[];
  hiddenSections?: string[];
  sectionDefaults?: ISectionDefaults;
}

/**
 * Computed field state after applying configuration
 */
export interface IComputedFieldState {
  fieldName: string;
  isVisible: boolean;
  requiredLevel: "required" | "recommended" | "none";
  isOverridden: boolean;
}

/**
 * Computed section state
 */
export interface IComputedSectionState {
  sectionName: string;
  isVisible: boolean;
  collapseState: SectionCollapseState;
}

/**
 * Computed field states result
 */
export interface IComputedFieldStates {
  fields: Map<string, IComputedFieldState>;
  sections: Map<string, IComputedSectionState>;
  sectionDefaults: ISectionDefaults;
  sourceConfig: IEventTypeFieldConfig | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Default States (same as EventTypeService)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Default section collapse states
 */
export const DEFAULT_SECTION_STATES: ISectionDefaults = {
  dates: "expanded",
  relatedEvent: "collapsed",
  description: "expanded",
  history: "collapsed",
};

/**
 * All controllable section names
 */
export const ALL_SECTION_NAMES = ["dates", "relatedEvent", "description", "history"] as const;
export type SectionName = (typeof ALL_SECTION_NAMES)[number];

// ─────────────────────────────────────────────────────────────────────────────
// Hook Result Type
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result of useEventTypeConfig hook
 */
export interface UseEventTypeConfigResult {
  /** Whether the config is loading */
  isLoading: boolean;
  /** Error message if loading failed */
  error: string | null;
  /** Raw field configuration from Event Type */
  config: IEventTypeFieldConfig | null;
  /** Computed field states */
  fieldStates: IComputedFieldStates | null;
  /** Check if a field should be visible */
  isFieldVisible: (fieldName: string) => boolean;
  /** Check if a field is required */
  isFieldRequired: (fieldName: string) => boolean;
  /** Check if a section should be visible */
  isSectionVisible: (sectionName: SectionName) => boolean;
  /** Get section collapse default */
  getSectionCollapseState: (sectionName: SectionName) => SectionCollapseState;
}

// ─────────────────────────────────────────────────────────────────────────────
// Xrm.WebApi Access
// ─────────────────────────────────────────────────────────────────────────────

interface IXrmWebApi {
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;
}

/**
 * Get the Xrm.WebApi object from window context
 */
function getXrmWebApi(): IXrmWebApi | null {
  try {
    // Try window.parent.Xrm first (Custom Page in iframe)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi) {
      return parentXrm.WebApi as IXrmWebApi;
    }

    // Try window.Xrm (direct access)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.WebApi) {
      return windowXrm.WebApi as IXrmWebApi;
    }

    return null;
  } catch {
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Config Parsing (inlined from EventTypeService for isolation)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parse the sprk_fieldconfigjson field from an Event Type record
 */
function parseFieldConfigJson(jsonString: string | null | undefined): IEventTypeFieldConfig | null {
  if (!jsonString || jsonString.trim() === "") {
    return null;
  }

  try {
    const parsed = JSON.parse(jsonString);

    if (typeof parsed !== "object" || parsed === null) {
      console.warn("[useEventTypeConfig] Invalid config JSON - not an object");
      return null;
    }

    const config: IEventTypeFieldConfig = {};

    if (Array.isArray(parsed.visibleFields)) {
      config.visibleFields = parsed.visibleFields.filter(
        (f: unknown) => typeof f === "string"
      );
    }

    if (Array.isArray(parsed.hiddenFields)) {
      config.hiddenFields = parsed.hiddenFields.filter(
        (f: unknown) => typeof f === "string"
      );
    }

    if (Array.isArray(parsed.requiredFields)) {
      config.requiredFields = parsed.requiredFields.filter(
        (f: unknown) => typeof f === "string"
      );
    }

    if (Array.isArray(parsed.optionalFields)) {
      config.optionalFields = parsed.optionalFields.filter(
        (f: unknown) => typeof f === "string"
      );
    }

    if (Array.isArray(parsed.hiddenSections)) {
      const validSections = new Set(ALL_SECTION_NAMES);
      config.hiddenSections = parsed.hiddenSections.filter(
        (s: unknown) => typeof s === "string" && validSections.has(s as SectionName)
      );
    }

    if (parsed.sectionDefaults && typeof parsed.sectionDefaults === "object") {
      config.sectionDefaults = parseSectionDefaults(parsed.sectionDefaults);
    }

    return config;
  } catch (error) {
    console.warn("[useEventTypeConfig] Failed to parse config JSON:", error);
    return null;
  }
}

/**
 * Parse section defaults from configuration
 */
function parseSectionDefaults(defaults: Record<string, unknown>): ISectionDefaults {
  const result: ISectionDefaults = {};
  const validStates = ["expanded", "collapsed"];

  for (const key of ["dates", "relatedEvent", "description", "history"]) {
    const value = defaults[key];
    if (typeof value === "string" && validStates.includes(value)) {
      result[key as keyof ISectionDefaults] = value as SectionCollapseState;
    }
  }

  return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// Field State Computation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Default field states
 */
const DEFAULT_FIELD_STATES: Record<string, { visible: boolean; requiredLevel: "required" | "recommended" | "none" }> = {
  sprk_eventname: { visible: true, requiredLevel: "required" },
  sprk_description: { visible: true, requiredLevel: "none" },
  sprk_basedate: { visible: true, requiredLevel: "none" },
  sprk_duedate: { visible: true, requiredLevel: "none" },
  sprk_finalduedate: { visible: true, requiredLevel: "none" },
  sprk_completeddate: { visible: true, requiredLevel: "none" },
  scheduledstart: { visible: true, requiredLevel: "none" },
  scheduledend: { visible: true, requiredLevel: "none" },
  sprk_location: { visible: true, requiredLevel: "none" },
  sprk_remindat: { visible: true, requiredLevel: "none" },
  statecode: { visible: true, requiredLevel: "none" },
  statuscode: { visible: true, requiredLevel: "none" },
  sprk_priority: { visible: true, requiredLevel: "none" },
  sprk_source: { visible: true, requiredLevel: "none" },
  sprk_relatedevent: { visible: true, requiredLevel: "none" },
  sprk_relatedeventtype: { visible: true, requiredLevel: "none" },
  sprk_relatedeventoffsettype: { visible: true, requiredLevel: "none" },
};

/**
 * Compute field states from Event Type configuration
 */
function computeFieldStates(config: IEventTypeFieldConfig | null): IComputedFieldStates {
  const fields = new Map<string, IComputedFieldState>();

  // Start with defaults
  for (const [fieldName, defaultState] of Object.entries(DEFAULT_FIELD_STATES)) {
    fields.set(fieldName, {
      fieldName,
      isVisible: defaultState.visible,
      requiredLevel: defaultState.requiredLevel,
      isOverridden: false,
    });
  }

  // Apply configuration if provided
  if (config) {
    // Apply hidden fields first (lowest priority for visibility)
    if (config.hiddenFields) {
      for (const fieldName of config.hiddenFields) {
        const state = fields.get(fieldName);
        if (state) {
          state.isVisible = false;
          state.requiredLevel = "none";
          state.isOverridden = true;
        }
      }
    }

    // Apply visible fields (overrides hidden if both specified)
    if (config.visibleFields) {
      for (const fieldName of config.visibleFields) {
        const state = fields.get(fieldName);
        if (state) {
          state.isVisible = true;
          state.isOverridden = true;
        }
      }
    }

    // Apply optional fields
    if (config.optionalFields) {
      for (const fieldName of config.optionalFields) {
        const state = fields.get(fieldName);
        if (state) {
          state.requiredLevel = "none";
          state.isOverridden = true;
        }
      }
    }

    // Apply required fields (highest priority - also makes visible)
    if (config.requiredFields) {
      for (const fieldName of config.requiredFields) {
        const state = fields.get(fieldName);
        if (state) {
          state.isVisible = true;
          state.requiredLevel = "required";
          state.isOverridden = true;
        }
      }
    }
  }

  // Compute section defaults
  const sectionDefaults: ISectionDefaults = {
    ...DEFAULT_SECTION_STATES,
    ...config?.sectionDefaults,
  };

  // Compute section states
  const sections = new Map<string, IComputedSectionState>();
  const hiddenSectionsSet = new Set(config?.hiddenSections ?? []);

  for (const sectionName of ALL_SECTION_NAMES) {
    sections.set(sectionName, {
      sectionName,
      isVisible: !hiddenSectionsSet.has(sectionName),
      collapseState: sectionDefaults[sectionName] ?? "expanded",
    });
  }

  return {
    fields,
    sections,
    sectionDefaults,
    sourceConfig: config,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Entity name for Event Type in Dataverse
 */
const EVENT_TYPE_ENTITY = "sprk_eventtype";

/**
 * Fields to retrieve from Event Type record
 */
const EVENT_TYPE_SELECT_FIELDS = "sprk_eventtypeid,sprk_name,sprk_fieldconfigjson";

/**
 * Hook to fetch and compute Event Type field configuration
 *
 * @param eventTypeId - GUID of the Event Type (from event record)
 * @returns Configuration result with helper functions
 *
 * @example
 * ```tsx
 * const { isFieldVisible, isSectionVisible, getSectionCollapseState } =
 *   useEventTypeConfig(event._sprk_eventtype_value);
 *
 * // Check field visibility
 * if (isFieldVisible("sprk_basedate")) {
 *   // Show base date field
 * }
 *
 * // Check section visibility
 * if (isSectionVisible("dates")) {
 *   // Render dates section
 * }
 *
 * // Get section default collapse state
 * const datesExpanded = getSectionCollapseState("dates") === "expanded";
 * ```
 */
export function useEventTypeConfig(eventTypeId: string | undefined | null): UseEventTypeConfigResult {
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [config, setConfig] = React.useState<IEventTypeFieldConfig | null>(null);
  const [fieldStates, setFieldStates] = React.useState<IComputedFieldStates | null>(null);

  // Fetch Event Type configuration when eventTypeId changes
  React.useEffect(() => {
    // If no event type, use defaults
    if (!eventTypeId) {
      const defaultStates = computeFieldStates(null);
      setConfig(null);
      setFieldStates(defaultStates);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;

    const fetchConfig = async () => {
      setIsLoading(true);
      setError(null);

      const webApi = getXrmWebApi();
      if (!webApi) {
        console.warn("[useEventTypeConfig] Xrm.WebApi not available, using defaults");
        const defaultStates = computeFieldStates(null);
        setConfig(null);
        setFieldStates(defaultStates);
        setIsLoading(false);
        return;
      }

      try {
        // Normalize GUID
        const normalizedId = eventTypeId.replace(/[{}]/g, "").toLowerCase();

        // Query Dataverse for Event Type record
        const record = await webApi.retrieveRecord(
          EVENT_TYPE_ENTITY,
          normalizedId,
          `?$select=${EVENT_TYPE_SELECT_FIELDS}`
        );

        if (cancelled) return;

        // Extract and parse field config JSON
        const fieldConfigJson = (record["sprk_fieldconfigjson"] as string) ?? null;
        const parsedConfig = parseFieldConfigJson(fieldConfigJson);

        // Compute field states from configuration
        const computed = computeFieldStates(parsedConfig);

        setConfig(parsedConfig);
        setFieldStates(computed);

        console.log(
          "[useEventTypeConfig] Loaded config for Event Type:",
          record["sprk_name"],
          "- Config:",
          parsedConfig
        );
      } catch (err) {
        if (cancelled) return;

        const errorMessage = err instanceof Error ? err.message : String(err);

        // Check for 404 Not Found
        if (
          errorMessage.includes("404") ||
          errorMessage.toLowerCase().includes("not found")
        ) {
          console.warn(`[useEventTypeConfig] Event Type not found: ${eventTypeId}, using defaults`);
          const defaultStates = computeFieldStates(null);
          setConfig(null);
          setFieldStates(defaultStates);
          setError(null);
        } else {
          console.error("[useEventTypeConfig] Error loading config:", errorMessage);
          // On error, still use defaults so UI can render
          const defaultStates = computeFieldStates(null);
          setConfig(null);
          setFieldStates(defaultStates);
          setError(errorMessage);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    fetchConfig();

    return () => {
      cancelled = true;
    };
  }, [eventTypeId]);

  // ─────────────────────────────────────────────────────────────────────────
  // Helper Functions (memoized)
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Check if a field should be visible
   */
  const isFieldVisible = React.useCallback(
    (fieldName: string): boolean => {
      if (!fieldStates) return true; // Default to visible if no config
      const state = fieldStates.fields.get(fieldName);
      return state?.isVisible ?? true;
    },
    [fieldStates]
  );

  /**
   * Check if a field is required
   */
  const isFieldRequired = React.useCallback(
    (fieldName: string): boolean => {
      if (!fieldStates) return false;
      const state = fieldStates.fields.get(fieldName);
      return state?.requiredLevel === "required";
    },
    [fieldStates]
  );

  /**
   * Check if a section should be visible
   */
  const isSectionVisible = React.useCallback(
    (sectionName: SectionName): boolean => {
      if (!fieldStates) return true; // Default to visible if no config
      const state = fieldStates.sections.get(sectionName);
      return state?.isVisible ?? true;
    },
    [fieldStates]
  );

  /**
   * Get section collapse default
   */
  const getSectionCollapseState = React.useCallback(
    (sectionName: SectionName): SectionCollapseState => {
      if (!fieldStates) return DEFAULT_SECTION_STATES[sectionName] ?? "expanded";
      const state = fieldStates.sections.get(sectionName);
      return state?.collapseState ?? "expanded";
    },
    [fieldStates]
  );

  return {
    isLoading,
    error,
    config,
    fieldStates,
    isFieldVisible,
    isFieldRequired,
    isSectionVisible,
    getSectionCollapseState,
  };
}

export default useEventTypeConfig;
