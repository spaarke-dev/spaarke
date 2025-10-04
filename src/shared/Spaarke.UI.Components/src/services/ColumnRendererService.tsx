/**
 * Column renderer service - Type-based cell rendering
 */

import * as React from "react";
import {
  Badge,
  Link,
  tokens,
  Text
} from "@fluentui/react-components";
import {
  CheckmarkCircle20Regular,
  DismissCircle20Regular
} from "@fluentui/react-icons";
import { IDatasetColumn, IDatasetRecord } from "../types/DatasetTypes";
import { ColumnRenderer, DataverseAttributeType } from "../types/ColumnRendererTypes";

/**
 * Column renderer registry
 */
export class ColumnRendererService {

  /**
   * Get appropriate renderer for a column based on its dataType
   */
  static getRenderer(column: IDatasetColumn): ColumnRenderer {
    const dataType = column.dataType;

    // Check for secured fields first
    if (column.isSecured && column.canRead === false) {
      return this.renderSecuredField;
    }

    // Map Dataverse data type to renderer
    switch (dataType) {
      // String types
      case DataverseAttributeType.Email:
        return this.renderEmail;
      case DataverseAttributeType.Phone:
        return this.renderPhone;
      case DataverseAttributeType.Url:
        return this.renderUrl;
      case DataverseAttributeType.SingleLineText:
      case DataverseAttributeType.MultipleLineText:
      case DataverseAttributeType.TickerSymbol:
        return this.renderText;

      // Number types
      case DataverseAttributeType.WholeNumber:
      case DataverseAttributeType.DecimalNumber:
      case DataverseAttributeType.FloatingPoint:
        return this.renderNumber;
      case DataverseAttributeType.Money:
        return this.renderMoney;

      // Date/Time
      case DataverseAttributeType.DateAndTime:
        return this.renderDateTime;
      case DataverseAttributeType.DateOnly:
        return this.renderDateOnly;

      // Choice types
      case DataverseAttributeType.TwoOptions:
        return this.renderTwoOptions;
      case DataverseAttributeType.OptionSet:
        return this.renderOptionSet;
      case DataverseAttributeType.MultiSelectOptionSet:
        return this.renderMultiSelectOptionSet;

      // Lookup
      case DataverseAttributeType.Lookup:
      case DataverseAttributeType.Customer:
      case DataverseAttributeType.Owner:
        return this.renderLookup;

      // Boolean
      case DataverseAttributeType.Boolean:
        return this.renderBoolean;

      default:
        return this.renderText;
    }
  }

  /**
   * Render secured/masked field
   */
  private static renderSecuredField(): React.ReactElement {
    return (
      <Text italic style={{ color: tokens.colorNeutralForeground3 }}>
        ***
      </Text>
    );
  }

  /**
   * Render plain text
   */
  private static renderText(value: any): React.ReactElement | string {
    if (value == null || value === "") {
      return "";
    }
    return <Text>{String(value)}</Text>;
  }

  /**
   * Render email with link
   */
  private static renderEmail(value: any): React.ReactElement | string {
    if (!value) return "";

    return (
      <Link href={`mailto:${value}`} target="_blank">
        {String(value)}
      </Link>
    );
  }

  /**
   * Render phone
   */
  private static renderPhone(value: any): React.ReactElement | string {
    if (!value) return "";

    return (
      <Link href={`tel:${value}`}>
        {String(value)}
      </Link>
    );
  }

  /**
   * Render URL with link
   */
  private static renderUrl(value: any): React.ReactElement | string {
    if (!value) return "";

    // Ensure URL has protocol
    const url = String(value).startsWith("http") ? value : `https://${value}`;

    return (
      <Link href={url} target="_blank">
        {String(value)}
      </Link>
    );
  }

  /**
   * Render number with locale formatting
   */
  private static renderNumber(value: any): React.ReactElement | string {
    if (value == null) return "";

    const num = Number(value);
    if (isNaN(num)) return String(value);

    return <Text>{num.toLocaleString()}</Text>;
  }

  /**
   * Render money with currency symbol
   */
  private static renderMoney(value: any, record: IDatasetRecord, column: IDatasetColumn): React.ReactElement | string {
    if (value == null) return "";

    const num = Number(value);
    if (isNaN(num)) return String(value);

    // Check for currency code in record (e.g., transactioncurrencyid_formatted)
    const currencyField = `${column.name}_currency`;
    const currencyCode = record[currencyField] || "USD";

    const formatted = num.toLocaleString(undefined, {
      style: "currency",
      currency: currencyCode
    });

    return <Text>{formatted}</Text>;
  }

  /**
   * Render date and time
   */
  private static renderDateTime(value: any): React.ReactElement | string {
    if (!value) return "";

    const date = new Date(value);
    if (isNaN(date.getTime())) return String(value);

    const formatted = date.toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit"
    });

    return <Text>{formatted}</Text>;
  }

  /**
   * Render date only
   */
  private static renderDateOnly(value: any): React.ReactElement | string {
    if (!value) return "";

    const date = new Date(value);
    if (isNaN(date.getTime())) return String(value);

    const formatted = date.toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric"
    });

    return <Text>{formatted}</Text>;
  }

  /**
   * Render two options (boolean) with icons
   */
  private static renderTwoOptions(value: any): React.ReactElement | string {
    if (value == null) return "";

    const isTrue = value === true || value === 1 || value === "1" || value === "true";

    return isTrue ? (
      <CheckmarkCircle20Regular style={{ color: tokens.colorPaletteGreenForeground1 }} />
    ) : (
      <DismissCircle20Regular style={{ color: tokens.colorNeutralForeground3 }} />
    );
  }

  /**
   * Render option set (choice) with badge
   */
  private static renderOptionSet(value: any, record: IDatasetRecord, column: IDatasetColumn): React.ReactElement | string {
    if (value == null) return "";

    // Use formatted value if available (e.g., "columnname@OData.Community.Display.V1.FormattedValue")
    const formattedValue = record[`${column.name}@OData.Community.Display.V1.FormattedValue`] || String(value);

    return (
      <Badge appearance="outline" color="informative">
        {formattedValue}
      </Badge>
    );
  }

  /**
   * Render multi-select option set with multiple badges
   */
  private static renderMultiSelectOptionSet(value: any, record: IDatasetRecord, column: IDatasetColumn): React.ReactElement | string {
    if (value == null) return "";

    // Multi-select values come as comma-separated string or array
    const formattedValue = record[`${column.name}@OData.Community.Display.V1.FormattedValue`] || String(value);
    const options = formattedValue.split(";").map((s: string) => s.trim());

    return (
      <div style={{ display: "flex", gap: tokens.spacingHorizontalXS, flexWrap: "wrap" }}>
        {options.map((opt: string, idx: number) => (
          <Badge key={idx} appearance="outline" color="informative" size="small">
            {opt}
          </Badge>
        ))}
      </div>
    );
  }

  /**
   * Render lookup with entity reference
   */
  private static renderLookup(value: any, record: IDatasetRecord, column: IDatasetColumn): React.ReactElement | string {
    if (value == null) return "";

    // Lookup formatted value is in columnname_formatted or @OData annotation
    const lookupName = record[`${column.name}_formatted`] ||
                       record[`${column.name}@OData.Community.Display.V1.FormattedValue`] ||
                       record[`_${column.name}_value@OData.Community.Display.V1.FormattedValue`] ||
                       String(value);

    // Could add click handler to open related record (future enhancement)
    return (
      <Link>
        {lookupName}
      </Link>
    );
  }

  /**
   * Render boolean
   */
  private static renderBoolean(value: any): React.ReactElement | string {
    return this.renderTwoOptions(value);
  }
}
