/**
 * briefingNarrative.ts
 *
 * Deterministic template-based narrative generation for the portfolio briefing dialog.
 *
 * Generates structured narrative sections from portfolio metrics without any AI calls.
 * Used as the primary content source when the BFF /api/workspace/briefing endpoint is
 * unavailable, times out, or returns IsAiEnhanced=false.
 *
 * All sections correspond 1-to-1 with the BriefingDialog narrative paragraphs.
 */

/** Raw metrics needed to build a deterministic briefing. Matches BriefingResponse fields. */
export interface IBriefingMetrics {
  activeMatters: number;
  practiceAreaCount?: number;
  mattersAtRisk: number;
  overdueEvents: number;
  topPriorityMatterName?: string;
  topPriorityDeadline?: string | null;
  utilizationPercent: number;
  mattersExceedingThreshold?: number;
  /** Threshold percentage used to flag high-utilization matters (default: 85). */
  highUtilizationThreshold?: number;
}

/** A single rendered narrative section. */
export interface INarrativeSection {
  /** Section key — used as React key and for aria-labelling. */
  key: string;
  /** Section heading label (short). */
  label: string;
  /** Full narrative paragraph text. */
  text: string;
  /**
   * When true, the text contains risk/overdue language and should be
   * rendered with colorPaletteRedForeground1 for the relevant portion.
   * BriefingDialog handles the actual colour application.
   */
  isDanger: boolean;
}

/** All narrative sections for the briefing dialog. */
export interface IBriefingNarrative {
  sections: INarrativeSection[];
  /** One-line summary sentence (shown at the top of the dialog). */
  headline: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a deadline string (ISO date or date-only) into a human-readable form:
 *   "2026-03-15"  →  "March 15, 2026"
 *   "2026-03-15T00:00:00Z"  →  "March 15, 2026"
 * Returns the original string on parse failure.
 */
function formatDeadline(deadline: string): string {
  try {
    const d = new Date(deadline);
    if (isNaN(d.getTime())) return deadline;
    return d.toLocaleDateString("en-US", {
      month: "long",
      day: "numeric",
      year: "numeric",
    });
  } catch {
    return deadline;
  }
}

/**
 * Pluralise a word: pluralise(1, "matter") → "matter"; pluralise(3, "matter") → "matters"
 */
function pluralise(count: number, singular: string, plural?: string): string {
  return count === 1 ? singular : plural ?? `${singular}s`;
}

// ---------------------------------------------------------------------------
// Section generators
// ---------------------------------------------------------------------------

function buildActiveMattersSectionText(metrics: IBriefingMetrics): string {
  const { activeMatters, practiceAreaCount } = metrics;

  if (activeMatters === 0) {
    return "You currently have no active matters in your portfolio.";
  }

  if (practiceAreaCount != null && practiceAreaCount > 0) {
    return (
      `You currently manage ${activeMatters} active ` +
      `${pluralise(activeMatters, "matter")} across ` +
      `${practiceAreaCount} ${pluralise(practiceAreaCount, "practice area")}.`
    );
  }

  return (
    `You currently manage ${activeMatters} active ` +
    `${pluralise(activeMatters, "matter")} in your portfolio.`
  );
}

function buildAtRiskSectionText(metrics: IBriefingMetrics): string {
  const { mattersAtRisk, activeMatters } = metrics;

  if (mattersAtRisk === 0) {
    return "No matters are currently flagged as at risk. All matters are within budget and on schedule.";
  }

  const proportion =
    activeMatters > 0
      ? ` (${Math.round((mattersAtRisk / activeMatters) * 100)}% of active portfolio)`
      : "";

  return (
    `${mattersAtRisk} ${pluralise(mattersAtRisk, "matter is", "matters are")} ` +
    `currently at risk due to budget overruns or overdue events${proportion}. ` +
    `Immediate review is recommended to prevent further escalation.`
  );
}

function buildOverdueSectionText(metrics: IBriefingMetrics): string {
  const { overdueEvents } = metrics;

  if (overdueEvents === 0) {
    return "There are no overdue events. All scheduled activities are current.";
  }

  return (
    `${overdueEvents} overdue ${pluralise(overdueEvents, "event")} ` +
    `${pluralise(overdueEvents, "requires", "require")} attention. ` +
    `These ${pluralise(overdueEvents, "event")} ${pluralise(overdueEvents, "has", "have")} ` +
    `passed their scheduled date and should be rescheduled or resolved.`
  );
}

function buildTopPrioritySectionText(metrics: IBriefingMetrics): string {
  const { topPriorityMatterName, topPriorityDeadline } = metrics;

  if (!topPriorityMatterName) {
    return "No priority matters have been identified at this time.";
  }

  if (topPriorityDeadline) {
    const deadlineFormatted = formatDeadline(topPriorityDeadline);
    return (
      `Top priority matter: "${topPriorityMatterName}" ` +
      `with a key deadline of ${deadlineFormatted}. ` +
      `This matter has been identified as requiring the most immediate attention based on priority scoring.`
    );
  }

  return (
    `Top priority matter: "${topPriorityMatterName}". ` +
    `This matter has been identified as requiring the most immediate attention based on priority scoring.`
  );
}

function buildBudgetSectionText(metrics: IBriefingMetrics): string {
  const {
    utilizationPercent,
    mattersExceedingThreshold,
    highUtilizationThreshold = 85,
  } = metrics;

  const utilFormatted = Math.round(utilizationPercent);

  if (mattersExceedingThreshold != null && mattersExceedingThreshold > 0) {
    return (
      `Portfolio budget utilization is at ${utilFormatted}% overall. ` +
      `${mattersExceedingThreshold} ${pluralise(mattersExceedingThreshold, "matter")} ` +
      `${pluralise(mattersExceedingThreshold, "is", "are")} exceeding ` +
      `${highUtilizationThreshold}% utilization and should be reviewed for budget adjustments.`
    );
  }

  if (utilFormatted >= highUtilizationThreshold) {
    return (
      `Portfolio budget utilization is at ${utilFormatted}% — approaching budget limits. ` +
      `Consider reviewing spend patterns across active matters to avoid overruns.`
    );
  }

  return (
    `Portfolio budget utilization is at ${utilFormatted}%. ` +
    `Spending is within normal parameters across your active matters.`
  );
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Generate a deterministic narrative from portfolio metrics.
 *
 * Returns five narrative sections and a one-line headline. The isDanger flag
 * is set on sections that describe at-risk or overdue conditions so the
 * BriefingDialog can apply colorPaletteRedForeground1 styling.
 *
 * This function is pure (no side effects) and always returns a complete result
 * regardless of the input values — it is the guaranteed fallback path.
 *
 * @example
 * const narrative = generateDeterministicNarrative({
 *   activeMatters: 12,
 *   practiceAreaCount: 4,
 *   mattersAtRisk: 3,
 *   overdueEvents: 5,
 *   topPriorityMatterName: "Acme Corp Litigation",
 *   topPriorityDeadline: "2026-03-15",
 *   utilizationPercent: 73,
 *   mattersExceedingThreshold: 2,
 * });
 */
export function generateDeterministicNarrative(
  metrics: IBriefingMetrics
): IBriefingNarrative {
  const {
    activeMatters,
    mattersAtRisk,
    overdueEvents,
    utilizationPercent,
  } = metrics;

  // Build headline
  let headline: string;
  if (activeMatters === 0) {
    headline = "Your portfolio has no active matters at this time.";
  } else if (mattersAtRisk > 0 || overdueEvents > 0) {
    headline =
      `Portfolio overview: ${activeMatters} active ` +
      `${pluralise(activeMatters, "matter")}, ` +
      `${mattersAtRisk} at risk, ` +
      `${Math.round(utilizationPercent)}% budget utilization.`;
  } else {
    headline =
      `Portfolio overview: ${activeMatters} active ` +
      `${pluralise(activeMatters, "matter")}, ` +
      `all on track, ` +
      `${Math.round(utilizationPercent)}% budget utilization.`;
  }

  const sections: INarrativeSection[] = [
    {
      key: "active-matters",
      label: "Active Matters",
      text: buildActiveMattersSectionText(metrics),
      isDanger: false,
    },
    {
      key: "at-risk",
      label: "At-Risk Matters",
      text: buildAtRiskSectionText(metrics),
      isDanger: mattersAtRisk > 0,
    },
    {
      key: "overdue-events",
      label: "Overdue Events",
      text: buildOverdueSectionText(metrics),
      isDanger: overdueEvents > 0,
    },
    {
      key: "top-priority",
      label: "Top Priority",
      text: buildTopPrioritySectionText(metrics),
      isDanger: false,
    },
    {
      key: "budget",
      label: "Budget Watch",
      text: buildBudgetSectionText(metrics),
      isDanger: false,
    },
  ];

  return { headline, sections };
}
