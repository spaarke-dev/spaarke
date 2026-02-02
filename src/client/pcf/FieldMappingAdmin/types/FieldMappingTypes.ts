/**
 * Field Mapping Types for FieldMappingAdmin PCF
 *
 * Local type definitions that mirror the shared types from
 * @spaarke/ui-components but are standalone for PCF bundling.
 *
 * These should stay in sync with:
 * src/client/shared/Spaarke.UI.Components/src/types/FieldMappingTypes.ts
 */

/**
 * Field types supported by the mapping system.
 * Maps to sprk_sourcefieldtype and sprk_targetfieldtype choice fields.
 */
export enum FieldType {
    Text = 0,
    Lookup = 1,
    OptionSet = 2,
    Number = 3,
    DateTime = 4,
    Boolean = 5,
    Memo = 6,
}

/**
 * Display names for field types (for UI display)
 */
export const FieldTypeLabels: Record<FieldType, string> = {
    [FieldType.Text]: "Text",
    [FieldType.Lookup]: "Lookup",
    [FieldType.OptionSet]: "Option Set",
    [FieldType.Number]: "Number",
    [FieldType.DateTime]: "Date/Time",
    [FieldType.Boolean]: "Yes/No",
    [FieldType.Memo]: "Multiline Text",
};

/**
 * Mapping direction - controls which way field values flow.
 * Maps to sprk_mappingdirection choice field.
 */
export enum MappingDirection {
    ParentToChild = 0,
    ChildToParent = 1,
    Bidirectional = 2,
}

/**
 * Display names for mapping directions (for UI display)
 */
export const MappingDirectionLabels: Record<MappingDirection, string> = {
    [MappingDirection.ParentToChild]: "Parent to Child",
    [MappingDirection.ChildToParent]: "Child to Parent",
    [MappingDirection.Bidirectional]: "Bidirectional",
};

/**
 * Sync mode - controls when mappings are applied.
 * Maps to sprk_syncmode choice field.
 */
export enum SyncMode {
    /** Apply mappings once at child record creation */
    OneTime = 0,
    /** Apply mappings when user clicks "Refresh from Parent" on child form */
    ManualRefresh = 1,
}

/**
 * Display names for sync modes (for UI display)
 */
export const SyncModeLabels: Record<SyncMode, string> = {
    [SyncMode.OneTime]: "One-time (at creation)",
    [SyncMode.ManualRefresh]: "Manual Refresh",
};

/**
 * Compatibility mode - controls type validation strictness.
 * Maps to sprk_compatibilitymode choice field.
 */
export enum CompatibilityMode {
    /** Only allow exact type matches or known-safe conversions */
    Strict = 0,
    /** Allow type resolution (e.g., Text -> Lookup by name matching) - future */
    Resolve = 1,
}

/**
 * Display names for compatibility modes (for UI display)
 */
export const CompatibilityModeLabels: Record<CompatibilityMode, string> = {
    [CompatibilityMode.Strict]: "Strict",
    [CompatibilityMode.Resolve]: "Resolve (future)",
};

/**
 * Field Mapping Profile - defines a mapping between two entities.
 * Maps to sprk_fieldmappingprofile Dataverse table.
 */
export interface IFieldMappingProfile {
    /** Profile record ID (GUID) */
    id: string;
    /** Profile name (sprk_name) */
    name: string;
    /** Source entity logical name (e.g., "sprk_matter") */
    sourceEntity: string;
    /** Target entity logical name (e.g., "sprk_event") */
    targetEntity: string;
    /** Direction of field value flow */
    mappingDirection: MappingDirection;
    /** When to apply mappings */
    syncMode: SyncMode;
    /** Whether this profile is active */
    isActive: boolean;
    /** Admin notes */
    description?: string;
    /** Loaded rules for this profile (populated by getRulesForProfile) */
    rules?: IFieldMappingRule[];
}

/**
 * Field Mapping Rule - defines a single field-to-field mapping.
 * Maps to sprk_fieldmappingrule Dataverse table.
 */
export interface IFieldMappingRule {
    /** Rule record ID (GUID) */
    id: string;
    /** Parent profile ID */
    profileId: string;
    /** Source field schema name (e.g., "sprk_client") */
    sourceField: string;
    /** Source field type */
    sourceFieldType: FieldType;
    /** Target field schema name (e.g., "sprk_regardingaccount") */
    targetField: string;
    /** Target field type */
    targetFieldType: FieldType;
    /** Type validation strictness */
    compatibilityMode: CompatibilityMode;
    /** Whether mapping fails if source value is empty */
    isRequired: boolean;
    /** Default value when source is empty */
    defaultValue?: string;
    /** Whether this field can trigger secondary cascading mappings */
    isCascadingSource: boolean;
    /** Execution order for dependent mappings */
    executionOrder: number;
    /** Whether this rule is active */
    isActive: boolean;
    /** Rule name (sprk_name) */
    name?: string;
}

/**
 * Type compatibility validation result.
 */
export interface ITypeCompatibilityResult {
    /** Whether the types are compatible */
    isCompatible: boolean;
    /** Compatibility level */
    level: CompatibilityLevel;
    /** Warning messages (for near-compatible types) */
    warnings: string[];
    /** Error messages (for incompatible types) */
    errors: string[];
}

/**
 * Compatibility levels for type matching.
 */
export enum CompatibilityLevel {
    /** Types are exactly the same */
    Exact = "exact",
    /** Types are different but conversion is safe (e.g., Text -> Memo) */
    SafeConversion = "safe_conversion",
    /** Types require resolution logic (future) */
    RequiresResolve = "requires_resolve",
    /** Types are incompatible */
    Incompatible = "incompatible",
}

/**
 * Type compatibility matrix for Strict mode.
 * Key is source type, value is array of compatible target types.
 */
export const STRICT_TYPE_COMPATIBILITY: Record<FieldType, FieldType[]> = {
    [FieldType.Lookup]: [FieldType.Lookup, FieldType.Text],
    [FieldType.Text]: [FieldType.Text, FieldType.Memo],
    [FieldType.Memo]: [FieldType.Text, FieldType.Memo],
    [FieldType.OptionSet]: [FieldType.OptionSet, FieldType.Text],
    [FieldType.Number]: [FieldType.Number, FieldType.Text],
    [FieldType.DateTime]: [FieldType.DateTime, FieldType.Text],
    [FieldType.Boolean]: [FieldType.Boolean, FieldType.Text],
};

/**
 * Error codes for mapping failures.
 */
export enum MappingErrorCode {
    /** Source field not found on source record */
    SourceFieldNotFound = "SOURCE_FIELD_NOT_FOUND",
    /** Target field not found on target record/form */
    TargetFieldNotFound = "TARGET_FIELD_NOT_FOUND",
    /** Source and target field types are incompatible */
    TypeMismatch = "TYPE_MISMATCH",
    /** Required source field has no value and no default */
    RequiredFieldEmpty = "REQUIRED_FIELD_EMPTY",
    /** Profile not found for entity pair */
    ProfileNotFound = "PROFILE_NOT_FOUND",
    /** Profile exists but is inactive */
    ProfileInactive = "PROFILE_INACTIVE",
    /** Cascading loop detected (exceeded two-pass limit) */
    CascadingLoopDetected = "CASCADING_LOOP_DETECTED",
    /** Dataverse API call failed */
    DataverseError = "DATAVERSE_ERROR",
    /** Unknown error */
    Unknown = "UNKNOWN",
}

/**
 * Error that occurred during mapping application.
 */
export interface IMappingError {
    /** Rule that caused the error */
    ruleId: string;
    /** Source field that failed */
    sourceField: string;
    /** Target field that failed */
    targetField: string;
    /** Error message */
    message: string;
    /** Error code for programmatic handling */
    code: MappingErrorCode;
}

/**
 * Result of applying field mappings.
 */
export interface IMappingResult {
    /** Whether all required mappings succeeded */
    success: boolean;
    /** Number of rules successfully applied */
    appliedRules: number;
    /** Number of rules skipped (e.g., inactive, optional with empty source) */
    skippedRules: number;
    /** Total rules processed */
    totalRules: number;
    /** Fields that were mapped (target field names) */
    fieldsMapped: string[];
    /** Errors that occurred during mapping */
    errors: IMappingError[];
    /** Pass number (1 or 2) - for cascading tracking */
    pass?: number;
    /** Source record ID that was mapped from */
    sourceRecordId?: string;
    /** Profile that was applied */
    profileId?: string;
}
