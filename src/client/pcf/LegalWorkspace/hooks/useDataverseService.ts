/**
 * useDataverseService — React hook factory for DataverseService.
 *
 * Provides a stable DataverseService instance created from the PCF framework
 * context, enabling dependency injection into React components without
 * threading the raw WebApi reference through every prop chain.
 *
 * Usage in a component:
 *   const service = useDataverseService(context);
 *   const result = await service.getMattersByUser(userId);
 *
 * Usage pattern when the PCF context is passed from LegalWorkspaceApp:
 *   // In LegalWorkspaceApp.tsx or a block component:
 *   const service = useDataverseService(props.context);
 *
 * The service instance is memoized — it is only recreated when the webAPI
 * reference changes (i.e. PCF updateView with a new context object).
 */

import { useMemo } from 'react';
import { DataverseService } from '../services/DataverseService';

/**
 * Create and memoize a DataverseService from a PCF ComponentFramework.WebApi.
 *
 * @param webApi - The webAPI from the PCF context: context.webAPI
 * @returns      A stable DataverseService instance
 */
export function useDataverseService(
  webApi: ComponentFramework.WebApi
): DataverseService {
  return useMemo(() => new DataverseService(webApi), [webApi]);
}

/**
 * Overload that accepts the full PCF context object.
 * Extracts webAPI internally so callers don't need to drill into context.
 *
 * @param context - Full PCF ComponentFramework.Context<TInputs>
 * @returns       A stable DataverseService instance
 */
export function useDataverseServiceFromContext<TInputs extends Record<string, unknown>>(
  context: ComponentFramework.Context<TInputs>
): DataverseService {
  return useDataverseService(context.webAPI);
}
