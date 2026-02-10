/**
 * DescriptionSection - Collapsible description section for Event Detail Side Pane
 *
 * Displays a multiline text editor for event description/notes.
 * Uses Fluent UI v9 Textarea component with proper theming support.
 *
 * Features:
 * - Collapsible section wrapper
 * - Multiline text editing
 * - Auto-resize or fixed height with scroll
 * - Character count indicator (optional)
 *
 * @see projects/events-workspace-apps-UX-r1/design.md - Description section spec
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Textarea,
  Text,
  Spinner,
} from "@fluentui/react-components";
import { TextDescriptionRegular } from "@fluentui/react-icons";
import { CollapsibleSection } from "./CollapsibleSection";

// -----------------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------------

/**
 * Props for the DescriptionSection component
 */
export interface DescriptionSectionProps {
  /** Description text value */
  value: string | null;
  /** Callback when description changes */
  onChange: (value: string) => void;
  /** Whether the section starts expanded (default: false per design.md) */
  defaultExpanded?: boolean;
  /** Controlled expanded state */
  expanded?: boolean;
  /** Callback when expanded state changes */
  onExpandedChange?: (expanded: boolean) => void;
  /** Whether the field is disabled (read-only mode) */
  disabled?: boolean;
  /** Loading state */
  isLoading?: boolean;
  /** Placeholder text for empty state */
  placeholder?: string;
  /** Maximum character limit (optional) */
  maxLength?: number;
  /** Whether to show character count */
  showCharCount?: boolean;
  /** Minimum number of rows for the textarea */
  minRows?: number;
  /** Maximum number of rows for auto-resize (null for unlimited) */
  maxRows?: number;
}

// -----------------------------------------------------------------------------
// Constants
// -----------------------------------------------------------------------------

/** Default placeholder text */
const DEFAULT_PLACEHOLDER = "Enter event description or notes...";

/** Default minimum rows for textarea */
const DEFAULT_MIN_ROWS = 3;

/** Default maximum rows for auto-resize */
const DEFAULT_MAX_ROWS = 10;

// -----------------------------------------------------------------------------
// Styles
// -----------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  textarea: {
    width: "100%",
    minHeight: "80px",
    resize: "vertical",
    "& textarea": {
      fontFamily: tokens.fontFamilyBase,
      fontSize: tokens.fontSizeBase300,
      lineHeight: tokens.lineHeightBase300,
    },
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  charCount: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  charCountWarning: {
    color: tokens.colorPaletteYellowForeground2,
  },
  charCountError: {
    color: tokens.colorPaletteRedForeground1,
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
});

// -----------------------------------------------------------------------------
// Component
// -----------------------------------------------------------------------------

/**
 * DescriptionSection component for displaying and editing event description.
 *
 * Rendered as a collapsible section that can be expanded/collapsed.
 * Contains a multiline Textarea field for entering event notes/description.
 *
 * Uses Fluent UI v9 Textarea with proper dark mode support via tokens.
 * Supports auto-resize based on content with configurable min/max rows.
 *
 * @example Basic usage
 * ```tsx
 * <DescriptionSection
 *   value={event.sprk_description}
 *   onChange={handleDescriptionChange}
 *   defaultExpanded={true}
 *   disabled={!canEdit}
 * />
 * ```
 *
 * @example With character limit
 * ```tsx
 * <DescriptionSection
 *   value={event.sprk_description}
 *   onChange={handleDescriptionChange}
 *   maxLength={4000}
 *   showCharCount
 * />
 * ```
 */
export const DescriptionSection: React.FC<DescriptionSectionProps> = ({
  value,
  onChange,
  defaultExpanded = false,
  expanded,
  onExpandedChange,
  disabled = false,
  isLoading = false,
  placeholder = DEFAULT_PLACEHOLDER,
  maxLength,
  showCharCount = false,
  minRows = DEFAULT_MIN_ROWS,
  maxRows = DEFAULT_MAX_ROWS,
}) => {
  const styles = useStyles();

  // Track local value for controlled textarea
  const [localValue, setLocalValue] = React.useState<string>(value ?? "");

  // Sync local value when prop changes
  React.useEffect(() => {
    setLocalValue(value ?? "");
  }, [value]);

  // ---------------------------------------------------------------------------
  // Computed Values
  // ---------------------------------------------------------------------------

  const charCount = localValue.length;
  const isNearLimit = maxLength && charCount > maxLength * 0.9;
  const isAtLimit = maxLength && charCount >= maxLength;

  // Determine character count styling
  const charCountClassName = React.useMemo(() => {
    if (isAtLimit) return `${styles.charCount} ${styles.charCountError}`;
    if (isNearLimit) return `${styles.charCount} ${styles.charCountWarning}`;
    return styles.charCount;
  }, [styles, isAtLimit, isNearLimit]);

  // ---------------------------------------------------------------------------
  // Event Handlers
  // ---------------------------------------------------------------------------

  /**
   * Handle textarea value change
   */
  const handleChange = React.useCallback(
    (
      _event: React.ChangeEvent<HTMLTextAreaElement>,
      data: { value: string }
    ) => {
      const newValue = data.value;

      // Enforce maxLength if specified
      if (maxLength && newValue.length > maxLength) {
        return;
      }

      setLocalValue(newValue);
      onChange(newValue);
    },
    [onChange, maxLength]
  );

  /**
   * Handle blur event - trim whitespace if needed
   */
  const handleBlur = React.useCallback(() => {
    const trimmed = localValue.trim();
    if (trimmed !== localValue) {
      setLocalValue(trimmed);
      onChange(trimmed);
    }
  }, [localValue, onChange]);

  // ---------------------------------------------------------------------------
  // Render Loading State
  // ---------------------------------------------------------------------------

  if (isLoading) {
    return (
      <CollapsibleSection
        title="Description"
        icon={<TextDescriptionRegular />}
        defaultExpanded={defaultExpanded}
        expanded={expanded}
        onExpandedChange={onExpandedChange}
        disabled
      >
        <div className={styles.loadingContainer}>
          <Spinner size="small" label="Loading description..." />
        </div>
      </CollapsibleSection>
    );
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <CollapsibleSection
      title="Description"
      icon={<TextDescriptionRegular />}
      defaultExpanded={defaultExpanded}
      expanded={expanded}
      onExpandedChange={onExpandedChange}
      disabled={disabled}
      ariaLabel="Description section"
    >
      <div className={styles.container}>
        {/* Multiline Textarea */}
        <Textarea
          className={styles.textarea}
          value={localValue}
          onChange={handleChange}
          onBlur={handleBlur}
          placeholder={placeholder}
          disabled={disabled}
          resize="vertical"
          aria-label="Event description"
          // Note: Fluent UI Textarea does not have native minRows/maxRows
          // Using style-based approach for sizing
          style={{
            minHeight: `${minRows * 24}px`,
            maxHeight: maxRows ? `${maxRows * 24}px` : undefined,
          }}
        />

        {/* Character count footer (optional) */}
        {(showCharCount || maxLength) && (
          <div className={styles.footer}>
            <Text className={charCountClassName}>
              {maxLength
                ? `${charCount.toLocaleString()} / ${maxLength.toLocaleString()}`
                : `${charCount.toLocaleString()} characters`}
            </Text>
          </div>
        )}
      </div>
    </CollapsibleSection>
  );
};

export default DescriptionSection;
