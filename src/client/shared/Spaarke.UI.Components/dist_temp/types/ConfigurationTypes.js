/**
 * Configuration Types for Grid Configuration Service
 *
 * Type definitions for sprk_gridconfiguration Dataverse entity.
 * Used by ConfigurationService and ViewService.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 */
/**
 * View type options from sprk_viewtype choice field
 */
export var GridConfigViewType;
(function (GridConfigViewType) {
    /** Reference to existing savedquery view */
    GridConfigViewType[GridConfigViewType["SavedView"] = 1] = "SavedView";
    /** Inline FetchXML and layout */
    GridConfigViewType[GridConfigViewType["CustomFetchXML"] = 2] = "CustomFetchXML";
    /** Reference to another configuration (for reuse) */
    GridConfigViewType[GridConfigViewType["LinkedView"] = 3] = "LinkedView";
})(GridConfigViewType || (GridConfigViewType = {}));
//# sourceMappingURL=ConfigurationTypes.js.map