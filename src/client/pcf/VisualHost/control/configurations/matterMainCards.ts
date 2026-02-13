/**
 * @deprecated v1.2.33 — Static configurations replaced by Dataverse chart definition records.
 * Use the enhanced MetricCard visual type with ICardConfig (sprk_valueformat, sprk_colorsource,
 * sprk_configurationjson) instead of hardcoded configs. See metriccard-enhancement-design.md.
 *
 * Matter Main Form - Grade Metric Card Configurations (DEPRECATED)
 *
 * Defines chart definition objects for the three performance area
 * GradeMetricCard instances displayed on the Matter main form:
 *   1. Guidelines  (sprk_guidelinecompliancegrade_current)
 *   2. Budget      (sprk_budgetcompliancegrade_current)
 *   3. Outcomes    (sprk_outcomecompliancegrade_current)
 *
 * Each configuration is a complete IChartDefinition record that mirrors
 * what will be stored in the sprk_chartdefinition Dataverse table.
 * The sprk_chartdefinitionid values are placeholders and will be replaced
 * by actual Dataverse record GUIDs at deployment time.
 */

import {
  VisualType,
  AggregationType,
  type IChartDefinition,
} from "../types";

// ---------------------------------------------------------------------------
// Configuration JSON payloads
// ---------------------------------------------------------------------------

/**
 * Guidelines card configuration.
 * Includes explicit colorRules to define grade-to-color mapping:
 *   - Blue  (0.85 - 1.00) : A+, A, A-, B+, B, B-
 *   - Yellow (0.70 - 0.84) : C+, C, C-
 *   - Red   (0.00 - 0.69) : D+, D, D-, F
 */
const GUIDELINES_CONFIG_JSON = JSON.stringify({
  icon: "guidelines",
  contextTemplate: "You have a {grade}% in {area} compliance",
  colorRules: [
    { range: [0.85, 1.00], color: "blue" },
    { range: [0.70, 0.84], color: "yellow" },
    { range: [0.00, 0.69], color: "red" },
  ],
});

/**
 * Budget card configuration.
 * Omits colorRules to use the GradeMetricCard defaults (same blue/yellow/red
 * thresholds as Guidelines).
 */
const BUDGET_CONFIG_JSON = JSON.stringify({
  icon: "budget",
  contextTemplate: "You have a {grade}% in {area} compliance",
});

/**
 * Outcomes card configuration.
 * Omits colorRules to use the GradeMetricCard defaults (same blue/yellow/red
 * thresholds as Guidelines).
 */
const OUTCOMES_CONFIG_JSON = JSON.stringify({
  icon: "outcomes",
  contextTemplate: "You have a {grade}% in {area} compliance",
});

// ---------------------------------------------------------------------------
// Chart Definition Objects
// ---------------------------------------------------------------------------

/**
 * Guidelines Grade Metric Card
 *
 * Displays the current guideline compliance grade for a matter.
 * Bound to `sprk_guidelinecompliancegrade_current` and aggregated as Average.
 * Color rules: Blue (85-100%), Yellow (70-84%), Red (0-69%).
 */
export const GUIDELINES_CARD_CONFIG: IChartDefinition = {
  sprk_chartdefinitionid: "00000000-0000-0000-0000-000000000030",
  sprk_name: "Guidelines",
  sprk_description:
    "Displays guideline compliance grade for the current matter",
  sprk_visualtype: VisualType.ReportCardMetric,
  sprk_entitylogicalname: "sprk_matter",
  sprk_aggregationfield: "sprk_guidelinecompliancegrade_current",
  sprk_aggregationtype: AggregationType.Average,
  sprk_configurationjson: GUIDELINES_CONFIG_JSON,
};

/**
 * Budget Grade Metric Card
 *
 * Displays the current budget compliance grade for a matter.
 * Bound to `sprk_budgetcompliancegrade_current` and aggregated as Average.
 * Uses default color rules (same thresholds as Guidelines).
 */
export const BUDGET_CARD_CONFIG: IChartDefinition = {
  sprk_chartdefinitionid: "00000000-0000-0000-0000-000000000031",
  sprk_name: "Budget",
  sprk_description:
    "Displays budget compliance grade for the current matter",
  sprk_visualtype: VisualType.ReportCardMetric,
  sprk_entitylogicalname: "sprk_matter",
  sprk_aggregationfield: "sprk_budgetcompliancegrade_current",
  sprk_aggregationtype: AggregationType.Average,
  sprk_configurationjson: BUDGET_CONFIG_JSON,
};

/**
 * Outcomes Grade Metric Card
 *
 * Displays the current outcome compliance grade for a matter.
 * Bound to `sprk_outcomecompliancegrade_current` and aggregated as Average.
 * Uses default color rules (same thresholds as Guidelines).
 */
export const OUTCOMES_CARD_CONFIG: IChartDefinition = {
  sprk_chartdefinitionid: "00000000-0000-0000-0000-000000000032",
  sprk_name: "Outcomes",
  sprk_description:
    "Displays outcome compliance grade for the current matter",
  sprk_visualtype: VisualType.ReportCardMetric,
  sprk_entitylogicalname: "sprk_matter",
  sprk_aggregationfield: "sprk_outcomecompliancegrade_current",
  sprk_aggregationtype: AggregationType.Average,
  sprk_configurationjson: OUTCOMES_CONFIG_JSON,
};

// ---------------------------------------------------------------------------
// Convenience array
// ---------------------------------------------------------------------------

/**
 * All three grade metric card definitions for the matter main form.
 * Use this array to register all cards at once during VisualHost initialization.
 */
export const MATTER_MAIN_GRADE_CARDS: IChartDefinition[] = [
  GUIDELINES_CARD_CONFIG,
  BUDGET_CARD_CONFIG,
  OUTCOMES_CARD_CONFIG,
];
