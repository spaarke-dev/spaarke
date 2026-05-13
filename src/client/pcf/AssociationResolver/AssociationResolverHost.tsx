/**
 * AssociationResolverHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - Async BFF API base URL resolution from Dataverse env var (useEffect)
 *  - Theme resolution (memoized off context)
 *  - FluentProvider + AssociationResolverApp composition
 *
 * Per ADR-022: virtual controls return React elements from updateView()
 * rather than rendering imperatively. Async work that needs to flip the UI
 * into a render state lives in useState/useEffect inside React, not the
 * PCF class.
 */

import * as React from 'react';
import { useEffect, useState, useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { AssociationResolverApp } from './AssociationResolverApp';
import { getApiBaseUrl } from '../shared/utils/environmentVariables';

export interface IAssociationResolverHostProps {
  context: ComponentFramework.Context<IInputs>;
  onRecordSelected: (recordId: string, recordName: string) => void;
  version: string;
}

interface RecordTypeReference {
  id: string;
  name: string;
  entityLogicalName?: string;
}

function getRecordTypeReference(
  context: ComponentFramework.Context<IInputs>
): RecordTypeReference | null {
  const rawValue = context.parameters.regardingRecordType?.raw;
  if (!rawValue) return null;
  const ref = Array.isArray(rawValue) ? rawValue[0] : rawValue;
  if (!ref || !ref.id) return null;
  return {
    id: ref.id,
    name: ref.name || '',
    entityLogicalName: ref.entityType || 'sprk_recordtype_ref',
  };
}

export const AssociationResolverHost: React.FC<IAssociationResolverHostProps> = ({
  context,
  onRecordSelected,
  version,
}) => {
  // Resolve BFF API base URL from Dataverse env var. Falls back to manifest
  // input property if the env var query fails. No hardcoded dev URLs.
  const manifestApiBaseUrl = context.parameters.apiBaseUrl?.raw || '';
  const [resolvedApiBaseUrl, setResolvedApiBaseUrl] = useState(manifestApiBaseUrl);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const url = await getApiBaseUrl(context.webAPI);
        if (!cancelled && url) setResolvedApiBaseUrl(url);
      } catch {
        if (!cancelled && !manifestApiBaseUrl) {
          console.error(
            '[AssociationResolver] BFF API base URL not configured. ' +
              'Set sprk_BffApiBaseUrl env var or configure the apiBaseUrl property.'
          );
        }
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);
  const regardingRecordType = useMemo(() => getRecordTypeReference(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <AssociationResolverApp
        context={context}
        regardingRecordType={regardingRecordType}
        apiBaseUrl={resolvedApiBaseUrl}
        onRecordSelected={onRecordSelected}
        version={version}
      />
    </FluentProvider>
  );
};
