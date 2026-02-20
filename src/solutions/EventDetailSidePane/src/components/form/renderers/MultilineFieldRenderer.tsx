/**
 * MultilineFieldRenderer - Renders a multi-line textarea field
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { Textarea } from "@fluentui/react-components";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";

export interface MultilineFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

export const MultilineFieldRenderer: React.FC<MultilineFieldRendererProps> = ({
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
    <Textarea
      value={(value as string) ?? ""}
      onChange={handleChange}
      disabled={disabled || config.readOnly}
      placeholder=""
      aria-label={config.label}
      resize="vertical"
      rows={3}
      appearance="outline"
    />
  );
};
