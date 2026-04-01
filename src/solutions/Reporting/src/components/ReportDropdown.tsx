/**
 * ReportDropdown.tsx
 * Report selection dropdown for the Reporting Code Page header.
 *
 * Fetches the report catalog via useReportCatalog() (GET /api/reporting/reports),
 * groups reports by category (Financial, Operational, Compliance, Documents, Custom),
 * and renders a Fluent v9 Dropdown with an OptionGroup per category.
 *
 * Auto-selection precedence:
 *   1. URL parameter ?reportId=<id>  (deep link / direct navigation)
 *   2. First report in the catalog (default)
 *
 * On selection change, calls the onReportSelect callback so the parent
 * (App.tsx ReportingShell) can update the embed token fetch via useEmbedToken.
 *
 * @see ADR-021 - Fluent UI v9 only; makeStyles; design tokens; dark mode
 * @see ADR-012 - Use @spaarke/ui-components where available
 */

import * as React from "react";
import {
  Dropdown,
  Option,
  OptionGroup,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import type { DropdownProps } from "@fluentui/react-components";
import { DocumentMultipleRegular } from "@fluentui/react-icons";
import { useReportCatalog } from "../hooks/useReportCatalog";
import type { ReportCatalogItem, ReportCategory, ReportDropdownProps } from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Ordered list of report categories for display grouping.
 * Custom is always last so user-authored reports are separated from templates.
 */
const CATEGORY_ORDER: ReportCategory[] = [
  "Financial",
  "Operational",
  "Compliance",
  "Documents",
  "Custom",
];

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  dropdownWrapper: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    minWidth: "220px",
    maxWidth: "340px",
  },
  dropdown: {
    minWidth: "220px",
    maxWidth: "340px",
  },
  loadingWrapper: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    borderColor: tokens.colorNeutralStroke1,
    borderStyle: "solid",
    borderWidth: "1px",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    minWidth: "220px",
  },
  emptyWrapper: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Helper: read reportId URL parameter for deep-link auto-selection
// ---------------------------------------------------------------------------

function getReportIdFromUrl(): string | null {
  try {
    const params = new URLSearchParams(window.location.search);
    return params.get("reportId");
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Helper: group catalog items by category preserving CATEGORY_ORDER
// ---------------------------------------------------------------------------

function groupByCategory(
  reports: ReportCatalogItem[]
): Map<ReportCategory, ReportCatalogItem[]> {
  const grouped = new Map<ReportCategory, ReportCatalogItem[]>();

  for (const category of CATEGORY_ORDER) {
    const items = reports.filter((r) => r.category === category);
    if (items.length > 0) {
      grouped.set(category, items);
    }
  }

  // Catch any unrecognized categories and place them under "Custom"
  const knownCategories = new Set<string>(CATEGORY_ORDER);
  const unrecognized = reports.filter((r) => !knownCategories.has(r.category));
  if (unrecognized.length > 0) {
    const existing = grouped.get("Custom") ?? [];
    grouped.set("Custom", [...existing, ...unrecognized]);
  }

  return grouped;
}

// ---------------------------------------------------------------------------
// ReportDropdown — presentational component
// ---------------------------------------------------------------------------

/**
 * Presentational dropdown that renders a grouped list of reports.
 * Caller is responsible for fetching the catalog and managing selected state.
 */
export const ReportDropdown: React.FC<ReportDropdownProps> = ({
  reports,
  selectedReportId,
  onReportSelect,
  loading = false,
}) => {
  const styles = useStyles();

  // Build lookup for display value
  const selectedReport = React.useMemo(
    () => (selectedReportId ? reports.find((r) => r.id === selectedReportId) : undefined),
    [reports, selectedReportId]
  );

  // Group catalog by category for OptionGroup rendering
  const grouped = React.useMemo(() => groupByCategory(reports), [reports]);

  // ---------------------------------------------------------------------------
  // Loading state — show inline spinner while catalog is being fetched
  // ---------------------------------------------------------------------------
  if (loading) {
    return (
      <div className={styles.loadingWrapper} role="status" aria-label="Loading reports">
        <Spinner size="tiny" />
        <Text size={200} color="inherit">
          Loading reports…
        </Text>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Empty state — no reports in catalog
  // ---------------------------------------------------------------------------
  if (reports.length === 0) {
    return (
      <div className={styles.emptyWrapper} role="status" aria-label="No reports available">
        <DocumentMultipleRegular className={styles.emptyIcon} aria-hidden="true" />
        <Text size={200}>No reports available</Text>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Dropdown — reports grouped by category with OptionGroup per group
  // ---------------------------------------------------------------------------

  const handleSelect: DropdownProps["onOptionSelect"] = (_ev, data) => {
    if (data.optionValue) {
      onReportSelect(data.optionValue);
    }
  };

  return (
    <div className={styles.dropdownWrapper}>
      <Dropdown
        className={styles.dropdown}
        value={selectedReport?.name ?? "Select a report…"}
        selectedOptions={selectedReportId ? [selectedReportId] : []}
        onOptionSelect={handleSelect}
        aria-label="Select report"
        placeholder="Select a report…"
      >
        {Array.from(grouped.entries()).map(([category, items]) => (
          <OptionGroup key={category} label={category}>
            {items.map((report) => (
              <Option key={report.id} value={report.id}>
                {report.name}
              </Option>
            ))}
          </OptionGroup>
        ))}
      </Dropdown>
    </div>
  );
};

// ---------------------------------------------------------------------------
// ReportDropdownContainer — stateful wrapper that owns catalog fetch
// ---------------------------------------------------------------------------

/**
 * Props for the stateful container that manages catalog fetch and auto-selection.
 */
export interface ReportDropdownContainerProps {
  /** Called when the selected report changes (from auto-selection or user pick). */
  onReportSelect: (report: ReportCatalogItem) => void;
}

/**
 * Stateful container component that:
 *   - Fetches catalog via useReportCatalog() hook on mount
 *   - Auto-selects first report or ?reportId= URL param after catalog loads
 *   - Manages loading / error state
 *   - Renders <ReportDropdown> with resolved props
 */
export const ReportDropdownContainer: React.FC<ReportDropdownContainerProps> = ({
  onReportSelect,
}) => {
  const { reports, loading, error } = useReportCatalog();
  const [selectedReportId, setSelectedReportId] = React.useState<string | null>(null);

  // Auto-select when catalog first loads (or when selection hasn't been set yet)
  React.useEffect(() => {
    if (loading || reports.length === 0 || selectedReportId !== null) {
      return;
    }

    // Prefer ?reportId= URL param for deep-link navigation
    const urlReportId = getReportIdFromUrl();
    const targetReport =
      (urlReportId ? reports.find((r) => r.id === urlReportId) : undefined) ?? reports[0];

    setSelectedReportId(targetReport.id);
    onReportSelect(targetReport);
    // onReportSelect intentionally not in deps — only fire once on first load
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, reports, selectedReportId]);

  const handleSelect = React.useCallback(
    (reportId: string) => {
      const report = reports.find((r) => r.id === reportId);
      if (!report) return;

      setSelectedReportId(reportId);
      onReportSelect(report);
    },
    [reports, onReportSelect]
  );

  if (error) {
    return (
      <Text size={200} style={{ color: tokens.colorStatusDangerForeground1 }}>
        Failed to load reports
      </Text>
    );
  }

  return (
    <ReportDropdown
      reports={reports}
      selectedReportId={selectedReportId}
      onReportSelect={handleSelect}
      loading={loading}
    />
  );
};
