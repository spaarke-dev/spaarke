/**
 * Field Mapping Framework Types
 *
 * Core types for the Field Mapping Framework that enables admin-configurable
 * field-to-field mappings between parent and child entities.
 *
 * @see spec.md - Field Mapping Framework section for full requirements
 */
/**
 * Field types supported by the mapping system.
 * Maps to sprk_sourcefieldtype and sprk_targetfieldtype choice fields.
 */
export var FieldType;
(function (FieldType) {
    FieldType[FieldType["Text"] = 0] = "Text";
    FieldType[FieldType["Lookup"] = 1] = "Lookup";
    FieldType[FieldType["OptionSet"] = 2] = "OptionSet";
    FieldType[FieldType["Number"] = 3] = "Number";
    FieldType[FieldType["DateTime"] = 4] = "DateTime";
    FieldType[FieldType["Boolean"] = 5] = "Boolean";
    FieldType[FieldType["Memo"] = 6] = "Memo";
})(FieldType || (FieldType = {}));
/**
 * Display names for field types (for UI display)
 */
export const FieldTypeLabels = {
    [FieldType.Text]: 'Text',
    [FieldType.Lookup]: 'Lookup',
    [FieldType.OptionSet]: 'Option Set',
    [FieldType.Number]: 'Number',
    [FieldType.DateTime]: 'Date/Time',
    [FieldType.Boolean]: 'Yes/No',
    [FieldType.Memo]: 'Multiline Text',
};
/**
 * Mapping direction - controls which way field values flow.
 * Maps to sprk_mappingdirection choice field.
 */
export var MappingDirection;
(function (MappingDirection) {
    MappingDirection[MappingDirection["ParentToChild"] = 0] = "ParentToChild";
    MappingDirection[MappingDirection["ChildToParent"] = 1] = "ChildToParent";
    MappingDirection[MappingDirection["Bidirectional"] = 2] = "Bidirectional";
})(MappingDirection || (MappingDirection = {}));
/**
 * Display names for mapping directions (for UI display)
 */
export const MappingDirectionLabels = {
    [MappingDirection.ParentToChild]: 'Parent to Child',
    [MappingDirection.ChildToParent]: 'Child to Parent',
    [MappingDirection.Bidirectional]: 'Bidirectional',
};
/**
 * Sync mode - controls when mappings are applied.
 * Maps to sprk_syncmode choice field.
 *
 * - One-time: Apply on create only
 * - ManualRefresh: Apply on user request (pull from parent)
 * - UpdateRelated: Apply on source record change (push to children) - handled via API
 */
export var SyncMode;
(function (SyncMode) {
    /** Apply mappings once at child record creation */
    SyncMode[SyncMode["OneTime"] = 0] = "OneTime";
    /** Apply mappings when user clicks "Refresh from Parent" on child form */
    SyncMode[SyncMode["ManualRefresh"] = 1] = "ManualRefresh";
})(SyncMode || (SyncMode = {}));
/**
 * Display names for sync modes (for UI display)
 */
export const SyncModeLabels = {
    [SyncMode.OneTime]: 'One-time (at creation)',
    [SyncMode.ManualRefresh]: 'Manual Refresh',
};
/**
 * Compatibility mode - controls type validation strictness.
 * Maps to sprk_compatibilitymode choice field.
 */
export var CompatibilityMode;
(function (CompatibilityMode) {
    /** Only allow exact type matches or known-safe conversions */
    CompatibilityMode[CompatibilityMode["Strict"] = 0] = "Strict";
    /** Allow type resolution (e.g., Text -> Lookup by name matching) - future */
    CompatibilityMode[CompatibilityMode["Resolve"] = 1] = "Resolve";
})(CompatibilityMode || (CompatibilityMode = {}));
/**
 * Display names for compatibility modes (for UI display)
 */
export const CompatibilityModeLabels = {
    [CompatibilityMode.Strict]: 'Strict',
    [CompatibilityMode.Resolve]: 'Resolve (future)',
};
/**
 * Error codes for mapping failures.
 */
export var MappingErrorCode;
(function (MappingErrorCode) {
    /** Source field not found on source record */
    MappingErrorCode["SourceFieldNotFound"] = "SOURCE_FIELD_NOT_FOUND";
    /** Target field not found on target record/form */
    MappingErrorCode["TargetFieldNotFound"] = "TARGET_FIELD_NOT_FOUND";
    /** Source and target field types are incompatible */
    MappingErrorCode["TypeMismatch"] = "TYPE_MISMATCH";
    /** Required source field has no value and no default */
    MappingErrorCode["RequiredFieldEmpty"] = "REQUIRED_FIELD_EMPTY";
    /** Profile not found for entity pair */
    MappingErrorCode["ProfileNotFound"] = "PROFILE_NOT_FOUND";
    /** Profile exists but is inactive */
    MappingErrorCode["ProfileInactive"] = "PROFILE_INACTIVE";
    /** Cascading loop detected (exceeded two-pass limit) */
    MappingErrorCode["CascadingLoopDetected"] = "CASCADING_LOOP_DETECTED";
    /** Dataverse API call failed */
    MappingErrorCode["DataverseError"] = "DATAVERSE_ERROR";
    /** Unknown error */
    MappingErrorCode["Unknown"] = "UNKNOWN";
})(MappingErrorCode || (MappingErrorCode = {}));
/**
 * Compatibility levels for type matching.
 */
export var CompatibilityLevel;
(function (CompatibilityLevel) {
    /** Types are exactly the same */
    CompatibilityLevel["Exact"] = "exact";
    /** Types are different but conversion is safe (e.g., Text -> Memo) */
    CompatibilityLevel["SafeConversion"] = "safe_conversion";
    /** Types require resolution logic (future) */
    CompatibilityLevel["RequiresResolve"] = "requires_resolve";
    /** Types are incompatible */
    CompatibilityLevel["Incompatible"] = "incompatible";
})(CompatibilityLevel || (CompatibilityLevel = {}));
/**
 * Type compatibility matrix for Strict mode.
 * Key is source type, value is array of compatible target types.
 */
export const STRICT_TYPE_COMPATIBILITY = {
    [FieldType.Lookup]: [FieldType.Lookup, FieldType.Text],
    [FieldType.Text]: [FieldType.Text, FieldType.Memo],
    [FieldType.Memo]: [FieldType.Text, FieldType.Memo],
    [FieldType.OptionSet]: [FieldType.OptionSet, FieldType.Text],
    [FieldType.Number]: [FieldType.Number, FieldType.Text],
    [FieldType.DateTime]: [FieldType.DateTime, FieldType.Text],
    [FieldType.Boolean]: [FieldType.Boolean, FieldType.Text],
};
//# sourceMappingURL=FieldMappingTypes.js.map