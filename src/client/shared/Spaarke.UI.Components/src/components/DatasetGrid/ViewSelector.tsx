/**
 * ViewSelector Component
 *
 * Fluent UI v9 dropdown for selecting views from savedquery and custom configurations.
 * Styled to match OOB Power Apps view selector.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */

import * as React from "react";
import {
  Dropdown,
  Option,
  OptionGroup,
  Spinner,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import type { DropdownProps } from "@fluentui/react-components";
import type { IViewDefinition, ViewType } from "../../types/FetchXmlTypes";
import { ViewService, IGetViewsOptions } from "../../services/ViewService";
import type { XrmContext } from "../../utils/xrmContext";

/**
 * Props for ViewSelector component
 */
export interface IViewSelectorProps {
  /** Xrm context for ViewService */
  xrm: XrmContext;
  /** Entity logical name to fetch views for */
  entityLogicalName: string;
  /** Currently selected view ID */
  selectedViewId?: string;
  /** Default view name to show when loading */
  defaultViewName?: string;
  /** Callback when view selection changes */
  onViewChange?: (view: IViewDefinition) => void;
  /** Include custom views from sprk_gridconfiguration */
  includeCustomViews?: boolean;
  /** Include personal views from userquery */
  includePersonalViews?: boolean;
  /** Group views by type in dropdown */
  groupByType?: boolean;
  /** Additional CSS class */
  className?: string;
  /** Compact mode (smaller size) */
  compact?: boolean;
  /** Disabled state */
  disabled?: boolean;
  /** Placeholder text */
  placeholder?: string;
}

const useStyles = makeStyles({
  selector: {
    minWidth: "200px",
  },
  selectorCompact: {
    minWidth: "160px",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    minWidth: "200px",
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    height: "32px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
  },
  loadingText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  optionIcon: {
    marginRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * Group labels for view types
 */
const VIEW_TYPE_LABELS: Record<ViewType, string> = {
  savedquery: "System Views",
  userquery: "Personal Views",
  custom: "Custom Views",
};

/**
 * ViewSelector - Dropdown for selecting entity views
 */
export const ViewSelector: React.FC<IViewSelectorProps> = ({
  xrm,
  entityLogicalName,
  selectedViewId,
  defaultViewName,
  onViewChange,
  includeCustomViews = false,
  includePersonalViews = false,
  groupByType = false,
  className,
  compact = false,
  disabled = false,
  placeholder = "Select a view",
}) => {
  const styles = useStyles();
  const [views, setViews] = React.useState<IViewDefinition[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  // Create ViewService instance
  const viewService = React.useMemo(() => new ViewService(xrm), [xrm]);

  // Load views on mount or when entity changes
  React.useEffect(() => {
    let mounted = true;

    const loadViews = async () => {
      setLoading(true);
      setError(null);

      try {
        const options: IGetViewsOptions = {
          includeCustom: includeCustomViews,
          includePersonal: includePersonalViews,
        };

        const fetchedViews = await viewService.getViews(entityLogicalName, options);

        if (mounted) {
          setViews(fetchedViews);
          setLoading(false);

          // Auto-select default view if no selection
          if (!selectedViewId && fetchedViews.length > 0) {
            const defaultView = fetchedViews.find((v) => v.isDefault) || fetchedViews[0];
            if (onViewChange) {
              onViewChange(defaultView);
            }
          }
        }
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : "Failed to load views");
          setLoading(false);
        }
      }
    };

    loadViews();

    return () => {
      mounted = false;
    };
  }, [entityLogicalName, includeCustomViews, includePersonalViews, viewService]);

  // Handle selection change
  const handleSelectionChange: DropdownProps["onOptionSelect"] = React.useCallback(
    (_event, data) => {
      const viewId = data.optionValue;
      if (viewId && onViewChange) {
        const selectedView = views.find((v) => v.id === viewId);
        if (selectedView) {
          onViewChange(selectedView);
        }
      }
    },
    [views, onViewChange]
  );

  // Get selected view name for display
  const selectedViewName = React.useMemo(() => {
    if (selectedViewId) {
      const view = views.find((v) => v.id === selectedViewId);
      return view?.name;
    }
    return defaultViewName;
  }, [selectedViewId, views, defaultViewName]);

  // Group views by type
  const groupedViews = React.useMemo(() => {
    if (!groupByType) {
      return { ungrouped: views };
    }

    const groups: Partial<Record<ViewType, IViewDefinition[]>> = {};
    for (const view of views) {
      const type = view.viewType;
      if (!groups[type]) {
        groups[type] = [];
      }
      groups[type]!.push(view);
    }
    return groups;
  }, [views, groupByType]);

  // Loading state
  if (loading) {
    return (
      <div className={mergeClasses(styles.loadingContainer, className)}>
        <Spinner size="tiny" />
        <span className={styles.loadingText}>
          {defaultViewName || "Loading views..."}
        </span>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={mergeClasses(styles.loadingContainer, className)}>
        <span className={styles.errorText}>Error: {error}</span>
      </div>
    );
  }

  // No views available
  if (views.length === 0) {
    return (
      <Dropdown
        className={mergeClasses(
          styles.selector,
          compact && styles.selectorCompact,
          className
        )}
        disabled
        placeholder="No views available"
        size={compact ? "small" : "medium"}
      />
    );
  }

  // Render options
  const renderOptions = () => {
    if (groupByType && Object.keys(groupedViews).length > 1) {
      // Render grouped
      return (
        <>
          {(["savedquery", "custom", "userquery"] as ViewType[]).map((type) => {
            const typeViews = groupedViews[type];
            if (!typeViews || typeViews.length === 0) return null;

            return (
              <OptionGroup key={type} label={VIEW_TYPE_LABELS[type]}>
                {typeViews.map((view) => (
                  <Option key={view.id} value={view.id}>
                    {view.name}
                  </Option>
                ))}
              </OptionGroup>
            );
          })}
        </>
      );
    }

    // Render flat list
    return views.map((view) => (
      <Option key={view.id} value={view.id}>
        {view.name}
      </Option>
    ));
  };

  return (
    <Dropdown
      className={mergeClasses(
        styles.selector,
        compact && styles.selectorCompact,
        className
      )}
      value={selectedViewName}
      selectedOptions={selectedViewId ? [selectedViewId] : []}
      onOptionSelect={handleSelectionChange}
      disabled={disabled}
      placeholder={placeholder}
      size={compact ? "small" : "medium"}
      aria-label={`Select view for ${entityLogicalName}`}
    >
      {renderOptions()}
    </Dropdown>
  );
};

// Default export for convenience
export default ViewSelector;
