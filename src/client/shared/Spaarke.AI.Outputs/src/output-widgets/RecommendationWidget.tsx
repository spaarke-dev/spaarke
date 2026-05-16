/**
 * RecommendationWidget
 *
 * Renders a ranked list of AI recommendations in the output pane. Each
 * recommendation is displayed as a Fluent v9 Card with:
 *   - A priority Badge (high=danger, medium=warning, low=informative)
 *   - The recommendation text
 *   - An optional rationale in a smaller Text element
 *   - An optional "Apply" action button (only shown when onApply prop is set)
 *
 * The "Apply" button calls the optional onApply?: (id: string) => void prop.
 * No side effects (API calls, state mutation) occur inside the widget itself.
 *
 * All colors and spacing use Fluent v9 design tokens (ADR-021). Dark mode is
 * supported automatically via FluentProvider theme switching.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Badge,
  Button,
  Card,
  CardHeader,
  Spinner,
} from "@fluentui/react-components";
import type { OutputWidgetProps } from "../types";

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

/** Priority level for an individual recommendation. */
export type RecommendationPriority = "high" | "medium" | "low";

/** A single AI recommendation entry. */
export interface Recommendation {
  /** Unique identifier for this recommendation. */
  id: string;
  /** Priority level — affects badge appearance. */
  priority: RecommendationPriority;
  /** The main recommendation text. */
  text: string;
  /** Optional supporting rationale shown below the main text. */
  rationale?: string;
}

export interface RecommendationData {
  /** Ordered list of recommendations (highest priority first by convention). */
  recommendations: Recommendation[];
}

export type RecommendationWidgetProps = OutputWidgetProps<RecommendationData> & {
  /**
   * Optional callback invoked when the user clicks "Apply" on a recommendation.
   * When not provided, the Apply button is hidden entirely.
   *
   * @param id - The id of the recommendation the user applied.
   */
  onApply?: (id: string) => void;
};

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  cardList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  card: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalM,
  },
  cardHeaderRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  recommendationText: {
    fontWeight: tokens.fontWeightSemibold,
    flex: "1",
  },
  rationale: {
    color: tokens.colorNeutralForeground2,
  },
  actionRow: {
    display: "flex",
    justifyContent: "flex-end",
    marginTop: tokens.spacingVerticalXS,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Map a RecommendationPriority to the Badge appearance value expected by
 * Fluent v9 Badge. Uses the color prop values defined in the component API.
 */
function priorityToBadgeColor(
  priority: RecommendationPriority
): "danger" | "warning" | "informative" {
  switch (priority) {
    case "high":
      return "danger";
    case "medium":
      return "warning";
    case "low":
    default:
      return "informative";
  }
}

function priorityLabel(priority: RecommendationPriority): string {
  switch (priority) {
    case "high":
      return "High";
    case "medium":
      return "Medium";
    case "low":
    default:
      return "Low";
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * RecommendationWidget renders AI recommendations as styled Fluent v9 Cards.
 * Each card shows a priority badge, recommendation text, optional rationale,
 * and an Apply button (only visible when the onApply prop is provided).
 */
export default function RecommendationWidget({
  data,
  isLoading,
  error,
  className,
  onApply,
}: RecommendationWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading recommendations..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.cardList}>
        {data.recommendations.map((rec) => (
          <Card key={rec.id} className={styles.card}>
            <CardHeader
              header={
                <div className={styles.cardHeaderRow}>
                  <Badge
                    color={priorityToBadgeColor(rec.priority)}
                    appearance="filled"
                    size="small"
                    aria-label={`Priority: ${priorityLabel(rec.priority)}`}
                  >
                    {priorityLabel(rec.priority)}
                  </Badge>
                  <Text size={300} className={styles.recommendationText}>
                    {rec.text}
                  </Text>
                </div>
              }
            />

            {rec.rationale && (
              <Text size={200} className={styles.rationale}>
                {rec.rationale}
              </Text>
            )}

            {onApply && (
              <div className={styles.actionRow}>
                <Button
                  appearance="primary"
                  size="small"
                  onClick={() => onApply(rec.id)}
                >
                  Apply
                </Button>
              </div>
            )}
          </Card>
        ))}
      </div>
    </div>
  );
}
