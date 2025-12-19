# Dataverse Queries Pattern

> **Domain**: PCF / WebAPI & Environment Variables
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-006, ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/shared/utils/environmentVariables.ts` | Environment variable access |
| `src/client/pcf/UniversalDatasetGrid/control/services/SdapApiClient.ts` | API client pattern |
| `src/client/pcf/UniversalQuickCreate/control/services/MetadataService.ts` | Metadata caching |

---

## Environment Variable Access

### Known Variables
```typescript
export type KnownEnvironmentVariable =
    | "sprk_BffApiBaseUrl"
    | "sprk_AzureOpenAiEndpoint"
    | "sprk_ApplicationInsightsKey"
    | "sprk_ContainerId";
```

### Single Variable Retrieval
```typescript
export async function getEnvironmentVariable(
    webApi: ComponentFramework.WebApi,
    variableName: KnownEnvironmentVariable
): Promise<string | undefined> {
    // Check cache first
    if (cache.has(variableName)) return cache.get(variableName);

    const result = await webApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        `?$filter=schemaname eq '${variableName}'` +
        `&$expand=environmentvariablevalue($select=value)`
    );

    const value = result.entities[0]?.environmentvariablevalue?.[0]?.value
        ?? result.entities[0]?.defaultvalue;

    cache.set(variableName, value, CACHE_TTL);
    return value;
}
```

### Bulk Configuration Load
```typescript
export async function loadSpaarkeConfiguration(
    webApi: ComponentFramework.WebApi
): Promise<SpaarkeConfig> {
    const [apiBaseUrl, openAiEndpoint, appInsightsKey, containerId] =
        await Promise.all([
            getEnvironmentVariable(webApi, "sprk_BffApiBaseUrl"),
            getEnvironmentVariable(webApi, "sprk_AzureOpenAiEndpoint"),
            getEnvironmentVariable(webApi, "sprk_ApplicationInsightsKey"),
            getEnvironmentVariable(webApi, "sprk_ContainerId")
        ]);

    return { apiBaseUrl, openAiEndpoint, appInsightsKey, containerId };
}
```

---

## Cache Implementation

```typescript
const CACHE_TTL = 5 * 60 * 1000; // 5 minutes

class EnvironmentVariableCache {
    private cache = new Map<string, { value: string; expiry: number }>();

    get(key: string): string | undefined {
        const entry = this.cache.get(key);
        if (!entry || Date.now() > entry.expiry) return undefined;
        return entry.value;
    }

    set(key: string, value: string, ttl: number): void {
        this.cache.set(key, { value, expiry: Date.now() + ttl });
    }

    clear(): void {
        this.cache.clear();
    }
}
```

---

## WebAPI Query Patterns

### Retrieve Single Record
```typescript
const record = await context.webAPI.retrieveRecord(
    "account",
    recordId,
    "?$select=name,accountnumber"
);
```

### Retrieve Multiple with Filter
```typescript
const result = await context.webAPI.retrieveMultipleRecords(
    "contact",
    `?$filter=parentcustomerid eq ${accountId}` +
    `&$select=fullname,emailaddress1` +
    `&$orderby=fullname asc` +
    `&$top=50`
);
const contacts = result.entities;
```

### Create Record
```typescript
const newRecord = await context.webAPI.createRecord("task", {
    subject: "Follow up",
    description: "Follow up with customer",
    "regardingobjectid_account@odata.bind": `/accounts(${accountId})`
});
const newId = newRecord.id;
```

### Update Record
```typescript
await context.webAPI.updateRecord("account", recordId, {
    name: "Updated Name",
    description: "Updated description"
});
```

### Delete Record
```typescript
await context.webAPI.deleteRecord("account", recordId);
```

---

## Dataset Access

For dataset-bound PCF controls:

```typescript
const dataset = context.parameters.myDataset;

// Get all record IDs
const recordIds = dataset.sortedRecordIds;

// Iterate records
for (const id of recordIds) {
    const record = dataset.records[id];
    const name = record.getFormattedValue("name");
    const rawValue = record.getValue("accountnumber");
}

// Paging
if (dataset.paging.hasNextPage) {
    dataset.paging.loadNextPage();
}

// Refresh
dataset.refresh();
```

---

## API Client Factory Pattern

```typescript
export class SdapApiClientFactory {
    private static instance: SdapApiClient | null = null;

    static async create(
        webApi: ComponentFramework.WebApi
    ): Promise<SdapApiClient> {
        if (this.instance) return this.instance;

        const baseUrl = await getEnvironmentVariable(webApi, "sprk_BffApiBaseUrl");
        this.instance = new SdapApiClient(baseUrl || FALLBACK_URL);
        return this.instance;
    }
}
```

---

## Related Patterns

- [Control Initialization](control-initialization.md) - Context access
- [PCF Constraints](../../constraints/pcf.md) - Caching requirements

---

**Lines**: ~125
