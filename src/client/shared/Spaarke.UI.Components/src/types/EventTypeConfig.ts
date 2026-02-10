/**
 * EventTypeConfig - Type definitions for Event Type field configuration
 *
 * These types support the EventTypeService for querying Event Type field visibility
 * and requirement configurations. Used by both EventFormController and EventDetailSidePane.
 *
 * @see ADR-012 - Shared Component Library
 * @see spec.md - Event Type Field Configuration section
 */

/**
 * Field visibility and requirement rule for a single field
 */
export interface IFieldRule {
  /** Schema name of the field (e.g., "sprk_duedate") */
  fieldName: string;
  /** Display name for user-friendly messages (e.g., "Due Date") */
  displayName?: string;
  /** Whether the field should be visible */
  isVisible: boolean;
  /** Whether the field is required */
  isRequired: boolean;
}

/**
 * Dataverse requirement level values
 */
export type RequiredLevel = "required" | "recommended" | "none";

/**
 * Default state configuration for a field
 */
export interface IFieldDefaultState {
  /** Whether the field should be visible by default */
  visible: boolean;
  /** Requirement level for the field */
  requiredLevel: RequiredLevel;
}

/**
 * Map of field names to their default states
 */
export interface IFieldDefaultStates {
  [fieldName: string]: IFieldDefaultState;
}

/**
 * Section collapse state options
 */
export type SectionCollapseState = "expanded" | "collapsed";

/**
 * Default collapse states for form sections
 */
export interface ISectionDefaults {
  /** Dates section (Base Date, Due Date, Completed Date, etc.) */
  dates?: SectionCollapseState;
  /** Related Event section */
  relatedEvent?: SectionCollapseState;
  /** Description section */
  description?: SectionCollapseState;
  /** History section (for side pane) */
  history?: SectionCollapseState;
}

/**
 * Event Type field configuration stored in sprk_fieldconfigjson
 *
 * This JSON is stored on the sprk_eventtype entity and defines
 * which fields should be visible/required for events of that type.
 *
 * @example
 * ```json
 * {
 *   "visibleFields": ["sprk_duedate", "sprk_priority"],
 *   "hiddenFields": ["sprk_completeddate"],
 *   "requiredFields": ["sprk_duedate"],
 *   "hiddenSections": ["dates"],
 *   "sectionDefaults": {
 *     "dates": "expanded",
 *     "relatedEvent": "collapsed"
 *   }
 * }
 * ```
 */
export interface IEventTypeFieldConfig {
  /**
   * Fields that should be explicitly visible
   * If not specified, fields use their default visibility
   */
  visibleFields?: string[];

  /**
   * Fields that should be hidden for this event type
   */
  hiddenFields?: string[];

  /**
   * Fields that are required for this event type
   * Required fields are automatically made visible
   */
  requiredFields?: string[];

  /**
   * Optional fields that should not be required
   * Overrides any default requirement level
   */
  optionalFields?: string[];

  /**
   * Sections that should be hidden on main Dataverse forms.
   * Uses Dataverse section.setVisible(false) API.
   * Valid section names: "dates", "relatedEvent", "description"
   */
  hiddenSections?: string[];

  /**
   * Default collapse states for form sections.
   * Used by Custom Pages and PCF controls (React state).
   * Main Dataverse forms do NOT support programmatic collapse control.
   */
  sectionDefaults?: ISectionDefaults;
}

/**
 * Result of applying field configuration rules
 */
export interface IApplyRulesResult {
  /** Whether all rules were applied successfully */
  success: boolean;
  /** Number of rules that were applied */
  rulesApplied: number;
  /** Field names that were skipped (not found on form) */
  skippedFields: string[];
  /** Error messages if any rules failed */
  errors: string[];
}

/**
 * Event Type record from Dataverse
 */
export interface IEventType {
  /** Event Type record ID (GUID) */
  id: string;
  /** Event Type name */
  name: string;
  /** Field configuration JSON (sprk_fieldconfigjson) */
  fieldConfigJson?: string;
  /** Parsed field configuration */
  fieldConfig?: IEventTypeFieldConfig;
}

/**
 * Options for the EventTypeService configuration fetching
 */
export interface IEventTypeServiceOptions {
  /**
   * Whether to cache fetched configurations
   * @default false
   */
  enableCache?: boolean;

  /**
   * Cache time-to-live in milliseconds
   * @default 300000 (5 minutes)
   */
  cacheTtlMs?: number;
}

/**
 * Cache entry for event type configurations
 */
export interface IEventTypeConfigCacheEntry {
  /** Cached configuration */
  config: IEventTypeFieldConfig;
  /** Timestamp when cached */
  cachedAt: number;
  /** Timestamp when cache expires */
  expiresAt: number;
}

/**
 * Computed field state after applying configuration
 */
export interface IComputedFieldState {
  /** Field schema name */
  fieldName: string;
  /** Computed visibility */
  isVisible: boolean;
  /** Computed required level */
  requiredLevel: RequiredLevel;
  /** Whether this differs from the default */
  isOverridden: boolean;
}

/**
 * Computed section visibility state
 */
export interface IComputedSectionState {
  /** Section name */
  sectionName: string;
  /** Whether the section is visible (for main forms via section.setVisible) */
  isVisible: boolean;
  /** Default collapse state (for custom pages/PCF controls) */
  collapseState: SectionCollapseState;
}

/**
 * Result of computing field states from configuration
 */
export interface IComputedFieldStates {
  /** Map of field names to their computed states */
  fields: Map<string, IComputedFieldState>;
  /** Map of section names to their computed visibility/collapse states */
  sections: Map<string, IComputedSectionState>;
  /** Section default states (legacy - use sections Map instead) */
  sectionDefaults: ISectionDefaults;
  /** The source configuration that was applied */
  sourceConfig: IEventTypeFieldConfig | null;
}
