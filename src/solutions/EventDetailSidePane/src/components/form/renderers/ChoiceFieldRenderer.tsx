/**
 * ChoiceFieldRenderer - Renders a dropdown/optionset field
 *
 * Options are fetched from Dataverse metadata via useFieldMetadata,
 * not stored in the JSON config. This keeps the JSON clean and
 * ensures a single source of truth for optionset values.
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { Dropdown, Option, Spinner } from "@fluentui/react-components";
import type {
  IFieldConfig,
  IFieldMetadata,
  FieldChangeCallback,
} from "../../../types/FormConfig";

export interface ChoiceFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
  metadata?: IFieldMetadata;
}

export const ChoiceFieldRenderer: React.FC<ChoiceFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
  metadata,
}) => {
  const options = metadata?.options ?? [];
  const numericValue = typeof value === "number" ? value : null;

  // Find the selected option label for display
  const selectedLabel = React.useMemo(() => {
    if (numericValue === null) return "";
    const option = options.find((o) => o.value === numericValue);
    return option?.label ?? "";
  }, [numericValue, options]);

  const handleChange = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      if (data.optionValue) {
        const parsed = parseInt(data.optionValue, 10);
        if (!isNaN(parsed)) {
          onChange(config.name, parsed);
        }
      }
    },
    [config.name, onChange]
  );

  // Loading state while metadata is being fetched
  if (!metadata && options.length === 0) {
    return <Spinner size="tiny" label="Loading options..." />;
  }

  return (
    <Dropdown
      value={selectedLabel}
      selectedOptions={numericValue !== null ? [String(numericValue)] : []}
      onOptionSelect={handleChange}
      disabled={disabled || config.readOnly}
      placeholder={`Select ${config.label.toLowerCase()}...`}
      aria-label={config.label}
      style={{ width: "100%" }}
    >
      {options.map((option) => (
        <Option key={option.value} value={String(option.value)}>
          {option.label}
        </Option>
      ))}
    </Dropdown>
  );
};
