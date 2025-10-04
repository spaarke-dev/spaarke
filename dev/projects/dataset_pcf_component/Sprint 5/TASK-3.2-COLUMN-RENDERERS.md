# TASK-3.2: Column Renderers Implementation

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** Phase 3 - Advanced Features
**Estimated Time:** 4 hours
**Prerequisites:** TASK-2.3 (GridView), TASK-3.1 (Command System)
**Next Task:** TASK-3.3 (Virtualization)

---

## Objective

Implement **type-based column renderers** to display different data types appropriately in the GridView, CardView, and ListView components. This includes handling Dataverse data types (text, number, date, choice, lookup, boolean) with proper formatting and respecting **field-level security**.

**Why:**
- Improve user experience with type-appropriate rendering (dates formatted, choices labeled, lookups clickable)
- Support Dataverse data types per [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)
- Respect column-level security implemented in previous tasks
- Enable future extensibility for custom renderers

---

## Critical Standards

**Must Follow:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Fluent UI v9 components only
- [ADR-012-Shared-Component-Library.md](../../docs/adrs/ADR-012-Shared-Component-Library.md) - Build in `src/shared/Spaarke.UI.Components/`
- [KM-COLUMN-LEVEL-SECURITY.md](../../docs/KM-COLUMN-LEVEL-SECURITY.md) - Respect field security

**Key Rules:**
- ✅ Use Fluent UI v9 components (Badge, Persona, Link, etc.)
- ✅ NO Fluent UI v8 imports
- ✅ Respect `IDatasetColumn.canRead` for secured fields
- ✅ Display masked values (`***`) for secured fields with `canRead = false`
- ✅ Support all Dataverse attribute types
- ✅ TypeScript strict mode

---

## Current State Analysis

### What We Have Built

From previous tasks, we have:

1. **Core Types** ([DatasetTypes.ts](../../src/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts)):
   ```typescript
   export interface IDatasetColumn {
     name: string;
     displayName: string;
     dataType: string;  // "SingleLine.Text", "Whole.None", "DateTime", etc.
     isKey?: boolean;
     isPrimary?: boolean;
     visualSizeFactor?: number;
     // Field-level security
     isSecured?: boolean;
     canRead?: boolean;
     canUpdate?: boolean;
     canCreate?: boolean;
   }
   ```

2. **GridView** with basic string rendering:
   ```typescript
   renderCell: (item) => {
     // Check field-level read permission
     if (col.isSecured && col.canRead === false) {
       return <span style={{ fontStyle: "italic", color: tokens.colorNeutralForeground3 }}>***</span>;
     }

     const value = item[col.name];
     return <span>{value != null ? String(value) : ""}</span>;
   }
   ```

3. **Field Security** integrated via [FieldSecurityService](../../src/shared/Spaarke.UI.Components/src/services/FieldSecurityService.ts)

### What Needs Enhancement

- Replace generic `String(value)` with **type-specific renderers**
- Handle Dataverse data types: `TwoOptions`, `Picklist`, `Lookup.Simple`, `DateTime`, `Money`, `Decimal.Number`, etc.
- Use Fluent UI v9 components for visual consistency
- Extract renderers into reusable functions

---

## Implementation Steps

### Step 1: Create Column Renderer Types

Create type definitions for column renderers.

**File:** `src/shared/Spaarke.UI.Components/src/types/ColumnRendererTypes.ts`

```typescript
/**
 * Column renderer types for different Dataverse attribute types
 */

import { IDatasetColumn, IDatasetRecord } from "./DatasetTypes";

/**
 * Column renderer function signature
 */
export type ColumnRenderer = (
  value: any,
  record: IDatasetRecord,
  column: IDatasetColumn
) => React.ReactElement | string | null;

/**
 * Dataverse attribute type mapping
 * Matches ComponentFramework.PropertyHelper.DataSetApi.DataType
 */
export enum DataverseAttributeType {
  // String types
  SingleLineText = "SingleLine.Text",
  MultipleLineText = "Multiple",
  Email = "SingleLine.Email",
  Phone = "SingleLine.Phone",
  Url = "SingleLine.URL",
  TickerSymbol = "SingleLine.Ticker",

  // Number types
  WholeNumber = "Whole.None",
  DecimalNumber = "Decimal.Number",
  FloatingPoint = "FP",
  Money = "Currency",

  // Date/Time
  DateAndTime = "DateAndTime.DateAndTime",
  DateOnly = "DateAndTime.DateOnly",

  // Choice types
  TwoOptions = "TwoOptions",
  OptionSet = "OptionSet",
  MultiSelectOptionSet = "MultiSelectOptionSet",

  // Lookup
  Lookup = "Lookup.Simple",
  Customer = "Lookup.Customer",
  Owner = "Lookup.Owner",
  PartyList = "Lookup.PartyList",
  Regarding = "Lookup.Regarding",

  // Other
  Boolean = "Boolean",
  Image = "Image",
  File = "File"
}

/**
 * Choice option metadata
 */
export interface IChoiceOption {
  value: number;
  label: string;
  color?: string;
}

/**
 * Lookup reference metadata
 */
export interface ILookupReference {
  id: string;
  name: string;
  entityType: string;
}
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 2: Create Base Column Renderers Service

Implement core renderer functions for each data type.

**File:** `src/shared/Spaarke.UI.Components/src/services/ColumnRendererService.tsx`

```typescript
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
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 3: Update GridView to Use Renderers

Modify GridView to use type-based renderers instead of generic string rendering.

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx`

**Find:**
```typescript
renderCell: (item) => {
  // Check field-level read permission
  if (col.isSecured && col.canRead === false) {
    return <span style={{ fontStyle: "italic", color: tokens.colorNeutralForeground3 }}>***</span>;
  }

  const value = item[col.name];
  return <span>{value != null ? String(value) : ""}</span>;
}
```

**Replace with:**
```typescript
renderCell: (item) => {
  const renderer = ColumnRendererService.getRenderer(col);
  return renderer(item[col.name], item, col);
}
```

**Add import:**
```typescript
import { ColumnRendererService } from "../../services/ColumnRendererService";
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 4: Update CardView to Use Renderers

Apply the same renderer pattern to CardView.

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/CardView.tsx`

**Find:**
```typescript
{displayColumns.slice(1).map((col) => {
  // Check field-level read permission
  const fieldValue = col.isSecured && col.canRead === false
    ? "***"
    : String(record[col.name] || "-");

  return (
    <div key={col.name} className={styles.fieldRow}>
      <Text className={styles.fieldLabel}>{col.displayName}:</Text>
      <Text className={styles.fieldValue} truncate>{fieldValue}</Text>
    </div>
  );
})}
```

**Replace with:**
```typescript
{displayColumns.slice(1).map((col) => {
  const renderer = ColumnRendererService.getRenderer(col);
  const renderedValue = renderer(record[col.name], record, col);

  return (
    <div key={col.name} className={styles.fieldRow}>
      <Text className={styles.fieldLabel}>{col.displayName}:</Text>
      <div className={styles.fieldValue}>{renderedValue}</div>
    </div>
  );
})}
```

**Add import:**
```typescript
import { ColumnRendererService } from "../../services/ColumnRendererService";
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 5: Update ListView to Use Renderers

Apply renderers to ListView for primary field display.

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx`

**Add renderer support for the primary field:**
```typescript
const primaryColumn = props.columns[0];
const renderer = ColumnRendererService.getRenderer(primaryColumn);
const primaryValue = renderer(record[primaryColumn.name], record, primaryColumn);
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 6: Export New Types and Services

Update exports to include new column renderer types and services.

**File:** `src/shared/Spaarke.UI.Components/src/types/index.ts`

**Add:**
```typescript
export * from "./ColumnRendererTypes";
export { ColumnRendererService } from "../services/ColumnRendererService";
```

**Validation:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build
```

---

### Step 7: Test with Sample Data

Create test data to verify all renderers work correctly.

**File:** `src/shared/Spaarke.UI.Components/src/__tests__/ColumnRendererService.test.tsx` (optional)

```typescript
import { render } from "@testing-library/react";
import { ColumnRendererService } from "../services/ColumnRendererService";
import { IDatasetColumn } from "../types/DatasetTypes";
import { DataverseAttributeType } from "../types/ColumnRendererTypes";

describe("ColumnRendererService", () => {
  it("should render email with link", () => {
    const column: IDatasetColumn = {
      name: "emailaddress1",
      displayName: "Email",
      dataType: DataverseAttributeType.Email
    };

    const renderer = ColumnRendererService.getRenderer(column);
    const result = renderer("test@example.com", {}, column);

    const { container } = render(<>{result}</>);
    expect(container.querySelector("a")).toHaveAttribute("href", "mailto:test@example.com");
  });

  it("should render secured field as masked", () => {
    const column: IDatasetColumn = {
      name: "ssn",
      displayName: "SSN",
      dataType: DataverseAttributeType.SingleLineText,
      isSecured: true,
      canRead: false
    };

    const renderer = ColumnRendererService.getRenderer(column);
    const result = renderer("123-45-6789", {}, column);

    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe("***");
  });
});
```

**Run tests:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm test
```

---

## Validation Checklist

Run these commands to verify completion:

```bash
# 1. Build succeeds
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build

# 2. No TypeScript errors
npm run type-check

# 3. Exports are correct
node -e "const lib = require('./dist/index.js'); console.log('ColumnRendererService:', typeof lib.ColumnRendererService); console.log('DataverseAttributeType:', typeof lib.DataverseAttributeType);"
```

**Manual Verification:**

1. ✅ `ColumnRendererTypes.ts` defines all Dataverse attribute types
2. ✅ `ColumnRendererService.tsx` implements renderers for each type
3. ✅ GridView uses `ColumnRendererService.getRenderer()`
4. ✅ CardView uses `ColumnRendererService.getRenderer()`
5. ✅ ListView uses `ColumnRendererService.getRenderer()`
6. ✅ Secured fields render as `***`
7. ✅ Email/Phone/URL render as clickable links
8. ✅ Dates formatted with locale
9. ✅ Choice fields render as badges
10. ✅ Lookups render as links

---

## Success Criteria

- ✅ **Type-based rendering:** Different cell renderers for each Dataverse attribute type
- ✅ **Field security respected:** Secured fields with `canRead = false` show `***`
- ✅ **Fluent UI v9 components:** Badge, Link, Text used (no v8)
- ✅ **Formatted values:** Dates, numbers, currency properly formatted
- ✅ **Clickable links:** Email, phone, URL, lookups are interactive
- ✅ **Build passes:** 0 TypeScript errors
- ✅ **Exports correct:** ColumnRendererService and types exported

---

## Deliverables

### Files Created
1. `src/shared/Spaarke.UI.Components/src/types/ColumnRendererTypes.ts` - Renderer types
2. `src/shared/Spaarke.UI.Components/src/services/ColumnRendererService.tsx` - Renderer implementations

### Files Modified
1. `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx` - Use renderers
2. `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/CardView.tsx` - Use renderers
3. `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx` - Use renderers
4. `src/shared/Spaarke.UI.Components/src/types/index.ts` - Export renderers

### Build Outputs
- `dist/types/ColumnRendererTypes.d.ts`
- `dist/services/ColumnRendererService.d.ts`
- `dist/index.d.ts` (updated exports)

---

## Common Issues

### Issue 1: "Cannot find module ColumnRendererService"

**Cause:** Missing export in index.ts

**Fix:**
```typescript
// src/types/index.ts
export { ColumnRendererService } from "../services/ColumnRendererService";
```

### Issue 2: "Property 'dataType' does not exist on type 'IDatasetColumn'"

**Cause:** DatasetTypes.ts missing dataType property

**Fix:** Already exists in current implementation.

### Issue 3: Formatted values not showing for lookups/choices

**Cause:** PCF dataset doesn't always provide `@OData.Community.Display.V1.FormattedValue` annotations

**Fix:** Add fallback logic in renderers to use `_formatted` suffix or raw value.

---

## Notes

### Dataverse Data Type Mapping

PCF Dataset `column.dataType` values:
- Single line text: `"SingleLine.Text"`
- Email: `"SingleLine.Email"`
- Phone: `"SingleLine.Phone"`
- URL: `"SingleLine.URL"`
- Whole number: `"Whole.None"`
- Decimal: `"Decimal.Number"`
- Money: `"Currency"`
- Date/Time: `"DateAndTime.DateAndTime"`
- Date only: `"DateAndTime.DateOnly"`
- Two options: `"TwoOptions"`
- Choice: `"OptionSet"`
- Multi-choice: `"MultiSelectOptionSet"`
- Lookup: `"Lookup.Simple"`, `"Lookup.Customer"`, `"Lookup.Owner"`

### Future Enhancements

1. **Custom renderers:** Allow apps to register custom renderers for specific columns
2. **Lookup navigation:** Click lookup to open related record
3. **Rich text rendering:** Support HTML in multiline text fields
4. **Image rendering:** Display images inline
5. **File download:** Add download links for file attributes

---

## Next Steps

After completing this task:

1. ✅ Mark this task complete in [TASK-INDEX.md](./TASK-INDEX.md)
2. ➡️ Proceed to [TASK-3.3-VIRTUALIZATION.md](./TASK-3.3-VIRTUALIZATION.md)
3. 📝 Update completion time in this document

---

**Status:** 📝 Ready for Implementation
**Last Updated:** 2025-10-03
**Completion Date:** _[Fill in after completion]_
**Actual Time:** _[Fill in after completion]_
