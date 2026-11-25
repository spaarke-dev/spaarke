/**
 * DataverseWebApiService — the Dataverse org URL for this code page's direct Web API reads.
 *
 * The filter-option reads that used to live here moved to `@spaarke/ui-components`
 * (`SearchFilters/searchFilterOptions`) in #3131, shared with the Spaarke Console.
 */

import { getDataverseOrgUrl } from '@spaarke/ui-components/components/SearchFilters';

/** The Dataverse org URL from Xrm, or this page's origin outside a Dataverse host. */
export function getOrgUrl(): string {
  return getDataverseOrgUrl();
}
