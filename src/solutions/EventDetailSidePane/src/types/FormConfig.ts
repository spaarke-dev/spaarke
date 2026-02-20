/**
 * FormConfig - JSON Schema Types for Dynamic Form Renderer (Approach A)
 *
 * These types define the structure of the sprk_fieldconfigjson field on
 * entity type records (sprk_eventtype, sprk_mattertype, etc.).
 *
 * The JSON IS the form definition — it declares which sections exist,
 * which fields appear, and in what order. If it's not in the JSON,
 * it doesn't render.
 *
 * @see approach-a-dynamic-form-renderer.md
 * @see ADR-012 - Shared Component Library
 */

// ─────────────────────────────────────────────────────────────────────────────
// Field Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Supported field renderer types.
 * Each maps to a specific renderer component.
 */
export type FieldType =
  | "text"       // Single-line text input
  | "multiline"  // Multi-line textarea
  | "date"       // Date picker (date only)
  | "datetime"   // Date + time picker
  | "choice"     // Dropdown / optionset (options fetched from metadata)
  | "lookup"     // Dataverse lookup (opens Xrm.Utility.lookupObjects)
  | "url";       // URL input with clickable link

/**
 * Configuration for a single field within a section.
 */
export interface IFieldConfig {
  /** Dataverse logical name (e.g., "sprk_duedate", "sprk_assignedto") */
  name: string;
  /** Field renderer type — determines which component renders this field */
  type: FieldType;
  /** Display label shown above the field */
  label: string;
  /** Whether this field is required for save validation (default: false) */
  required?: boolean;
  /** Whether this field is always read-only regardless of record permissions (default: false) */
  readOnly?: boolean;
  /**
   * Lookup target entity logical names.
   * Required when type is "lookup".
   * @example ["contact"] or ["systemuser", "team"]
   */
  targets?: string[];
  /**
   * Navigation property name for @odata.bind when saving lookup values.
   * Uses SchemaName casing (PascalCase after prefix) — CASE-SENSITIVE.
   * Required for lookup fields that will be editable.
   * If omitted, falls back to the column logical name (which may fail).
   * @example "sprk_CompletedBy" (NOT "sprk_completedby")
   * @see .claude/patterns/dataverse/relationship-navigation.md
   */
  navigationProperty?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Section Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Configuration for a section (group of fields) in the side pane form.
 */
export interface ISectionConfig {
  /** Unique section identifier (used for React keys and state tracking) */
  id: string;
  /** Display title for the section header */
  title: string;
  /** Whether the section can be collapsed/expanded (default: true) */
  collapsible?: boolean;
  /** Whether the section starts expanded (default: true) */
  defaultExpanded?: boolean;
  /** Ordered array of fields to render in this section */
  fields: IFieldConfig[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Form Config (Root)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Root configuration stored in sprk_fieldconfigjson on entity type records.
 *
 * @example
 * ```json
 * {
 *   "version": 1,
 *   "sections": [
 *     {
 *       "id": "dates",
 *       "title": "Dates",
 *       "fields": [
 *         { "name": "sprk_duedate", "type": "date", "label": "Due Date" },
 *         { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
 *       ]
 *     }
 *   ]
 * }
 * ```
 */
export interface IFormConfig {
  /** Schema version for forward compatibility */
  version: number;
  /** Ordered array of sections to render */
  sections: ISectionConfig[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Field Metadata (Runtime — fetched from Dataverse)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * A single option in a choice/optionset field.
 */
export interface IChoiceOption {
  /** Numeric value stored in Dataverse */
  value: number;
  /** Display label */
  label: string;
}

/**
 * Runtime metadata for a field, fetched from Dataverse entity metadata.
 * Used primarily for choice fields to get optionset labels.
 */
export interface IFieldMetadata {
  /** Field logical name */
  fieldName: string;
  /** Optionset options (only for choice fields) */
  options?: IChoiceOption[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Lookup Value (for tracking lookup field state)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Represents a resolved lookup value with display info.
 */
export interface ILookupValue {
  /** Record GUID (without braces) */
  id: string;
  /** Display name of the referenced record */
  name: string;
  /** Entity logical name of the referenced record */
  entityType: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Form Renderer Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Field change callback signature.
 * Used by all field renderers to notify parent of value changes.
 *
 * For regular fields: onChange("sprk_duedate", "2026-02-20")
 * For lookup fields: onChange("sprk_assignedto", { id: "guid", name: "John", entityType: "contact" })
 * For clearing a lookup: onChange("sprk_assignedto", null)
 */
export type FieldChangeCallback = (fieldName: string, value: unknown) => void;

// ─────────────────────────────────────────────────────────────────────────────
// Config Parsing
// ─────────────────────────────────────────────────────────────────────────────

/** Current schema version */
export const FORM_CONFIG_VERSION = 1;

/**
 * Parse and validate a JSON string into IFormConfig.
 * Returns null if the JSON is invalid or empty.
 */
export function parseFormConfig(jsonString: string | null | undefined): IFormConfig | null {
  if (!jsonString || jsonString.trim() === "") {
    return null;
  }

  try {
    const parsed = JSON.parse(jsonString);

    if (typeof parsed !== "object" || parsed === null) {
      console.warn("[FormConfig] Invalid config JSON — not an object");
      return null;
    }

    // Validate version
    if (typeof parsed.version !== "number") {
      console.warn("[FormConfig] Missing or invalid version field");
      return null;
    }

    // Validate sections array
    if (!Array.isArray(parsed.sections)) {
      console.warn("[FormConfig] Missing or invalid sections array");
      return null;
    }

    // Validate each section
    const sections: ISectionConfig[] = [];
    for (const rawSection of parsed.sections) {
      const section = parseSection(rawSection);
      if (section) {
        sections.push(section);
      }
    }

    if (sections.length === 0) {
      console.warn("[FormConfig] No valid sections found");
      return null;
    }

    return {
      version: parsed.version,
      sections,
    };
  } catch (error) {
    console.warn("[FormConfig] Failed to parse JSON:", error);
    return null;
  }
}

/**
 * Parse a single section from raw JSON.
 */
function parseSection(raw: unknown): ISectionConfig | null {
  if (typeof raw !== "object" || raw === null) return null;

  const obj = raw as Record<string, unknown>;

  if (typeof obj.id !== "string" || typeof obj.title !== "string") {
    console.warn("[FormConfig] Section missing id or title");
    return null;
  }

  if (!Array.isArray(obj.fields)) {
    console.warn("[FormConfig] Section missing fields array:", obj.id);
    return null;
  }

  const fields: IFieldConfig[] = [];
  for (const rawField of obj.fields) {
    const field = parseField(rawField);
    if (field) {
      fields.push(field);
    }
  }

  if (fields.length === 0) {
    console.warn("[FormConfig] Section has no valid fields:", obj.id);
    return null;
  }

  return {
    id: obj.id,
    title: obj.title,
    collapsible: typeof obj.collapsible === "boolean" ? obj.collapsible : true,
    defaultExpanded: typeof obj.defaultExpanded === "boolean" ? obj.defaultExpanded : true,
    fields,
  };
}

/** Valid field types */
const VALID_FIELD_TYPES: Set<string> = new Set([
  "text", "multiline", "date", "datetime", "choice", "lookup", "url",
]);

/**
 * Parse a single field from raw JSON.
 */
function parseField(raw: unknown): IFieldConfig | null {
  if (typeof raw !== "object" || raw === null) return null;

  const obj = raw as Record<string, unknown>;

  if (typeof obj.name !== "string" || typeof obj.type !== "string" || typeof obj.label !== "string") {
    console.warn("[FormConfig] Field missing name, type, or label");
    return null;
  }

  if (!VALID_FIELD_TYPES.has(obj.type)) {
    console.warn("[FormConfig] Unknown field type:", obj.type);
    return null;
  }

  // Lookup fields must have targets
  if (obj.type === "lookup") {
    if (!Array.isArray(obj.targets) || obj.targets.length === 0) {
      console.warn("[FormConfig] Lookup field missing targets:", obj.name);
      return null;
    }
  }

  return {
    name: obj.name,
    type: obj.type as FieldType,
    label: obj.label,
    required: typeof obj.required === "boolean" ? obj.required : undefined,
    readOnly: typeof obj.readOnly === "boolean" ? obj.readOnly : undefined,
    targets: Array.isArray(obj.targets) ? (obj.targets as string[]) : undefined,
    navigationProperty: typeof obj.navigationProperty === "string" ? obj.navigationProperty : undefined,
  };
}

/**
 * Extract all unique field names from a form config.
 * Used to build the OData $select query.
 */
export function extractFieldNames(config: IFormConfig): string[] {
  const names = new Set<string>();

  for (const section of config.sections) {
    for (const field of section.fields) {
      if (field.type === "lookup") {
        // Lookup fields use _fieldname_value format in OData
        names.add(`_${field.name}_value`);
      } else {
        names.add(field.name);
      }
    }
  }

  return Array.from(names);
}

/**
 * Extract all choice field names from a form config.
 * Used to determine which fields need metadata (optionset labels).
 */
export function extractChoiceFieldNames(config: IFormConfig): string[] {
  const names: string[] = [];
  for (const section of config.sections) {
    for (const field of section.fields) {
      if (field.type === "choice") {
        names.push(field.name);
      }
    }
  }
  return names;
}
