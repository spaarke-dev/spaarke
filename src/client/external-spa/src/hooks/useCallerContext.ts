/**
 * useCallerContext - the signed-in caller's `/me` context, read on demand (spaarke-SPA-external-access-platform-r3
 * task 054, FR-24 / FR-26).
 *
 * The record detail view needs two facts the lists do not carry: WHO the caller is (to offer Delete only on rows they
 * created) and, for a project, THEIR LEVEL (to offer Upload only with Create). Both come from `GET /api/v1/external/me`
 * through `getExternalUserContext` (`bffApiCall`: active backend, active token acquirer).
 *
 * `enabled` defers the read until the view has something to show (the modal is mounted while closed). A failed read
 * leaves `context` null: Delete and a project's Upload then stay hidden (the safe default), never shown on a guess; the
 * server re-checks every upload and delete either way. Unlike `useExternalContext`, a failure here is silent: the detail
 * view works without it.
 */
import { useEffect, useState } from 'react';
import { getExternalUserContext } from '../auth/bff-client';
import type { ExternalUserContextResponse } from '../auth/bff-client';

export function useCallerContext(enabled: boolean): ExternalUserContextResponse | null {
  const [context, setContext] = useState<ExternalUserContextResponse | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    getExternalUserContext().then(
      data => {
        if (!cancelled) setContext(data);
      },
      () => {
        if (!cancelled) setContext(null);
      }
    );
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return context;
}
