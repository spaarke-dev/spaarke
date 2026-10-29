/**
 * `useViewerRecordNames` — React binding for the page-session viewer name resolver
 * (`getViewerRecordNameResolver`). Resolves the given record refs as the viewer and returns a
 * `ViewerTargetName` lookup for the provenance reducers; the lookup's identity changes whenever new
 * names arrive, so memoized derivations recompute. With no `webApi`, every lookup is `undefined`
 * (no candidate cards — fail closed).
 */
import * as React from 'react';
import type { IPolymorphicWebApi } from '@spaarke/ui-components';
import {
  getViewerRecordNameResolver,
  type ViewerRecordNameResolver,
  type ViewerRecordRef,
  type ViewerTargetName,
} from '../logic/connections';

function lookupOf(resolver: ViewerRecordNameResolver | undefined): ViewerTargetName {
  return (entity, id) => resolver?.nameOf(entity, id);
}

export function useViewerRecordNames(
  webApi: IPolymorphicWebApi | undefined,
  refs: readonly ViewerRecordRef[]
): ViewerTargetName {
  const resolver = React.useMemo(() => (webApi ? getViewerRecordNameResolver(webApi) : undefined), [webApi]);
  const [viewerName, setViewerName] = React.useState<ViewerTargetName>(() => lookupOf(resolver));

  React.useEffect(() => {
    setViewerName(() => lookupOf(resolver));
    if (!resolver) return undefined;
    return resolver.subscribe(() => setViewerName(() => lookupOf(resolver)));
  }, [resolver]);

  // Re-resolve only when the SET of refs changes (callers usually build a fresh array each render).
  const refKey = refs.map(r => `${r.entity ?? ''}:${r.id ?? ''}`).join('|');
  const latestRefs = React.useRef(refs);
  latestRefs.current = refs;
  React.useEffect(() => {
    if (resolver && latestRefs.current.length > 0) void resolver.resolve(latestRefs.current);
  }, [resolver, refKey]);

  return viewerName;
}
