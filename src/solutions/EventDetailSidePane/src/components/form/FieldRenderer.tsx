/**
 * FieldRenderer - Dispatches to type-specific field renderer components
 *
 * Wraps each field in a Fluent UI Field component for consistent
 * label rendering and required indicator.
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { Field, makeStyles, shorthands, tokens } from "@fluentui/react-components";
import type {
  IFieldConfig,
  IFieldMetadata,
  FieldChangeCallback,
} from "../../types/FormConfig";
import { TextFieldRenderer } from "./renderers/TextFieldRenderer";
import { MultilineFieldRenderer } from "./renderers/MultilineFieldRenderer";
import { DateFieldRenderer } from "./renderers/DateFieldRenderer";
import { DateTimeFieldRenderer } from "./renderers/DateTimeFieldRenderer";
import { ChoiceFieldRenderer } from "./renderers/ChoiceFieldRenderer";
import { LookupFieldRenderer } from "./renderers/LookupFieldRenderer";
import { UrlFieldRenderer } from "./renderers/UrlFieldRenderer";

const useStyles = makeStyles({
  fieldLabel: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
});

export interface FieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
  metadata?: IFieldMetadata;
}

export const FieldRenderer: React.FC<FieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
  metadata,
}) => {
  const styles = useStyles();

  const fieldContent = React.useMemo(() => {
    const props = { config, value, onChange, disabled };

    switch (config.type) {
      case "text":
        return <TextFieldRenderer {...props} />;
      case "multiline":
        return <MultilineFieldRenderer {...props} />;
      case "date":
        return <DateFieldRenderer {...props} />;
      case "datetime":
        return <DateTimeFieldRenderer {...props} />;
      case "choice":
        return <ChoiceFieldRenderer {...props} metadata={metadata} />;
      case "lookup":
        return <LookupFieldRenderer {...props} />;
      case "url":
        return <UrlFieldRenderer {...props} />;
      default:
        // Unknown type — render as text (safe fallback)
        return <TextFieldRenderer {...props} />;
    }
  }, [config, value, onChange, disabled, metadata]);

  return (
    <Field
      label={
        <span className={styles.fieldLabel}>
          {config.label}
        </span>
      }
      required={config.required}
    >
      {fieldContent}
    </Field>
  );
};
