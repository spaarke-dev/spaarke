/**
 * searchFilterOptions — the option lists behind the semantic-search filters (Document Type, File
 * Type, Matter Type), read from Dataverse.
 *
 * Shared by the SemanticSearch code page and the Spaarke Console's Semantic Search criteria
 * (#3131), so both offer the same values and a value chosen in the Console means the same thing
 * on the search page it opens. Moved here from the code page's `DataverseWebApiService`.
 *
 * - Document Type: the `sprk_document.sprk_documenttype` choice (value = option number).
 * - Matter Type: active `sprk_mattertype_ref` records (value = record id).
 * - File Type: a fixed list of extensions (not stored in Dataverse).
 *
 * Reads go straight to the Dataverse Web API with `fetch`: `Xrm.WebApi` cannot read metadata
 * (`EntityDefinitions`). A list that loaded is cached per page for the session. A missing column
 * or table (404) is an empty list; any other failure throws, and `loadSearchFilterOptions` names
 * the lists that failed so the host can say so instead of showing an empty "All".
 */

import { getClientUrl } from '../../utils/xrmContext';
import type { FilterDropdownOption } from './FilterDropdown';

/** A list read from Dataverse. */
export type SearchFilterOptionList = 'documentTypes' | 'matterTypes';

/** The three option lists, and which Dataverse reads failed (their lists are empty). */
export interface SearchFilterOptions {
  documentTypes: FilterDropdownOption[];
  fileTypes: FilterDropdownOption[];
  matterTypes: FilterDropdownOption[];
  failed: SearchFilterOptionList[];
}

/** File types the search index records (extensions, lower case). */
export const SEARCH_FILE_TYPE_OPTIONS: readonly FilterDropdownOption[] = [
  { value: 'pdf', label: 'PDF' },
  { value: 'doc', label: 'Word (DOC)' },
  { value: 'docx', label: 'Word (DOCX)' },
  { value: 'xls', label: 'Excel (XLS)' },
  { value: 'xlsx', label: 'Excel (XLSX)' },
  { value: 'ppt', label: 'PowerPoint (PPT)' },
  { value: 'pptx', label: 'PowerPoint (PPTX)' },
  { value: 'txt', label: 'Text (TXT)' },
  { value: 'msg', label: 'Email (MSG)' },
  { value: 'eml', label: 'Email (EML)' },
];

const cache = new Map<string, FilterDropdownOption[]>();

const ODATA_HEADERS: HeadersInit = {
  Accept: 'application/json',
  'OData-MaxVersion': '4.0',
  'OData-Version': '4.0',
};

interface OptionMetadata {
  Value: number;
  Label?: { UserLocalizedLabel?: { Label?: string } };
}

/** The Dataverse org URL from Xrm, or this page's origin outside a Dataverse host. */
function orgUrl(): string {
  return getClientUrl() ?? window.location.origin;
}

/** The options of a choice (picklist) column, sorted by label. Throws on a failed read (not 404). */
export async function fetchOptionsetOptions(
  entityName: string,
  attributeName: string
): Promise<FilterDropdownOption[]> {
  const cacheKey = `optionset:${entityName}.${attributeName}`;
  const cached = cache.get(cacheKey);
  if (cached) return cached;

  const url =
    `${orgUrl()}/api/data/v9.2/EntityDefinitions(LogicalName='${entityName}')` +
    `/Attributes(LogicalName='${attributeName}')` +
    `/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$expand=OptionSet($select=Options)`;
  const response = await fetch(url, { headers: ODATA_HEADERS });
  if (response.status === 404) return [];
  if (!response.ok) {
    throw new Error(`Failed to fetch optionset ${entityName}.${attributeName}: ${response.status}`);
  }
  const data = await response.json();
  const rawOptions: OptionMetadata[] = data.OptionSet?.Options ?? data.Options ?? [];
  const options = rawOptions
    .map(opt => ({
      value: opt.Value.toString(),
      label: opt.Label?.UserLocalizedLabel?.Label ?? `Value ${opt.Value}`,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  cache.set(cacheKey, options);
  return options;
}

/** Active records of a lookup table as options (value = record id), ordered by name. Throws on a failed read (not 404). */
export async function fetchLookupOptions(
  entitySetName: string,
  nameAttribute: string
): Promise<FilterDropdownOption[]> {
  const cacheKey = `lookup:${entitySetName}`;
  const cached = cache.get(cacheKey);
  if (cached) return cached;

  const entityLogicalName = entitySetName.endsWith('s') ? entitySetName.slice(0, -1) : entitySetName;
  const primaryKey = `${entityLogicalName}id`;
  const url =
    `${orgUrl()}/api/data/v9.2/${entitySetName}` +
    `?$select=${primaryKey},${nameAttribute}` +
    `&$filter=statecode eq 0` +
    `&$orderby=${nameAttribute} asc`;
  const response = await fetch(url, { headers: ODATA_HEADERS });
  if (response.status === 404) return [];
  if (!response.ok) {
    throw new Error(`Failed to fetch lookup options from ${entitySetName}: ${response.status}`);
  }
  const data = await response.json();
  const options: FilterDropdownOption[] = ((data.value ?? []) as Record<string, unknown>[]).map(record => ({
    value: typeof record[primaryKey] === 'string' ? (record[primaryKey] as string) : '',
    label: typeof record[nameAttribute] === 'string' ? (record[nameAttribute] as string) : 'Unknown',
  }));
  cache.set(cacheKey, options);
  return options;
}

/**
 * All three option lists. The two Dataverse reads run in parallel and independently: a list that
 * fails is empty and named in `failed` (and logged); the other still loads. Never rejects.
 */
export async function loadSearchFilterOptions(): Promise<SearchFilterOptions> {
  const [documentTypes, matterTypes] = await Promise.allSettled([
    fetchOptionsetOptions('sprk_document', 'sprk_documenttype'),
    fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename'),
  ]);
  const failed: SearchFilterOptionList[] = [];
  const valueOf = (
    result: PromiseSettledResult<FilterDropdownOption[]>,
    list: SearchFilterOptionList
  ): FilterDropdownOption[] => {
    if (result.status === 'fulfilled') return result.value;
    console.error(`Failed to load the ${list} filter options:`, result.reason);
    failed.push(list);
    return [];
  };
  return {
    documentTypes: valueOf(documentTypes, 'documentTypes'),
    fileTypes: [...SEARCH_FILE_TYPE_OPTIONS],
    matterTypes: valueOf(matterTypes, 'matterTypes'),
    failed,
  };
}

/** Forget cached options (the next load reads Dataverse again). */
export function clearSearchFilterOptionsCache(): void {
  cache.clear();
}
