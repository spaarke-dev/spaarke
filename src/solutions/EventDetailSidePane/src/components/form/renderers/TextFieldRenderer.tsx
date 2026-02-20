/**
 * TextFieldRenderer - Renders a single-line text input field
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { Input } from "@fluentui/react-components";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";

export interface TextFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

export const TextFieldRenderer: React.FC<TextFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
}) => {
  const handleChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      onChange(config.name, data.value);
    },
    [config.name, onChange]
  );

  return (
    <Input
      value={(value as string) ?? ""}
      onChange={handleChange}
      disabled={disabled || config.readOnly}
      placeholder={`Enter ${config.label.toLowerCase()}...`}
      aria-label={config.label}
      appearance="underline"
    />
  );
};
