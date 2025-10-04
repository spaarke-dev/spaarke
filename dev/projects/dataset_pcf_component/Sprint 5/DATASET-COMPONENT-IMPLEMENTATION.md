# Dataset Component Implementation Guide

## Core Component Structure
### Main Component Class
```typescript
// index.ts - Main PCF Component
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { UniversalDatasetGrid } from "./components/UniversalDatasetGrid";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private entityConfig: IEntityConfiguration;
  private selectedRecordIds: string[] = [];
  private totalCount: number = 0;
  private lastAction?: string;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;

    // Detect entity and load configuration
    this.entityConfig = this.resolveEntityConfiguration(context);

    // Enable responsive sizing
    context.mode.trackContainerResize(true);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    // Build props from context and configuration
    const props = this.buildComponentProps(context);

    // Render React component
    ReactDOM.render(
      React.createElement(UniversalDatasetGrid, props),
      this.container
    );
  }

  private resolveEntityConfiguration(context: ComponentFramework.Context<IInputs>): IEntityConfiguration {
    // Priority: explicit > dataset > context > tableName > default
    const entityName =
      context.parameters.entityName?.raw ||
      context.parameters.dataset?.getTargetEntityType?.() ||
      (context.mode as any).contextInfo?.entityTypeName ||
      context.parameters.tableName?.raw ||
      "unknown";

    return EntityConfigurationService.getConfig(entityName);
  }

  private buildComponentProps(context: ComponentFramework.Context<IInputs>): IUniversalDatasetProps {
    // Implementation left to React layer; pass dataset and inputs directly
    return {
      context,
      dataset: context.parameters.dataset,
      componentMode: (context.parameters.componentMode?.raw as any) ?? "Auto",
      viewMode: (context.parameters.viewMode?.raw as any) ?? "Grid",
      columnBehavior: parseJson(context.parameters.columnBehavior?.raw),
      enabledCommands: context.parameters.enabledCommands?.raw ?? "open,create,delete,refresh",
      commandConfig: parseJson(context.parameters.commandConfig?.raw),
      onSelectionChange: ids => {
        this.selectedRecordIds = ids;
        this.notifyOutputChanged();
      },
      onMetrics: m => PerformanceService.track(m),
      onAction: a => {
        this.lastAction = a;
        this.notifyOutputChanged();
      }
    };
  }

  public getOutputs(): IOutputs {
    return {
      selectedRecordIds: this.selectedRecordIds.join(","),
      totalRecordCount: this.totalCount,
      lastAction: this.lastAction ?? ""
    } as any;
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

### React Component Pattern
```typescript
// components/UniversalDatasetGrid.tsx
import React, { useMemo } from "react";
import { FluentProvider } from "@fluentui/react-components";
import { useDatasetMode } from "../hooks/useDatasetMode";
import { useHeadlessMode } from "../hooks/useHeadlessMode";
import { GridView } from "./views/GridView";
import { CardView } from "./views/CardView";
import { ListView } from "./views/ListView";
import { spaarkeLight } from "../theme";

export const UniversalDatasetGrid: React.FC<IUniversalDatasetProps> = (props) => {
  // Determine data source
  const dataSource = props.componentMode === "Headless"
    ? useHeadlessMode(props)
    : useDatasetMode(props);

  // Select view component
  const ViewComponent = useMemo(() => {
    switch (props.viewMode) {
      case "Card": return CardView;
      case "List": return ListView;
      // case "Kanban": return KanbanView;
      default: return GridView;
    }
  }, [props.viewMode]);

  // Detect and apply theme
  const hostTheme = (props.context as any).fluentDesignLanguage?.tokenTheme;
  const theme = hostTheme ?? spaarkeLight;

  return (
    <FluentProvider theme={theme}>
      <ViewComponent
        data={dataSource}
        config={props}
        onAction={props.onAction}
      />
    </FluentProvider>
  );
};
```

## Data Pipeline Patterns
### Dataset Mode Hook
```typescript
// hooks/useDatasetMode.ts
export function useDatasetMode(props: IUniversalDatasetProps) {
  const ds = props.dataset!;
  const items = React.useMemo(() => {
    if (!ds || ds.loading) return [];
    const ids = ds.sortedRecordIds ?? Object.keys(ds.records);
    return ids.map(id => transformRecord(ds.records[id], ds.columns));
  }, [ds.records, ds.sortedRecordIds, ds.columns, ds.loading]);

  const loadMore = React.useCallback(() => {
    if (ds.paging.hasNextPage) ds.paging.loadNextPage();
  }, [ds.paging]);

  return {
    items,
    loading: ds.loading,
    hasMore: ds.paging.hasNextPage,
    loadMore,
    refresh: () => ds.refresh()
  };
}
```

### Headless Mode Hook
```typescript
// hooks/useHeadlessMode.ts
export function useHeadlessMode(props: IUniversalDatasetProps) {
  const [items, setItems] = React.useState<IDataRow[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [nextLink, setNextLink] = React.useState<string | undefined>();

  const fetchData = React.useCallback(async () => {
    setLoading(true);
    try {
      const query = buildQuery(props);
      const result = await props.context.webAPI.retrieveMultipleRecords(props.tableName!, query);
      setItems(prev => [...prev, ...result.entities.map(transformEntity)]);
      setNextLink(result.nextLink);
    } finally {
      setLoading(false);
    }
  }, [props]);

  React.useEffect(() => { fetchData(); }, []);

  return {
    items,
    loading,
    hasMore: !!nextLink,
    loadMore: fetchData,
    refresh: () => { setItems([]); setNextLink(undefined); fetchData(); }
  };
}
```

## Column Rendering System
### Type-to-Renderer Mapping
```typescript
// renderers/ColumnRendererFactory.ts
export class ColumnRendererFactory {
  static getRenderer(column: IColumn, config?: IColumnBehavior): IColumnRenderer {
    if (config?.renderer) {
      return this.customRenderers.get(config.renderer);
    }
    switch (column.dataType) {
      case "SingleLine.Text":
        return column.name.includes("email") ? new EmailRenderer() : new TextRenderer();
      case "Lookup":
        return new LookupRenderer();
      case "OptionSet":
        return new ChoiceRenderer();
      case "DateTime":
        return new DateTimeRenderer();
      case "Currency":
        return new CurrencyRenderer();
      case "TwoOptions":
        return new BooleanRenderer();
      default:
        return new DefaultRenderer();
    }
  }
}
```

### Fluent v9 Cell Components
```typescript
// renderers/FluentCellRenderers.tsx
import { TableCellLayout, Avatar, Badge, Link } from "@fluentui/react-components";

export const LookupRenderer: React.FC<ICellProps> = ({ value }) => (
  <TableCellLayout media={<Avatar size={24} name={value?.name} />} truncate>
    <Link onClick={() => navigateToRecord(value.id, value.entityType)}>
      {value?.name || "-"}
    </Link>
  </TableCellLayout>
);

export const ChoiceRenderer: React.FC<ICellProps> = ({ value, config }) => {
  const color = config?.colorMap?.[value] || "neutral";
  return <Badge appearance="filled" color={color as any}>{value}</Badge>;
};

export const DateTimeRenderer: React.FC<ICellProps> = ({ value, formatting }) => (
  <TableCellLayout>{formatting.formatDateShort(new Date(value))}</TableCellLayout>
);
```

## AI Coding Prompt
Implement the core PCF and React composition:
- PCF lifecycle (`init`, `updateView`, `getOutputs`, `destroy`) in TypeScript with strong types from generated `ManifestTypes`.
- `resolveEntityConfiguration` using the priority order: explicit `entityName` > dataset target > context info > `tableName`.
- React subtree wrapped in `FluentProvider` with theme fallback; mount a `UniversalDatasetGrid` that switches between Dataset and Headless hooks.
- Hooks: `useDatasetMode` (reads dataset; transforms to items; supports paging/refresh/selection sync) and `useHeadlessMode` (retrieveMultipleRecords; nextLink paging; refresh).
- Column renderer factory using Fluent v9 cells (`TableCellLayout`, `Badge`, `Link`, `Avatar`) and platform formatting helpers.
- Navigation helpers to open records/views; use `context.navigation` not manual URLs.
- No global CSS; style with Griffel (`makeStyles`, `shorthands`, `tokens`).
Deliverables: `index.ts`, `components/UniversalDatasetGrid.tsx`, `hooks/useDatasetMode.ts`, `hooks/useHeadlessMode.ts`, and `renderers/*` with one example per type (text, lookup, choice, date).
