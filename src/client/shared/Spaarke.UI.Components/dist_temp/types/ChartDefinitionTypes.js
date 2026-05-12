/**
 * Chart Definition Types - Spaarke Visuals Framework
 * Mirrors sprk_chartdefinition entity schema from Dataverse
 * Project: visualization-module
 */
/**
 * Visual type enumeration matching Dataverse option set values
 * @see projects/visualization-module/notes/entity-schema.md
 */
export var VisualType;
(function (VisualType) {
    VisualType[VisualType["MetricCard"] = 100000000] = "MetricCard";
    VisualType[VisualType["BarChart"] = 100000001] = "BarChart";
    VisualType[VisualType["LineChart"] = 100000002] = "LineChart";
    VisualType[VisualType["AreaChart"] = 100000003] = "AreaChart";
    VisualType[VisualType["DonutChart"] = 100000004] = "DonutChart";
    VisualType[VisualType["StatusBar"] = 100000005] = "StatusBar";
    VisualType[VisualType["Calendar"] = 100000006] = "Calendar";
    VisualType[VisualType["MiniTable"] = 100000007] = "MiniTable";
})(VisualType || (VisualType = {}));
/**
 * Aggregation type enumeration matching Dataverse option set values
 */
export var AggregationType;
(function (AggregationType) {
    AggregationType[AggregationType["Count"] = 100000000] = "Count";
    AggregationType[AggregationType["Sum"] = 100000001] = "Sum";
    AggregationType[AggregationType["Average"] = 100000002] = "Average";
    AggregationType[AggregationType["Min"] = 100000003] = "Min";
    AggregationType[AggregationType["Max"] = 100000004] = "Max";
})(AggregationType || (AggregationType = {}));
//# sourceMappingURL=ChartDefinitionTypes.js.map