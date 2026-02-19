/**
 * useDataverseService — React hook factory for DataverseService.
 *
 * Provides a stable DataverseService instance created from Xrm.WebApi,
 * enabling dependency injection into React components without threading
 * the raw WebApi reference through every prop chain.
 *
 * Usage:
 *   const service = useDataverseService(webApi);
 *   const result = await service.getMattersByUser(userId);
 */

import { useMemo } from 'react';
import { DataverseService } from '../services/DataverseService';
import type { IWebApi } from '../types/xrm';

/**
 * Create and memoize a DataverseService from an Xrm.WebApi instance.
 *
 * @param webApi - The Xrm.WebApi reference from xrmProvider.getWebApi()
 * @returns      A stable DataverseService instance
 */
export function useDataverseService(
  webApi: IWebApi
): DataverseService {
  return useMemo(() => new DataverseService(webApi), [webApi]);
}
