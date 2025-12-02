/**
 * UniversalDatasetGrid - Main component for dataset display
 * Routes to GridView, CardView, or ListView based on configuration
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md, ADR-012
 */

import * as React from "react";
import { FluentProvider, makeStyles, tokens } from "@fluentui/react-components";
import { detectTheme } from "../../utils/themeDetection";
import { IDatasetConfig } from "../../types";
import { useDatasetMode } from "../../hooks/useDatasetMode";
import { useHeadlessMode } from "../../hooks/useHeadlessMode";
import { useKeyboardShortcuts } from "../../hooks/useKeyboardShortcuts";
import { CommandToolbar } from "../Toolbar/CommandToolbar";
import { CommandRegistry } from "../../services/CommandRegistry";
import { PrivilegeService } from "../../services/PrivilegeService";
import { EntityConfigurationService } from "../../services/EntityConfigurationService";
import { ICommandContext, IEntityPrivileges } from "../../types/CommandTypes";
import { GridView } from "./GridView";
import { CardView } from "./CardView";
import { ListView } from "./ListView";

export interface IUniversalDatasetGridProps {
  // Configuration
  config?: IDatasetConfig; // Make optional for entity-based config
  configJson?: string; // NEW: JSON configuration string

  // Data Source (mutually exclusive)
  dataset?: ComponentFramework.PropertyTypes.DataSet;
  headlessConfig?: {
    webAPI: ComponentFramework.WebApi;
    entityName: string;
    fetchXml?: string;
    pageSize: number;
  };

  // Selection
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;

  // Actions
  onRecordClick: (recordId: string) => void;

  // Context (for theme detection)
  context: any; // ComponentFramework.Context<IInputs>
}

const useStyles = makeStyles({
  root: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    fontFamily: tokens.fontFamilyBase
  },
  content: {
    flex: 1,
    overflow: "hidden"
  },
  loading: {
    padding: tokens.spacingVerticalXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground2
  },
  error: {
    padding: tokens.spacingVerticalXL,
    textAlign: "center",
    color: tokens.colorPaletteRedForeground1
  }
});

export const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps> = (props) => {
  const styles = useStyles();

  // Load entity configuration if provided
  React.useEffect(() => {
    if (props.configJson) {
      EntityConfigurationService.loadConfiguration(props.configJson);
    }
  }, [props.configJson]);

  // Determine data mode
  const isHeadlessMode = !!props.headlessConfig;

  // Use appropriate hook based on mode
  const datasetResult = useDatasetMode({
    dataset: props.dataset || ({} as any)
  });

  const headlessResult = useHeadlessMode({
    webAPI: props.headlessConfig?.webAPI || ({} as any),
    entityName: props.headlessConfig?.entityName || "",
    fetchXml: props.headlessConfig?.fetchXml,
    pageSize: props.headlessConfig?.pageSize || 25,
    autoLoad: isHeadlessMode
  });

  // Select result based on mode
  const result = isHeadlessMode ? headlessResult : datasetResult;
  const { records, columns, loading, error, hasNextPage, loadNextPage, refresh } = result;

  // Get entity name
  const entityName = records[0]?.entityName || props.headlessConfig?.entityName || "";

  // Get entity-specific configuration
  const entityConfig = React.useMemo(() => {
    if (!entityName) return null;
    return EntityConfigurationService.getEntityConfiguration(entityName);
  }, [entityName]);

  // Merge props config with entity config
  const finalConfig: IDatasetConfig = React.useMemo(() => {
    const baseConfig = props.config ?? {
      viewMode: "Grid",
      enableVirtualization: true,
      rowHeight: 44,
      selectionMode: "Multiple",
      showToolbar: true,
      enabledCommands: ["open", "create", "delete", "refresh"],
      theme: "Auto",
      scrollBehavior: "Auto"
    };

    if (!entityConfig) return baseConfig;

    return {
      ...baseConfig,
      viewMode: entityConfig.viewMode,
      enabledCommands: entityConfig.enabledCommands,
      compactToolbar: entityConfig.compactToolbar,
      enableVirtualization: entityConfig.enableVirtualization,
      rowHeight: entityConfig.rowHeight,
      scrollBehavior: entityConfig.scrollBehavior,
      toolbarShowOverflow: entityConfig.toolbarShowOverflow
    };
  }, [props.config, entityConfig]);

  // Detect theme from context
  const theme = React.useMemo(
    () => detectTheme(props.context, finalConfig.theme),
    [props.context, finalConfig.theme]
  );

  // Get entity privileges
  const [privileges, setPrivileges] = React.useState<IEntityPrivileges | undefined>();

  React.useEffect(() => {
    if (props.dataset) {
      // Dataset mode: Use dataset.security
      const datasetPrivileges = PrivilegeService.getPrivilegesFromDataset(props.dataset);
      setPrivileges(datasetPrivileges);
    } else if (props.headlessConfig?.webAPI && props.headlessConfig?.entityName) {
      // Headless mode: Query privileges via Web API
      PrivilegeService.getEntityPrivileges(
        props.headlessConfig.webAPI,
        props.headlessConfig.entityName
      ).then(setPrivileges);
    }
  }, [props.dataset, props.headlessConfig?.webAPI, props.headlessConfig?.entityName]);

  // Select view component based on config
  const ViewComponent = React.useMemo(() => {
    switch (finalConfig.viewMode) {
      case "Card":
        return CardView;
      case "List":
        return ListView;
      case "Grid":
      default:
        return GridView;
    }
  }, [finalConfig.viewMode]);

  // Handle record click
  const handleRecordClick = React.useCallback((record: any) => {
    props.onRecordClick(record.id);
  }, [props]);

  // Get commands based on config and privileges (including custom commands)
  const commands = React.useMemo(() => {
    if (!entityName) {
      return CommandRegistry.getCommands(finalConfig.enabledCommands, privileges);
    }
    return CommandRegistry.getCommandsWithCustom(
      finalConfig.enabledCommands,
      entityName,
      privileges
    );
  }, [finalConfig.enabledCommands, entityName, privileges]);

  // Build command context
  const commandContext = React.useMemo((): ICommandContext => {
    return {
      selectedRecords: records.filter(r => props.selectedRecordIds.includes(r.id)),
      entityName: records[0]?.entityName || props.headlessConfig?.entityName || "",
      webAPI: props.headlessConfig?.webAPI || (props.context as any)?.webAPI || ({} as any),
      navigation: (props.context as any)?.navigation || ({} as any),
      refresh: refresh,
      emitLastAction: (action) => {
        console.log(`Last action: ${action}`);
      }
    };
  }, [records, props.selectedRecordIds, props.headlessConfig, props.context, refresh]);

  // Enable keyboard shortcuts
  useKeyboardShortcuts({
    commands,
    context: commandContext,
    enabled: finalConfig.showToolbar && commands.length > 0
  });

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Command Toolbar */}
        {finalConfig.showToolbar && (
          <CommandToolbar
            commands={commands}
            context={commandContext}
            compact={finalConfig.compactToolbar}
            showOverflow={finalConfig.toolbarShowOverflow}
            onCommandExecuted={(key) => {
              console.log(`Command executed: ${key}`);
            }}
          />
        )}

        <div className={styles.content}>
          {error ? (
            <div className={styles.error}>Error: {error}</div>
          ) : (
            <ViewComponent
              records={records}
              columns={columns}
              selectedRecordIds={props.selectedRecordIds}
              onSelectionChange={props.onSelectionChange}
              onRecordClick={handleRecordClick}
              enableVirtualization={finalConfig.enableVirtualization}
              rowHeight={finalConfig.rowHeight}
              scrollBehavior={finalConfig.scrollBehavior}
              loading={loading}
              hasNextPage={hasNextPage}
              loadNextPage={loadNextPage}
            />
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
