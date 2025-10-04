# Configuration UI Enhancement - Detailed Specification

**Project**: Universal Dataset Grid PCF Component
**Feature**: Visual Configuration Builder with Admin UI
**Status**: 📋 SPEC (Not Implemented)
**Target Sprint**: TBD (Post-v1.0.0)
**Estimated Effort**: 12-16 hours
**Priority**: Medium
**Created**: 2025-10-03

---

## Executive Summary

This specification outlines an admin-friendly visual configuration builder for the Universal Dataset Grid PCF component. Currently, configuration requires manual JSON editing in form properties or environment variables. This enhancement provides a model-driven app with visual forms that generate configuration JSON automatically, with multi-level caching to minimize performance impact.

**Key Benefits**:
- ✅ **No JSON editing required** - Visual form builder for admins
- ✅ **Per-entity configuration** - Different settings per entity
- ✅ **Version history** - Track configuration changes over time
- ✅ **Environment-independent** - Same config across Dev/Test/Prod
- ✅ **Minimal performance impact** - 2ms after first load (with caching)
- ✅ **No redeployment needed** - Change configs without solution updates

---

## Current State (v1.0.0)

### Configuration Methods Available

#### Method 1: Form Property (Current Recommended)
```xml
<!-- ControlManifest.Input.xml -->
<property name="configJson"
          display-name-key="Configuration JSON"
          of-type="Multiple"
          usage="input" />
```

**Admin Experience**:
1. Open form in designer
2. Select Universal Dataset Grid control
3. Paste JSON into `configJson` property
4. Publish form

**Pros**:
- ✅ Simple setup
- ✅ Form-specific configs
- ✅ Version controlled with solution

**Cons**:
- ❌ Requires JSON knowledge
- ❌ No syntax validation until runtime
- ❌ Must republish form to change
- ❌ Error-prone (typos, missing commas)

#### Method 2: Environment Variable
```bash
pac env var create --name "spaarke_DatasetGridConfig"
```

**Pros**:
- ✅ Environment-specific
- ✅ Change without solution update

**Cons**:
- ❌ Single config for ALL entities
- ❌ Still requires JSON editing
- ❌ Large JSON in text field (poor UX)

#### Method 3: Hardcoded in Component
```typescript
const config = {
  viewMode: "Grid",
  enabledCommands: ["open", "create"]
};
```

**Pros**:
- ✅ Fastest performance (no query)

**Cons**:
- ❌ Requires code changes
- ❌ Must rebuild and redeploy
- ❌ Not admin-friendly

---

## Proposed Solution

### Visual Configuration Builder (Admin UI)

**Model-Driven App**: "Dataset Grid Configuration Manager"

**Core Tables**:
1. `sprk_datasetgridconfiguration` - Main configuration storage
2. `sprk_customcommand` - Custom command definitions
3. `sprk_configurationhistory` - Version history

**User Experience**:
1. Admin opens "Dataset Grid Configuration Manager" app
2. Creates new configuration or edits existing
3. Selects entity from dropdown (e.g., "Account")
4. Configures settings via visual form (toggles, dropdowns, number inputs)
5. Adds custom commands via subgrid (no JSON editing)
6. Previews generated JSON in read-only field
7. Saves configuration
8. Changes take effect within 1 hour (or immediately with cache clear)

---

## Architecture Design

### Solution Components

```
SpaarkeSolution_ConfigurationUI/
├── Tables/
│   ├── sprk_datasetgridconfiguration
│   │   ├── Forms/
│   │   │   ├── Main Form (Visual Builder)
│   │   │   └── Quick Create Form
│   │   ├── Views/
│   │   │   ├── Active Configurations
│   │   │   ├── By Entity
│   │   │   └── Inactive Configurations
│   │   └── Business Rules/
│   │       └── Validate Configuration
│   ├── sprk_customcommand
│   │   ├── Forms/
│   │   │   └── Main Form
│   │   └── Views/
│   │       └── Active Commands
│   └── sprk_configurationhistory
│       └── Views/
│           └── All History
├── Plugins/
│   ├── GenerateConfigurationJSON.cs
│   ├── ValidateConfiguration.cs
│   └── ClearConfigurationCache.cs
├── Custom APIs/
│   ├── sprk_GenerateConfigJSON
│   ├── sprk_ValidateConfig
│   └── sprk_ClearConfigCache
├── Model-Driven App/
│   └── Dataset Grid Config Manager
├── Security Roles/
│   ├── Dataset Grid Administrator
│   └── Dataset Grid Viewer
└── Web Resources/
    ├── ConfigJSONPreview.js
    └── ConfigFormValidation.js
```

---

## Table Schemas

### Table 1: sprk_datasetgridconfiguration

**Display Name**: Dataset Grid Configuration
**Plural Name**: Dataset Grid Configurations
**Primary Field**: sprk_name (e.g., "Account - Main Form Config")
**Ownership**: Organization-owned

#### Columns

| Schema Name | Display Name | Type | Required | Description |
|-------------|--------------|------|----------|-------------|
| **sprk_name** | Name | Single Line Text (100) | Yes | Configuration name |
| **sprk_entityname** | Entity Name | Choice | Yes | Target entity logical name |
| **sprk_formname** | Form Name | Single Line Text (100) | No | Specific form (optional) |
| **sprk_description** | Description | Multiple Lines Text | No | Admin notes |
| **sprk_isactive** | Is Active | Yes/No | Yes | Enable/disable config |
| **sprk_viewmode** | Default View Mode | Choice | Yes | Grid, List, or Card |
| **sprk_compacttoolbar** | Compact Toolbar | Yes/No | Yes | Icon-only buttons |
| **sprk_enablevirtualization** | Enable Virtualization | Yes/No | Yes | Performance optimization |
| **sprk_virtualizationthreshold** | Virtualization Threshold | Whole Number | Yes | Min records for virtualization (default: 100) |
| **sprk_enablekeyboardshortcuts** | Enable Keyboard Shortcuts | Yes/No | Yes | Ctrl+N, Delete, etc. |
| **sprk_enableaccessibility** | Enable Accessibility | Yes/No | Yes | WCAG 2.1 AA features |
| **sprk_enabledcommands** | Enabled Commands | MultiSelect Choice | No | Open, Create, Delete, Refresh |
| **sprk_configjson** | Configuration JSON | Multiple Lines Text (JSON) | Yes | Generated JSON (read-only) |
| **sprk_version** | Version | Whole Number | Yes | Auto-increment on save |
| **sprk_lastupdatedby** | Last Updated By | Lookup (User) | No | Auto-populated |
| **sprk_lastupdatedon** | Last Updated On | Date/Time | No | Auto-populated |

#### Choice Fields

**sprk_entityname** (Global Choice Set):
- account
- contact
- opportunity
- lead
- case
- task
- (Dynamically populated from metadata)

**sprk_viewmode**:
- 1: Grid
- 2: List
- 3: Card

**sprk_enabledcommands** (Multi-Select):
- 1: Open
- 2: Create
- 3: Delete
- 4: Refresh

#### Relationships
- **1:N** → `sprk_customcommand` (parent: configuration)
- **1:N** → `sprk_configurationhistory` (parent: configuration)

---

### Table 2: sprk_customcommand

**Display Name**: Custom Command
**Plural Name**: Custom Commands
**Primary Field**: sprk_name (e.g., "Approve Invoice")
**Ownership**: Organization-owned

#### Columns

| Schema Name | Display Name | Type | Required | Description |
|-------------|--------------|------|----------|-------------|
| **sprk_name** | Command Name | Single Line Text (100) | Yes | Display label |
| **sprk_commandkey** | Command Key | Single Line Text (50) | Yes | Unique identifier (e.g., "approve") |
| **sprk_configurationid** | Configuration | Lookup (Configuration) | Yes | Parent config |
| **sprk_icon** | Icon | Single Line Text (50) | No | Fluent UI icon name |
| **sprk_actiontype** | Action Type | Choice | Yes | customapi, action, function, workflow |
| **sprk_actionname** | Action Name | Single Line Text (100) | Yes | Dataverse action name |
| **sprk_parameters** | Parameters | Multiple Lines Text (JSON) | No | Input parameters (JSON) |
| **sprk_requiresselection** | Requires Selection | Yes/No | Yes | Enable only when records selected |
| **sprk_minselection** | Minimum Selection | Whole Number | No | Min records required |
| **sprk_maxselection** | Maximum Selection | Whole Number | No | Max records allowed |
| **sprk_confirmationmessage** | Confirmation Message | Single Line Text (200) | No | Confirm before execution |
| **sprk_refresh** | Refresh After Execution | Yes/No | Yes | Reload grid after command |
| **sprk_sortorder** | Sort Order | Whole Number | Yes | Display order in toolbar |

#### Choice Fields

**sprk_actiontype**:
- 1: Custom API
- 2: Action
- 3: Function
- 4: Workflow

---

### Table 3: sprk_configurationhistory

**Display Name**: Configuration History
**Plural Name**: Configuration History
**Primary Field**: sprk_name (Auto-numbered)
**Ownership**: Organization-owned

#### Columns

| Schema Name | Display Name | Type | Required | Description |
|-------------|--------------|------|----------|-------------|
| **sprk_name** | History ID | Auto-number | Yes | Auto-generated |
| **sprk_configurationid** | Configuration | Lookup (Configuration) | Yes | Parent config |
| **sprk_version** | Version | Whole Number | Yes | Version number |
| **sprk_configjson** | Configuration JSON | Multiple Lines Text | Yes | JSON snapshot |
| **sprk_changedby** | Changed By | Lookup (User) | Yes | Who made change |
| **sprk_changedon** | Changed On | Date/Time | Yes | When changed |
| **sprk_changesummary** | Change Summary | Multiple Lines Text | No | What changed |

---

## Plugin Logic

### Plugin 1: GenerateConfigurationJSON.cs

**Trigger**: Pre-Create, Pre-Update of `sprk_datasetgridconfiguration`

**Purpose**: Generate `sprk_configjson` from form fields

**Algorithm**:
```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
    var service = GetOrganizationService(serviceProvider);

    var config = (Entity)context.InputParameters["Target"];

    // Build configuration object
    var configObj = new {
        schemaVersion = "1.0",
        defaultConfig = new {
            viewMode = GetViewModeString(config.GetAttributeValue<OptionSetValue>("sprk_viewmode")?.Value),
            compactToolbar = config.GetAttributeValue<bool>("sprk_compacttoolbar"),
            enableVirtualization = config.GetAttributeValue<bool>("sprk_enablevirtualization"),
            virtualizationThreshold = config.GetAttributeValue<int>("sprk_virtualizationthreshold"),
            enableKeyboardShortcuts = config.GetAttributeValue<bool>("sprk_enablekeyboardshortcuts"),
            enableAccessibility = config.GetAttributeValue<bool>("sprk_enableaccessibility"),
            enabledCommands = GetEnabledCommands(config),
            customCommands = GetCustomCommands(service, config.Id)
        }
    };

    // Serialize to JSON
    var json = JsonConvert.SerializeObject(configObj, Formatting.Indented);
    config["sprk_configjson"] = json;

    // Increment version
    var currentVersion = config.GetAttributeValue<int>("sprk_version");
    config["sprk_version"] = currentVersion + 1;

    // Update metadata
    config["sprk_lastupdatedby"] = new EntityReference("systemuser", context.UserId);
    config["sprk_lastupdatedon"] = DateTime.UtcNow;
}

private Dictionary<string, object> GetCustomCommands(IOrganizationService service, Guid configId)
{
    var query = new QueryExpression("sprk_customcommand");
    query.ColumnSet = new ColumnSet(true);
    query.Criteria.AddCondition("sprk_configurationid", ConditionOperator.Equal, configId);
    query.AddOrder("sprk_sortorder", OrderType.Ascending);

    var commands = service.RetrieveMultiple(query);
    var commandDict = new Dictionary<string, object>();

    foreach (var cmd in commands.Entities)
    {
        var commandKey = cmd.GetAttributeValue<string>("sprk_commandkey");
        commandDict[commandKey] = new {
            label = cmd.GetAttributeValue<string>("sprk_name"),
            icon = cmd.GetAttributeValue<string>("sprk_icon"),
            actionType = GetActionTypeString(cmd.GetAttributeValue<OptionSetValue>("sprk_actiontype")?.Value),
            actionName = cmd.GetAttributeValue<string>("sprk_actionname"),
            parameters = ParseParameters(cmd.GetAttributeValue<string>("sprk_parameters")),
            requiresSelection = cmd.GetAttributeValue<bool>("sprk_requiresselection"),
            minSelection = cmd.GetAttributeValue<int?>("sprk_minselection"),
            maxSelection = cmd.GetAttributeValue<int?>("sprk_maxselection"),
            confirmationMessage = cmd.GetAttributeValue<string>("sprk_confirmationmessage"),
            refresh = cmd.GetAttributeValue<bool>("sprk_refresh")
        };
    }

    return commandDict;
}
```

**Performance**: 50-100ms (runs server-side on save)

---

### Plugin 2: ValidateConfiguration.cs

**Trigger**: Pre-Create, Pre-Update of `sprk_datasetgridconfiguration`

**Purpose**: Validate configuration before save

**Validations**:
- Entity name exists in metadata
- Virtualization threshold > 0
- At least one enabled command
- Custom command keys are unique
- Custom command action names are valid
- Parameters JSON is valid JSON

**Example**:
```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
    var config = (Entity)context.InputParameters["Target"];

    // Validate virtualization threshold
    var threshold = config.GetAttributeValue<int>("sprk_virtualizationthreshold");
    if (threshold < 1 || threshold > 10000)
    {
        throw new InvalidPluginExecutionException(
            "Virtualization threshold must be between 1 and 10,000"
        );
    }

    // Validate at least one enabled command
    var enabledCommands = config.GetAttributeValue<OptionSetValueCollection>("sprk_enabledcommands");
    if (enabledCommands == null || enabledCommands.Count == 0)
    {
        throw new InvalidPluginExecutionException(
            "At least one built-in command must be enabled"
        );
    }

    // Validate custom commands (if any)
    // ... validation logic
}
```

---

### Plugin 3: CreateConfigurationHistory.cs

**Trigger**: Post-Update of `sprk_datasetgridconfiguration`

**Purpose**: Create history record on each save

**Algorithm**:
```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
    var service = GetOrganizationService(serviceProvider);

    var config = (Entity)context.InputParameters["Target"];

    var history = new Entity("sprk_configurationhistory");
    history["sprk_configurationid"] = new EntityReference("sprk_datasetgridconfiguration", config.Id);
    history["sprk_version"] = config.GetAttributeValue<int>("sprk_version");
    history["sprk_configjson"] = config.GetAttributeValue<string>("sprk_configjson");
    history["sprk_changedby"] = new EntityReference("systemuser", context.UserId);
    history["sprk_changedon"] = DateTime.UtcNow;

    service.Create(history);
}
```

---

## Custom APIs

### Custom API 1: sprk_GenerateConfigJSON

**Purpose**: Generate JSON from configuration ID (for preview)

**Parameters**:
- **Input**: ConfigurationId (Guid)
- **Output**: ConfigurationJSON (String)

**Usage**: Called from form JavaScript for live preview

---

### Custom API 2: sprk_ValidateConfig

**Purpose**: Validate configuration without saving

**Parameters**:
- **Input**: ConfigurationJSON (String)
- **Output**: IsValid (Boolean), Errors (String)

---

### Custom API 3: sprk_ClearConfigCache

**Purpose**: Clear browser cache for specific entity or all entities

**Parameters**:
- **Input**: EntityName (String, optional)
- **Output**: Success (Boolean)

**Implementation**:
```csharp
// Broadcasts message to all active users via SignalR or similar
// Users' browsers listen for message and clear localStorage
```

---

## Caching Architecture

### Multi-Level Cache Strategy

```
Configuration Retrieval Flow:

User Opens Grid
    ↓
┌─────────────────────────────────────┐
│ Level 1: Memory Cache (Component)  │ ← 0ms (same grid instance)
└─────────────────────────────────────┘
    ↓ MISS
┌─────────────────────────────────────┐
│ Level 2: localStorage (Browser)    │ ← 2ms (same session/new instance)
└─────────────────────────────────────┘
    ↓ MISS
┌─────────────────────────────────────┐
│ Level 3: sessionStorage (Browser)  │ ← 2ms (current session only)
└─────────────────────────────────────┘
    ↓ MISS
┌─────────────────────────────────────┐
│ Level 4: Dataverse Query            │ ← 200-500ms (first load only)
└─────────────────────────────────────┘
    ↓
┌─────────────────────────────────────┐
│ Level 5: Default Config             │ ← 0ms (fallback)
└─────────────────────────────────────┘
```

---

### TypeScript Service: ConfigurationCacheService.ts

**Location**: `src/shared/Spaarke.UI.Components/src/services/ConfigurationCacheService.ts`

```typescript
/**
 * Multi-level configuration caching service
 * Minimizes Dataverse queries for configuration loading
 */
export class ConfigurationCacheService {
  private static readonly CACHE_KEY_PREFIX = "spaarke_gridconfig_";
  private static readonly VERSION_KEY_PREFIX = "spaarke_gridconfig_version_";
  private static readonly CACHE_DURATION_MS = 3600000; // 1 hour
  private static memoryCache = new Map<string, IDatasetConfig>();

  /**
   * Get configuration with multi-level caching
   * @param context PCF context
   * @param entityName Entity logical name
   * @returns Configuration or null
   */
  public static async getConfiguration(
    context: ComponentFramework.Context<any>,
    entityName: string
  ): Promise<IDatasetConfig | null> {
    console.log(`[ConfigCache] Getting configuration for ${entityName}`);

    // Level 1: Memory cache (0ms)
    const memCached = this.getFromMemory(entityName);
    if (memCached) {
      console.log(`[ConfigCache] Memory cache HIT for ${entityName}`);
      return memCached;
    }

    // Level 2: localStorage (2ms)
    const localCached = this.getFromLocalStorage(entityName);
    if (localCached) {
      console.log(`[ConfigCache] localStorage HIT for ${entityName}`);
      this.memoryCache.set(entityName, localCached);
      return localCached;
    }

    // Level 3: sessionStorage (2ms)
    const sessionCached = this.getFromSessionStorage(entityName);
    if (sessionCached) {
      console.log(`[ConfigCache] sessionStorage HIT for ${entityName}`);
      this.memoryCache.set(entityName, sessionCached);
      this.saveToLocalStorage(entityName, sessionCached);
      return sessionCached;
    }

    // Level 4: Dataverse query (200-500ms)
    console.log(`[ConfigCache] Cache MISS for ${entityName}, querying Dataverse`);
    const config = await this.queryConfigurationTable(context, entityName);

    if (config) {
      this.saveToAllCaches(entityName, config);
      return config;
    }

    // Level 5: Fallback to defaults
    console.log(`[ConfigCache] No configuration found for ${entityName}, using defaults`);
    return null;
  }

  /**
   * Query configuration table in Dataverse
   */
  private static async queryConfigurationTable(
    context: ComponentFramework.Context<any>,
    entityName: string
  ): Promise<IDatasetConfig | null> {
    try {
      const query = [
        `?$filter=sprk_entityname eq '${entityName}'`,
        `and sprk_isactive eq true`,
        `&$top=1`,
        `&$select=sprk_configjson,sprk_version`,
        `&$orderby=sprk_version desc`
      ].join(" ");

      const result = await context.webAPI.retrieveMultipleRecords(
        "sprk_datasetgridconfiguration",
        query
      );

      if (result.entities.length > 0) {
        const entity = result.entities[0];
        const config = JSON.parse(entity.sprk_configjson);
        const version = entity.sprk_version;

        // Store version for cache invalidation
        this.saveVersion(entityName, version);

        return config;
      }

      return null;
    } catch (error) {
      console.error(`[ConfigCache] Error querying configuration table:`, error);
      return null;
    }
  }

  /**
   * Check if cached version matches latest version
   */
  public static async isCacheValid(
    context: ComponentFramework.Context<any>,
    entityName: string
  ): Promise<boolean> {
    const cachedVersion = this.getCachedVersion(entityName);
    if (!cachedVersion) return false;

    const latestVersion = await this.getLatestVersion(context, entityName);
    return cachedVersion === latestVersion;
  }

  /**
   * Get latest version from Dataverse (lightweight query)
   */
  private static async getLatestVersion(
    context: ComponentFramework.Context<any>,
    entityName: string
  ): Promise<number | null> {
    try {
      const result = await context.webAPI.retrieveMultipleRecords(
        "sprk_datasetgridconfiguration",
        `?$filter=sprk_entityname eq '${entityName}' and sprk_isactive eq true&$top=1&$select=sprk_version`
      );

      if (result.entities.length > 0) {
        return result.entities[0].sprk_version;
      }

      return null;
    } catch (error) {
      console.error(`[ConfigCache] Error getting latest version:`, error);
      return null;
    }
  }

  /**
   * Memory cache operations
   */
  private static getFromMemory(entityName: string): IDatasetConfig | null {
    return this.memoryCache.get(entityName) || null;
  }

  /**
   * localStorage operations
   */
  private static getFromLocalStorage(entityName: string): IDatasetConfig | null {
    try {
      const key = `${this.CACHE_KEY_PREFIX}${entityName}`;
      const cached = localStorage.getItem(key);

      if (!cached) return null;

      const parsed = JSON.parse(cached);
      const age = Date.now() - parsed.timestamp;

      if (age > this.CACHE_DURATION_MS) {
        console.log(`[ConfigCache] localStorage cache expired for ${entityName}`);
        localStorage.removeItem(key);
        return null;
      }

      return parsed.config;
    } catch (error) {
      console.error(`[ConfigCache] Error reading from localStorage:`, error);
      return null;
    }
  }

  private static saveToLocalStorage(entityName: string, config: IDatasetConfig): void {
    try {
      const key = `${this.CACHE_KEY_PREFIX}${entityName}`;
      localStorage.setItem(key, JSON.stringify({
        config,
        timestamp: Date.now()
      }));
    } catch (error) {
      console.error(`[ConfigCache] Error saving to localStorage:`, error);
    }
  }

  /**
   * sessionStorage operations (session-specific)
   */
  private static getFromSessionStorage(entityName: string): IDatasetConfig | null {
    try {
      const key = `${this.CACHE_KEY_PREFIX}${entityName}`;
      const cached = sessionStorage.getItem(key);

      if (!cached) return null;

      return JSON.parse(cached);
    } catch (error) {
      console.error(`[ConfigCache] Error reading from sessionStorage:`, error);
      return null;
    }
  }

  private static saveToSessionStorage(entityName: string, config: IDatasetConfig): void {
    try {
      const key = `${this.CACHE_KEY_PREFIX}${entityName}`;
      sessionStorage.setItem(key, JSON.stringify(config));
    } catch (error) {
      console.error(`[ConfigCache] Error saving to sessionStorage:`, error);
    }
  }

  /**
   * Version management
   */
  private static getCachedVersion(entityName: string): number | null {
    try {
      const key = `${this.VERSION_KEY_PREFIX}${entityName}`;
      const version = localStorage.getItem(key);
      return version ? parseInt(version, 10) : null;
    } catch {
      return null;
    }
  }

  private static saveVersion(entityName: string, version: number): void {
    try {
      const key = `${this.VERSION_KEY_PREFIX}${entityName}`;
      localStorage.setItem(key, version.toString());
    } catch (error) {
      console.error(`[ConfigCache] Error saving version:`, error);
    }
  }

  /**
   * Save to all cache levels
   */
  private static saveToAllCaches(entityName: string, config: IDatasetConfig): void {
    this.memoryCache.set(entityName, config);
    this.saveToLocalStorage(entityName, config);
    this.saveToSessionStorage(entityName, config);
  }

  /**
   * Clear cache for entity or all entities
   */
  public static clearCache(entityName?: string): void {
    if (entityName) {
      console.log(`[ConfigCache] Clearing cache for ${entityName}`);
      this.memoryCache.delete(entityName);
      localStorage.removeItem(`${this.CACHE_KEY_PREFIX}${entityName}`);
      sessionStorage.removeItem(`${this.CACHE_KEY_PREFIX}${entityName}`);
      localStorage.removeItem(`${this.VERSION_KEY_PREFIX}${entityName}`);
    } else {
      console.log(`[ConfigCache] Clearing all caches`);
      this.memoryCache.clear();

      // Clear all localStorage keys with prefix
      Object.keys(localStorage)
        .filter(key =>
          key.startsWith(this.CACHE_KEY_PREFIX) ||
          key.startsWith(this.VERSION_KEY_PREFIX)
        )
        .forEach(key => localStorage.removeItem(key));

      // Clear all sessionStorage keys with prefix
      Object.keys(sessionStorage)
        .filter(key => key.startsWith(this.CACHE_KEY_PREFIX))
        .forEach(key => sessionStorage.removeItem(key));
    }
  }

  /**
   * Preload configurations for multiple entities
   * Useful for form onLoad to warm cache
   */
  public static async preloadConfigurations(
    context: ComponentFramework.Context<any>,
    entityNames: string[]
  ): Promise<void> {
    console.log(`[ConfigCache] Preloading configurations for ${entityNames.length} entities`);

    const promises = entityNames.map(entityName =>
      this.getConfiguration(context, entityName)
    );

    await Promise.allSettled(promises);
  }

  /**
   * Get cache statistics
   */
  public static getCacheStats(): {
    memoryCacheSize: number;
    localStorageKeys: number;
    sessionStorageKeys: number;
  } {
    return {
      memoryCacheSize: this.memoryCache.size,
      localStorageKeys: Object.keys(localStorage).filter(k =>
        k.startsWith(this.CACHE_KEY_PREFIX)
      ).length,
      sessionStorageKeys: Object.keys(sessionStorage).filter(k =>
        k.startsWith(this.CACHE_KEY_PREFIX)
      ).length
    };
  }
}
```

---

### Integration with UniversalDatasetGrid Component

**Modified Component Init**:

```typescript
// src/components/DatasetGrid/DatasetGrid.tsx

export const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps> = (props) => {
  const [config, setConfig] = useState<IDatasetConfig>(DEFAULT_CONFIG);
  const [isLoadingConfig, setIsLoadingConfig] = useState(true);

  useEffect(() => {
    const loadConfiguration = async () => {
      if (!props.dataset) return;

      const entityName = props.dataset.getTargetEntityType();

      try {
        // Try cache first (multi-level)
        const cachedConfig = await ConfigurationCacheService.getConfiguration(
          props.context,
          entityName
        );

        if (cachedConfig) {
          setConfig({ ...DEFAULT_CONFIG, ...cachedConfig });
        } else {
          // Fall back to prop-based config or defaults
          if (props.config) {
            setConfig({ ...DEFAULT_CONFIG, ...props.config });
          }
        }
      } catch (error) {
        console.error("[UniversalDatasetGrid] Error loading configuration:", error);
        // Use defaults on error
      } finally {
        setIsLoadingConfig(false);
      }
    };

    loadConfiguration();
  }, [props.dataset, props.context]);

  // Rest of component...
};
```

---

## Performance Analysis

### Performance Comparison (All Options)

| Configuration Method | First Load | Subsequent Load | Admin Experience | Change Propagation |
|---------------------|------------|-----------------|------------------|-------------------|
| **Hardcoded** | 0ms | 0ms | ❌ Requires code change | Redeploy PCF |
| **Form Property** | 0ms | 0ms | ⚠️ JSON editing | Republish form |
| **Environment Variable** | 100-200ms | 100-200ms | ⚠️ JSON editing | Immediate |
| **Config Table (no cache)** | 200-500ms | 200-500ms | ✅ Visual form | Immediate |
| **Config Table + Memory Cache** | 200-500ms | 0ms* | ✅ Visual form | 1 hour delay |
| **Config Table + localStorage** | 200-500ms | 2ms | ✅ Visual form | 1 hour delay |
| **Config Table + Multi-Level** ⭐ | 200-500ms | 0-2ms** | ✅ Visual form | 1 hour delay |

\* *Same component instance*
\** *0ms if same instance, 2ms if new instance*

---

### Detailed Performance Breakdown

#### Scenario 1: User Opens Account Grid (First Time)

```
Timeline:
0ms     : PCF init() called
50ms    : Component mounted
52ms    : Check memory cache → MISS
54ms    : Check localStorage → MISS
56ms    : Check sessionStorage → MISS
58ms    : Start Dataverse query
         └─> SELECT sprk_configjson, sprk_version
             FROM sprk_datasetgridconfiguration
             WHERE sprk_entityname = 'account'
             AND sprk_isactive = true
             ORDER BY sprk_version DESC
             TOP 1
350ms   : Dataverse query complete (292ms)
355ms   : Parse JSON (5ms)
357ms   : Save to memory cache (0ms)
359ms   : Save to localStorage (2ms)
361ms   : Save to sessionStorage (2ms)
365ms   : Register commands (20ms)
465ms   : Render grid (100ms)
───────────────────────────────────────
Total: 465ms (first load)
```

#### Scenario 2: User Opens Account Grid Again (Same Session)

```
Timeline:
0ms     : PCF init() called
50ms    : Component mounted
52ms    : Check memory cache → HIT ✅
54ms    : Register commands (20ms)
154ms   : Render grid (100ms)
───────────────────────────────────────
Total: 154ms (cached, same session)
Performance impact: +4ms vs. inline config
```

#### Scenario 3: User Refreshes Page, Opens Account Grid

```
Timeline:
0ms     : PCF init() called
50ms    : Component mounted
52ms    : Check memory cache → MISS (page refresh cleared)
54ms    : Check localStorage → HIT ✅
56ms    : Save to memory cache (0ms)
58ms    : Register commands (20ms)
158ms   : Render grid (100ms)
───────────────────────────────────────
Total: 158ms (cached, new session)
Performance impact: +8ms vs. inline config
```

#### Scenario 4: Admin Updates Config, User Refreshes (1+ Hour Later)

```
Timeline:
0ms     : PCF init() called
50ms    : Component mounted
52ms    : Check memory cache → MISS
54ms    : Check localStorage → HIT but EXPIRED (>1 hour)
56ms    : Delete expired cache
58ms    : Start Dataverse query (fetch new config)
350ms   : Query complete, new config loaded
───────────────────────────────────────
Total: 465ms (cache expired, reload)
Result: User sees updated configuration ✅
```

---

### Cache Hit Rate Estimation

**Assumptions**:
- Average user session: 30 minutes
- User opens 5 different entity grids per session
- Grid refresh rate: 2-3 times per entity per session

**Cache Performance**:

```
Session Activity (30 minutes):

Grid Opens:
├── Account (1st time)     : 465ms (Dataverse query)
├── Account (2nd time)     : 154ms (memory cache) ✅
├── Account (3rd time)     : 154ms (memory cache) ✅
├── Contact (1st time)     : 465ms (Dataverse query)
├── Contact (2nd time)     : 154ms (memory cache) ✅
├── Opportunity (1st time) : 465ms (Dataverse query)
├── Opportunity (2nd time) : 154ms (memory cache) ✅
├── Lead (1st time)        : 465ms (Dataverse query)
├── Lead (2nd time)        : 154ms (memory cache) ✅
└── Task (1st time)        : 465ms (Dataverse query)

Total Grid Opens: 10
Dataverse Queries: 5 (first load per entity)
Cache Hits: 5 (subsequent loads)
Cache Hit Rate: 50%

Average Load Time:
(5 × 465ms + 5 × 154ms) / 10 = 309.5ms

Without Caching:
10 × 465ms = 4650ms total
465ms average

Savings: 155.5ms per load (33% faster)
Total Time Saved: 1555ms per session
```

---

### Network Payload Analysis

**Dataverse Query Payload**:

**Bad Query** (retrieves all columns):
```typescript
await context.webAPI.retrieveMultipleRecords(
  "sprk_datasetgridconfiguration",
  `?$filter=sprk_entityname eq 'account'`
);
```
**Payload**: 15-50KB (all columns, metadata)

**Optimized Query** (only needed columns):
```typescript
await context.webAPI.retrieveMultipleRecords(
  "sprk_datasetgridconfiguration",
  `?$filter=sprk_entityname eq 'account' and sprk_isactive eq true&$top=1&$select=sprk_configjson,sprk_version`
);
```
**Payload**: 2-5KB (JSON + version only)

**Impact**: 90% payload reduction → 200ms faster on slow networks

---

## Cache Invalidation Strategy

### Option 1: Time-Based Expiration (Recommended)

**Settings**:
- Cache duration: 1 hour (configurable)
- Auto-expires after duration
- Reloads fresh config automatically

**Pros**:
- ✅ Simple implementation
- ✅ No user action required
- ✅ Predictable behavior

**Cons**:
- ⚠️ Config changes take up to 1 hour to appear

**User Impact**: Low (admins can manually clear cache for immediate updates)

---

### Option 2: Version-Based Invalidation

**Algorithm**:
```typescript
// On component init
const cachedVersion = ConfigurationCacheService.getCachedVersion(entityName);
const latestVersion = await ConfigurationCacheService.getLatestVersion(context, entityName);

if (cachedVersion !== latestVersion) {
  // Cache invalid, clear and reload
  ConfigurationCacheService.clearCache(entityName);
  const config = await ConfigurationCacheService.getConfiguration(context, entityName);
}
```

**Pros**:
- ✅ Immediate invalidation (next load)
- ✅ No waiting for expiration

**Cons**:
- ⚠️ Extra query to check version (adds 50-100ms)
- ⚠️ More complex logic

**Performance**:
- Cached + version check: 2ms + 100ms = 102ms
- Still faster than full reload (200-500ms)

---

### Option 3: SignalR Push Notifications

**Architecture**:
```
Admin Updates Config
    ↓
Plugin Triggered
    ↓
Broadcast Message via SignalR
    ↓
All Connected Clients Receive Message
    ↓
Clients Clear Cache for Entity
    ↓
Next Grid Load Fetches Fresh Config
```

**Implementation**:
```typescript
// SignalR listener in PCF component
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/configurationHub")
  .build();

connection.on("ConfigurationUpdated", (entityName: string) => {
  console.log(`[ConfigCache] Received update notification for ${entityName}`);
  ConfigurationCacheService.clearCache(entityName);

  // Reload if currently displaying this entity
  if (currentEntityName === entityName) {
    reloadConfiguration();
  }
});
```

**Pros**:
- ✅ Real-time updates
- ✅ No delay
- ✅ Best user experience

**Cons**:
- ⚠️ Requires SignalR infrastructure
- ⚠️ More complex setup
- ⚠️ Fallback needed for disconnected clients

---

### Recommended Hybrid Approach

**Combine time-based + version check**:

1. **Primary**: 1-hour time-based expiration (simple, predictable)
2. **Secondary**: Version check on cache hit (catches recent updates)
3. **Manual**: Admin "Clear All Caches" button in config UI

**Algorithm**:
```typescript
public static async getConfiguration(
  context: ComponentFramework.Context<any>,
  entityName: string
): Promise<IDatasetConfig | null> {

  // Check cache
  const cached = this.getFromLocalStorage(entityName);

  if (cached) {
    // Cache hit - check if still valid
    const isExpired = this.isCacheExpired(entityName);

    if (!isExpired) {
      // Quick version check (100ms)
      const isValid = await this.isCacheValid(context, entityName);

      if (isValid) {
        return cached; // ✅ Cache valid
      }
    }

    // Cache expired or invalid - clear and reload
    this.clearCache(entityName);
  }

  // Load fresh config
  return await this.queryConfigurationTable(context, entityName);
}
```

**Performance**:
- Cache hit + valid: 2ms + 100ms = 102ms (version check)
- Cache hit + invalid: 102ms + 400ms = 502ms (version check + reload)
- Cache miss: 400ms (reload)

**Best of both worlds**: Fast when valid, accurate when changed

---

## Form Design

### Main Form: Configuration Builder

**Sections**:

#### Section 1: Basic Information
- **Name** (text, required) - "Account Main Form Configuration"
- **Entity Name** (dropdown, required) - account, contact, opportunity, etc.
- **Form Name** (text, optional) - "Main Form" (leave blank for all forms)
- **Description** (multiline, optional) - Admin notes
- **Is Active** (toggle, required) - Enable/disable this config

#### Section 2: View Settings
- **Default View Mode** (radio buttons, required)
  - ○ Grid
  - ○ List
  - ○ Card
- **Compact Toolbar** (toggle, required) - Use icon-only buttons

#### Section 3: Performance Settings
- **Enable Virtualization** (toggle, required, default: Yes)
- **Virtualization Threshold** (number, required, default: 100)
  - Help text: "Minimum number of records before virtualization activates"

#### Section 4: Features
- **Enable Keyboard Shortcuts** (toggle, required, default: Yes)
- **Enable Accessibility** (toggle, required, default: Yes)
- **Enabled Commands** (multi-select, optional)
  - ☐ Open
  - ☐ Create
  - ☐ Delete
  - ☐ Refresh

#### Section 5: Custom Commands
- **Commands** (subgrid) - Related `sprk_customcommand` records
  - Columns: Command Name, Action Type, Sort Order
  - + New Command button

#### Section 6: Generated Configuration (Read-Only)
- **Configuration JSON** (multiline, read-only, disabled)
  - Displays generated JSON (auto-updated on save)
- **Version** (number, read-only) - Auto-incremented
- **Last Updated By** (lookup, read-only)
- **Last Updated On** (datetime, read-only)

---

### Form: Custom Command

**Sections**:

#### Section 1: Command Details
- **Command Name** (text, required) - "Approve Invoice"
- **Command Key** (text, required) - "approve" (unique identifier)
- **Configuration** (lookup, required) - Parent configuration
- **Sort Order** (number, required) - Display order in toolbar

#### Section 2: Display
- **Icon** (text, optional) - "Checkmark" (Fluent UI icon name)
  - Help text: "Icon name from Fluent UI (without 'Regular' suffix)"
  - Link: [Fluent UI Icons](https://react.fluentui.dev/?path=/docs/icons-catalog--default)

#### Section 3: Action
- **Action Type** (dropdown, required)
  - Custom API
  - Action
  - Function
  - Workflow
- **Action Name** (text, required) - "sprk_ApproveInvoice"
  - Help text: "Unique name of the Custom API, Action, Function, or Workflow"

#### Section 4: Parameters
- **Parameters** (multiline JSON, optional)
  ```json
  {
    "InvoiceId": "{selectedRecordId}",
    "ApprovedBy": "{currentUserId}"
  }
  ```
  - Help text: "JSON object with parameter names and values. Use tokens: {selectedRecordId}, {selectedRecordIds}, {selectedCount}, {entityName}, {currentUserId}"
  - **Validate JSON** button (calls `sprk_ValidateConfig` API)

#### Section 5: Selection Requirements
- **Requires Selection** (toggle, required, default: No)
- **Minimum Selection** (number, optional) - Min records required
- **Maximum Selection** (number, optional) - Max records allowed

#### Section 6: Behavior
- **Confirmation Message** (text, optional) - "Approve {selectedCount} invoice(s)?"
  - Help text: "Ask user to confirm before executing. Supports tokens."
- **Refresh After Execution** (toggle, required, default: No)

---

## Web Resources

### JavaScript: ConfigJSONPreview.js

**Purpose**: Real-time JSON preview as admin configures

```javascript
// Form onLoad
function onFormLoad(executionContext) {
  const formContext = executionContext.getFormContext();

  // Register onChange handlers for all config fields
  const fields = [
    "sprk_viewmode",
    "sprk_compacttoolbar",
    "sprk_enablevirtualization",
    "sprk_virtualizationthreshold",
    "sprk_enablekeyboardshortcuts",
    "sprk_enableaccessibility",
    "sprk_enabledcommands"
  ];

  fields.forEach(field => {
    formContext.getAttribute(field).addOnChange(updateJSONPreview);
  });

  // Initial preview
  updateJSONPreview(executionContext);
}

// Update JSON preview field
async function updateJSONPreview(executionContext) {
  const formContext = executionContext.getFormContext();

  const configId = formContext.data.entity.getId().replace("{", "").replace("}", "");

  if (!configId) {
    // New record - generate preview from current form values
    const previewJSON = generatePreviewJSON(formContext);
    formContext.getAttribute("sprk_configjson").setValue(previewJSON);
  } else {
    // Existing record - call Custom API to generate JSON
    try {
      const result = await Xrm.WebApi.execute({
        customApiName: "sprk_GenerateConfigJSON",
        parameters: {
          ConfigurationId: configId
        }
      });

      const json = await result.json();
      formContext.getAttribute("sprk_configjson").setValue(json.ConfigurationJSON);
    } catch (error) {
      console.error("Error generating JSON preview:", error);
    }
  }
}

// Generate JSON from form values (for new records)
function generatePreviewJSON(formContext) {
  const config = {
    schemaVersion: "1.0",
    defaultConfig: {
      viewMode: getViewModeText(formContext.getAttribute("sprk_viewmode").getValue()),
      compactToolbar: formContext.getAttribute("sprk_compacttoolbar").getValue() || false,
      enableVirtualization: formContext.getAttribute("sprk_enablevirtualization").getValue() || true,
      virtualizationThreshold: formContext.getAttribute("sprk_virtualizationthreshold").getValue() || 100,
      enableKeyboardShortcuts: formContext.getAttribute("sprk_enablekeyboardshortcuts").getValue() || true,
      enableAccessibility: formContext.getAttribute("sprk_enableaccessibility").getValue() || true,
      enabledCommands: getEnabledCommandsText(formContext.getAttribute("sprk_enabledcommands").getValue())
    }
  };

  return JSON.stringify(config, null, 2);
}
```

---

### JavaScript: ClearCacheButton.js

**Purpose**: Ribbon button to clear cache for all users

```javascript
// Ribbon command
async function clearConfigurationCache(primaryControl) {
  const formContext = primaryControl;
  const entityName = formContext.getAttribute("sprk_entityname").getValue();

  if (!entityName) {
    Xrm.Navigation.openAlertDialog({
      text: "Please select an entity before clearing cache."
    });
    return;
  }

  const confirmation = await Xrm.Navigation.openConfirmDialog({
    text: `This will clear the browser cache for all users currently viewing ${entityName} grids. Continue?`,
    title: "Clear Configuration Cache"
  });

  if (confirmation.confirmed) {
    try {
      await Xrm.WebApi.execute({
        customApiName: "sprk_ClearConfigCache",
        parameters: {
          EntityName: entityName
        }
      });

      Xrm.Navigation.openAlertDialog({
        text: "Cache cleared successfully. Users will see updated configuration on next grid load.",
        title: "Success"
      });
    } catch (error) {
      Xrm.Navigation.openErrorDialog({
        message: `Error clearing cache: ${error.message}`
      });
    }
  }
}
```

---

## Security

### Security Roles

#### Role 1: Dataset Grid Administrator

**Privileges**:
- **sprk_datasetgridconfiguration**: Create, Read, Write, Delete, Append, Append To (Organization)
- **sprk_customcommand**: Create, Read, Write, Delete, Append, Append To (Organization)
- **sprk_configurationhistory**: Read (Organization)
- **Custom APIs**: Execute sprk_GenerateConfigJSON, sprk_ValidateConfig, sprk_ClearConfigCache

**Members**: System Administrators, Customizers

#### Role 2: Dataset Grid Viewer

**Privileges**:
- **sprk_datasetgridconfiguration**: Read (Organization)
- **sprk_customcommand**: Read (Organization)
- **sprk_configurationhistory**: Read (Organization)

**Members**: All users (for reading configs)

---

## Testing Strategy

### Unit Tests

**Test Files**:
1. `ConfigurationCacheService.test.ts`
   - Memory cache operations
   - localStorage operations
   - sessionStorage operations
   - Version checking
   - Cache invalidation
   - Preloading

**Coverage Target**: 90%

---

### Integration Tests

**Test Scenarios**:
1. Create configuration via UI → Verify JSON generated
2. Update configuration → Verify version incremented
3. Delete configuration → Verify history preserved
4. Add custom command → Verify included in JSON
5. Clear cache → Verify fresh config loaded

---

### E2E Tests (Playwright)

**Test Specifications**:

```typescript
// tests/e2e/specs/configuration-ui/admin-workflow.spec.ts

test.describe('Configuration UI - Admin Workflow @e2e', () => {
  test('should create new configuration via visual form', async ({ page }) => {
    // 1. Navigate to config manager app
    await page.goto('/apps/DatasetGridConfigManager');

    // 2. Click New
    await page.click('button:has-text("New")');

    // 3. Fill form
    await page.fill('input[name="sprk_name"]', 'Account Test Config');
    await page.selectOption('select[name="sprk_entityname"]', 'account');
    await page.click('input[name="sprk_viewmode"][value="2"]'); // Card
    await page.check('input[name="sprk_compacttoolbar"]');

    // 4. Save
    await page.click('button:has-text("Save")');

    // 5. Verify JSON generated
    const json = await page.inputValue('textarea[name="sprk_configjson"]');
    expect(json).toContain('"viewMode": "Card"');
    expect(json).toContain('"compactToolbar": true');
  });

  test('should add custom command', async ({ page }) => {
    // Open existing config
    // Add custom command via subgrid
    // Verify command appears in JSON
  });

  test('should clear cache on demand', async ({ page }) => {
    // Open config
    // Click "Clear Cache" button
    // Verify success message
  });
});
```

---

### Performance Tests

**Metrics to Track**:

| Metric | Target | Measurement |
|--------|--------|-------------|
| First load (cache miss) | <500ms | Time from init to render |
| Subsequent load (cache hit) | <10ms | Time from init to config loaded |
| Cache hit rate | >80% | Hits / Total loads |
| Configuration table query | <300ms | Dataverse query duration |
| JSON generation (plugin) | <100ms | Plugin execution time |
| Version check query | <100ms | Lightweight query duration |

**Test Script**:
```typescript
// Performance test
test('configuration loading performance', async () => {
  console.time('First Load');
  const config1 = await ConfigurationCacheService.getConfiguration(context, 'account');
  console.timeEnd('First Load'); // Target: <500ms

  console.time('Second Load (cached)');
  const config2 = await ConfigurationCacheService.getConfiguration(context, 'account');
  console.timeEnd('Second Load'); // Target: <10ms

  expect(config1).toEqual(config2);
});
```

---

## Migration & Rollout Plan

### Phase 1: Infrastructure Setup (Sprint X, Week 1)

**Tasks**:
- [ ] Create configuration tables (sprk_datasetgridconfiguration, sprk_customcommand, sprk_configurationhistory)
- [ ] Create security roles
- [ ] Deploy to Dev environment
- [ ] Test table creation

**Deliverables**:
- Solution: `SpaarkeSolution_ConfigurationUI_v1.0.0.zip`
- Tables and relationships created
- Security roles configured

---

### Phase 2: Backend Services (Sprint X, Week 2)

**Tasks**:
- [ ] Implement ConfigurationCacheService.ts
- [ ] Implement GenerateConfigurationJSON plugin
- [ ] Implement ValidateConfiguration plugin
- [ ] Implement CreateConfigurationHistory plugin
- [ ] Create Custom APIs (sprk_GenerateConfigJSON, sprk_ValidateConfig, sprk_ClearConfigCache)
- [ ] Unit tests (90% coverage target)

**Deliverables**:
- TypeScript service: `ConfigurationCacheService.ts`
- C# plugins: 3 plugin classes
- Custom APIs: 3 APIs registered
- Unit tests: 25+ tests

---

### Phase 3: Admin UI (Sprint X, Week 3)

**Tasks**:
- [ ] Design configuration form layouts
- [ ] Create main form (visual builder)
- [ ] Create custom command form
- [ ] Implement JSON preview web resource
- [ ] Implement cache management buttons
- [ ] Create model-driven app
- [ ] Create views (Active Configurations, By Entity, History)

**Deliverables**:
- Model-driven app: "Dataset Grid Configuration Manager"
- 2 main forms
- 4 views
- 2 web resources (JavaScript)

---

### Phase 4: Component Integration (Sprint X, Week 4)

**Tasks**:
- [ ] Update UniversalDatasetGrid component to use ConfigurationCacheService
- [ ] Add configuration loading logic
- [ ] Add error handling and fallbacks
- [ ] Integration tests
- [ ] E2E tests

**Deliverables**:
- Updated component: `UniversalDatasetGrid.tsx`
- Integration tests: 10+ tests
- E2E tests: 5+ scenarios

---

### Phase 5: Testing & Validation (Sprint X+1, Week 1)

**Tasks**:
- [ ] Performance testing (measure cache hit rates, load times)
- [ ] User acceptance testing (admins create configs)
- [ ] Security testing (role-based access)
- [ ] Browser compatibility testing
- [ ] Load testing (1000+ concurrent users)

**Deliverables**:
- Performance test report
- UAT sign-off
- Security audit results

---

### Phase 6: Documentation & Training (Sprint X+1, Week 2)

**Tasks**:
- [ ] Update Configuration Guide with UI screenshots
- [ ] Create admin training video (10 minutes)
- [ ] Create configuration UI user guide
- [ ] Update deployment guide
- [ ] Create troubleshooting guide for cache issues

**Deliverables**:
- Updated docs (5 files)
- Training video
- Admin quick reference

---

### Phase 7: Rollout (Sprint X+1, Week 3)

**Tasks**:
- [ ] Deploy to Test environment
- [ ] Pilot with 5 admins
- [ ] Gather feedback
- [ ] Fix issues
- [ ] Deploy to Production
- [ ] Monitor performance

**Deliverables**:
- Production deployment
- Monitoring dashboard
- Feedback summary

---

## Rollback Plan

### Scenario: Performance Issues in Production

**Symptoms**:
- Grid load times >1s consistently
- Cache not working
- Excessive Dataverse queries

**Rollback Steps**:
1. **Immediate**: Disable config table lookup via feature flag
   ```typescript
   const USE_CONFIG_TABLE = false; // Set to false
   ```
2. **Fallback**: Component uses form property or defaults
3. **Fix**: Investigate cache issues, optimize queries
4. **Re-enable**: Once fixed, set flag to true

**Rollback Time**: <1 hour

---

### Scenario: Configuration UI Bugs

**Symptoms**:
- Admins cannot create configurations
- JSON generation fails
- Forms not saving

**Rollback Steps**:
1. **Immediate**: Disable config UI app (hide from users)
2. **Temporary**: Admins use form properties (manual JSON)
3. **Fix**: Fix bugs in plugins/forms
4. **Re-enable**: Once tested, re-publish app

**Rollback Time**: <30 minutes

---

## Cost-Benefit Analysis

### Development Costs

| Phase | Hours | Developer | Cost Estimate |
|-------|-------|-----------|---------------|
| Infrastructure Setup | 4h | Mid-level | $400 |
| Backend Services | 16h | Senior | $2,400 |
| Admin UI | 12h | Mid-level | $1,200 |
| Component Integration | 8h | Senior | $1,200 |
| Testing & Validation | 12h | QA | $1,000 |
| Documentation | 8h | Technical Writer | $600 |
| Rollout | 4h | DevOps | $400 |
| **Total** | **64h** | | **$7,200** |

---

### Benefits

**Quantitative Benefits**:
- **Time Savings**: 30 min/config update → 5 min (6x faster)
  - 10 configs/month × 25 min saved × $100/hr = **$417/month**
- **Error Reduction**: 50% fewer config errors (no manual JSON)
  - 5 errors/month × 2hr debugging × $150/hr = **$1,500/month**
- **Deployment Savings**: No PCF redeployment for config changes
  - 5 deployments/month × 1hr × $150/hr = **$750/month**

**Total Monthly Savings**: $2,667
**ROI Timeline**: 3 months ($7,200 / $2,667 = 2.7 months)

**Qualitative Benefits**:
- ✅ Better admin experience (visual forms vs. JSON)
- ✅ Reduced training time for new admins
- ✅ Version history and audit trail
- ✅ Faster iteration on UX improvements
- ✅ Reduced support tickets (fewer errors)

---

## AI "Vibe Code" Instructions

### For AI Assistant to Implement This Spec

**Context**: You are implementing a visual configuration builder for the Universal Dataset Grid PCF component. The component currently requires manual JSON editing. This enhancement provides a model-driven app with visual forms that auto-generate JSON.

**Architecture**:
- **Frontend**: React TypeScript PCF component + Model-driven app (Dynamics forms)
- **Backend**: C# plugins for Dataverse + Custom APIs
- **Caching**: Multi-level (memory → localStorage → sessionStorage → Dataverse)
- **Storage**: Custom Dataverse tables (sprk_datasetgridconfiguration, sprk_customcommand)

**Key Files to Create**:

1. **TypeScript Service** (`src/shared/Spaarke.UI.Components/src/services/ConfigurationCacheService.ts`):
   - Multi-level cache (memory, localStorage, sessionStorage)
   - Dataverse query with OData filters
   - Version-based cache invalidation
   - Preloading support
   - Cache statistics

2. **C# Plugins** (`plugins/`):
   - `GenerateConfigurationJSON.cs`: Build JSON from form fields (pre-operation)
   - `ValidateConfiguration.cs`: Validate config before save (pre-operation)
   - `CreateConfigurationHistory.cs`: Version history (post-operation)

3. **Custom APIs**:
   - `sprk_GenerateConfigJSON`: Generate preview JSON
   - `sprk_ValidateConfig`: Validate without saving
   - `sprk_ClearConfigCache`: Broadcast cache clear

4. **Dataverse Schema** (Solution XML):
   - Tables: sprk_datasetgridconfiguration (17 columns), sprk_customcommand (12 columns), sprk_configurationhistory (6 columns)
   - Relationships: 1:N (config → commands), 1:N (config → history)
   - Choice sets: Entity names, View modes, Action types

5. **Model-Driven App**:
   - Main form: Visual configuration builder with 6 sections
   - Custom command form: Action definition
   - Views: Active configs, By entity, History
   - Web resources: JSON preview, cache management

6. **Web Resources** (`webresources/`):
   - `ConfigJSONPreview.js`: Real-time JSON preview
   - `ClearCacheButton.js`: Ribbon button for cache clear

**Implementation Steps**:

1. **Start with tables**: Create Dataverse schema (tables, columns, relationships)
2. **Build caching service**: Implement ConfigurationCacheService.ts with multi-level cache
3. **Create plugins**: GenerateConfigurationJSON, ValidateConfiguration, CreateConfigurationHistory
4. **Implement Custom APIs**: Register and test 3 Custom APIs
5. **Build forms**: Design configuration form with visual controls
6. **Add web resources**: JSON preview and cache management
7. **Integrate component**: Update UniversalDatasetGrid to use caching service
8. **Test thoroughly**: Unit tests (90%), integration tests, E2E tests, performance tests
9. **Document**: Update Configuration Guide with UI screenshots

**Code Style**:
- TypeScript: Strict mode, explicit types, ESLint compliant
- C#: PascalCase, XML docs, exception handling
- React: Functional components, hooks, memo optimization
- Naming: sprk_ prefix for all custom fields/tables

**Performance Targets**:
- First load: <500ms
- Cached load: <10ms
- Cache hit rate: >80%
- Dataverse query: <300ms

**Error Handling**:
- Fallback to defaults on cache failure
- Validate JSON before save
- Clear cache on schema changes
- Log all errors to console

**Testing Requirements**:
- Unit tests: 90% coverage
- Integration tests: 10+ scenarios
- E2E tests: 5+ admin workflows
- Performance tests: Measure cache hit rates

**Success Criteria**:
- Admin can create config in 5 minutes (vs. 30 minutes with JSON)
- Zero JSON editing required
- Configuration changes visible within 1 hour
- Performance impact <10ms for cached loads

**Start Command**: "Implement Phase 1: Infrastructure Setup - Create Dataverse tables and relationships"

---

## Open Questions & Decisions Needed

### Question 1: Cache Duration

**Options**:
- A) 1 hour (current spec)
- B) 4 hours (less frequent reloads)
- C) 24 hours (minimize queries)
- D) Configurable via admin setting

**Recommendation**: **A** (1 hour) - balances freshness and performance

---

### Question 2: Configuration Scope

**Options**:
- A) Per-entity only (current spec)
- B) Per-entity + per-form
- C) Per-entity + per-form + per-user
- D) Per-entity + per-form + per-security role

**Recommendation**: **B** (per-entity + per-form) - covers 90% of use cases without over-complexity

---

### Question 3: Cache Invalidation Method

**Options**:
- A) Time-based only (current spec)
- B) Time-based + version check (hybrid)
- C) SignalR push notifications
- D) Manual only (admin clears cache)

**Recommendation**: **B** (hybrid) - best balance of simplicity and accuracy

---

### Question 4: JSON Preview

**Options**:
- A) Read-only field (auto-updated on save)
- B) Live preview (updates as admin types)
- C) Preview button (click to generate)
- D) No preview (trust plugin generation)

**Recommendation**: **A** (read-only, auto-updated) - clear and non-intrusive

---

## Appendix A: Sample Configuration JSONs

### Example 1: Simple Account Config
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card",
    "compactToolbar": true,
    "enableVirtualization": true,
    "virtualizationThreshold": 100,
    "enableKeyboardShortcuts": true,
    "enableAccessibility": true,
    "enabledCommands": ["open", "create", "refresh"]
  }
}
```

### Example 2: Complex Config with Custom Commands
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "compactToolbar": false,
    "enableVirtualization": true,
    "virtualizationThreshold": 50,
    "enableKeyboardShortcuts": true,
    "enableAccessibility": true,
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "customCommands": {
      "approve": {
        "label": "Approve",
        "icon": "Checkmark",
        "actionType": "customapi",
        "actionName": "sprk_ApproveInvoice",
        "requiresSelection": true,
        "minSelection": 1,
        "maxSelection": 10,
        "parameters": {
          "InvoiceIds": "{selectedRecordIds}",
          "ApprovedBy": "{currentUserId}"
        },
        "confirmationMessage": "Approve {selectedCount} invoice(s)?",
        "refresh": true
      },
      "reject": {
        "label": "Reject",
        "icon": "Dismiss",
        "actionType": "customapi",
        "actionName": "sprk_RejectInvoice",
        "requiresSelection": true,
        "confirmationMessage": "Reject {selectedCount} invoice(s)?",
        "refresh": true
      }
    }
  }
}
```

---

## Appendix B: References

### Documentation
- [Configuration Guide](../../docs/guides/ConfigurationGuide.md)
- [Custom Commands Guide](../../docs/guides/CustomCommands.md)
- [Developer Guide](../../docs/guides/DeveloperGuide.md)
- [API Reference](../../docs/api/UniversalDatasetGrid.md)

### External Resources
- [Power Apps Component Framework](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Dataverse Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [Model-Driven Apps](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/)
- [Custom APIs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Fluent UI v9](https://react.fluentui.dev/)

---

## Document Metadata

**Created**: 2025-10-03
**Author**: Spaarke Engineering Team
**Status**: 📋 SPECIFICATION (Not Implemented)
**Version**: 1.0
**Target Implementation**: TBD (Post-v1.0.0)
**Estimated Effort**: 64 hours
**Estimated Cost**: $7,200
**ROI Timeline**: 3 months
**Priority**: Medium

---

**END OF SPECIFICATION**
