/**
 * UrlFieldRenderer - Renders a URL input with clickable link
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import {
  Input,
  Link,
  makeStyles,
  shorthands,
} from "@fluentui/react-components";
import { OpenRegular } from "@fluentui/react-icons";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  linkRow: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
  },
});

export interface UrlFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

export const UrlFieldRenderer: React.FC<UrlFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
}) => {
  const styles = useStyles();
  const urlValue = (value as string) ?? "";
  const isDisabled = disabled || config.readOnly;

  const handleChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      onChange(config.name, data.value);
    },
    [config.name, onChange]
  );

  const handleOpenLink = React.useCallback(() => {
    if (urlValue) {
      // Ensure URL has protocol
      const url = urlValue.startsWith("http") ? urlValue : `https://${urlValue}`;
      window.open(url, "_blank", "noopener,noreferrer");
    }
  }, [urlValue]);

  return (
    <div className={styles.container}>
      <Input
        value={urlValue}
        onChange={handleChange}
        disabled={isDisabled}
        placeholder=""
        aria-label={config.label}
        appearance="underline"
        type="url"
      />
      {urlValue && (
        <div className={styles.linkRow}>
          <Link onClick={handleOpenLink} aria-label={`Open ${config.label}`}>
            <OpenRegular style={{ fontSize: "12px", marginRight: "4px" }} />
            Open link
          </Link>
        </div>
      )}
    </div>
  );
};
