/**
 * Column renderer service - Type-based cell rendering
 */
import * as React from 'react';
import { Badge, Link, tokens, Text } from '@fluentui/react-components';
import { CheckmarkCircle20Regular, DismissCircle20Regular } from '@fluentui/react-icons';
import { DataverseAttributeType } from '../types/ColumnRendererTypes';
/**
 * Column renderer registry
 */
export class ColumnRendererService {
    /**
     * Get appropriate renderer for a column based on its dataType
     */
    static getRenderer(column) {
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
    static renderSecuredField() {
        return (React.createElement(Text, { italic: true, style: { color: tokens.colorNeutralForeground3 } }, "***"));
    }
    /**
     * Render plain text
     */
    static renderText(value) {
        if (value == null || value === '') {
            return '';
        }
        return React.createElement(Text, null, String(value));
    }
    /**
     * Render email with link
     */
    static renderEmail(value) {
        if (!value)
            return '';
        return (React.createElement(Link, { href: `mailto:${value}`, target: "_blank" }, String(value)));
    }
    /**
     * Render phone
     */
    static renderPhone(value) {
        if (!value)
            return '';
        return React.createElement(Link, { href: `tel:${value}` }, String(value));
    }
    /**
     * Render URL with link
     */
    static renderUrl(value) {
        if (!value)
            return '';
        // Ensure URL has protocol
        const url = String(value).startsWith('http') ? value : `https://${value}`;
        return (React.createElement(Link, { href: url, target: "_blank" }, String(value)));
    }
    /**
     * Render number with locale formatting
     */
    static renderNumber(value) {
        if (value == null)
            return '';
        const num = Number(value);
        if (isNaN(num))
            return String(value);
        return React.createElement(Text, null, num.toLocaleString());
    }
    /**
     * Render money with currency symbol
     */
    static renderMoney(value, record, column) {
        if (value == null)
            return '';
        const num = Number(value);
        if (isNaN(num))
            return String(value);
        // Check for currency code in record (e.g., transactioncurrencyid_formatted)
        const currencyField = `${column.name}_currency`;
        const currencyCode = record[currencyField] || 'USD';
        const formatted = num.toLocaleString(undefined, {
            style: 'currency',
            currency: currencyCode,
        });
        return React.createElement(Text, null, formatted);
    }
    /**
     * Render date and time
     */
    static renderDateTime(value) {
        if (!value)
            return '';
        const date = new Date(value);
        if (isNaN(date.getTime()))
            return String(value);
        const formatted = date.toLocaleString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
        return React.createElement(Text, null, formatted);
    }
    /**
     * Render date only
     */
    static renderDateOnly(value) {
        if (!value)
            return '';
        const date = new Date(value);
        if (isNaN(date.getTime()))
            return String(value);
        const formatted = date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
        return React.createElement(Text, null, formatted);
    }
    /**
     * Render two options (boolean) with icons
     */
    static renderTwoOptions(value) {
        if (value == null)
            return '';
        const isTrue = value === true || value === 1 || value === '1' || value === 'true';
        return isTrue ? (React.createElement(CheckmarkCircle20Regular, { style: { color: tokens.colorPaletteGreenForeground1 } })) : (React.createElement(DismissCircle20Regular, { style: { color: tokens.colorNeutralForeground3 } }));
    }
    /**
     * Render option set (choice) with badge
     */
    static renderOptionSet(value, record, column) {
        if (value == null)
            return '';
        // Use formatted value if available (e.g., "columnname@OData.Community.Display.V1.FormattedValue")
        const formattedValue = record[`${column.name}@OData.Community.Display.V1.FormattedValue`] || String(value);
        return (React.createElement(Badge, { appearance: "outline", color: "informative" }, formattedValue));
    }
    /**
     * Render multi-select option set with multiple badges
     */
    static renderMultiSelectOptionSet(value, record, column) {
        if (value == null)
            return '';
        // Multi-select values come as comma-separated string or array
        const formattedValue = record[`${column.name}@OData.Community.Display.V1.FormattedValue`] || String(value);
        const options = formattedValue.split(';').map((s) => s.trim());
        return (React.createElement("div", { style: {
                display: 'flex',
                gap: tokens.spacingHorizontalXS,
                flexWrap: 'wrap',
            } }, options.map((opt, idx) => (React.createElement(Badge, { key: idx, appearance: "outline", color: "informative", size: "small" }, opt)))));
    }
    /**
     * Render lookup with entity reference
     */
    static renderLookup(value, record, column) {
        if (value == null)
            return '';
        // Lookup formatted value is in columnname_formatted or @OData annotation
        const lookupName = record[`${column.name}_formatted`] ||
            record[`${column.name}@OData.Community.Display.V1.FormattedValue`] ||
            record[`_${column.name}_value@OData.Community.Display.V1.FormattedValue`] ||
            String(value);
        // Could add click handler to open related record (future enhancement)
        return React.createElement(Link, null, lookupName);
    }
    /**
     * Render boolean
     */
    static renderBoolean(value) {
        return this.renderTwoOptions(value);
    }
}
//# sourceMappingURL=ColumnRendererService.js.map