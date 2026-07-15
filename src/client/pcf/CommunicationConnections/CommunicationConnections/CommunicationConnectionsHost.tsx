/**
 * CommunicationConnectionsHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that a virtual control returns from updateView():
 *  - Theme resolution (memoized off context, ADR-021)
 *  - Read-only mode resolution (manifest `readOnly` prop OR `mode.isControlDisabled`)
 *  - FluentProvider + CommunicationConnectionsApp composition
 *
 * Mirrors RegardingResolverHost (the proven pattern on this same OOB form).
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationConnectionsApp } from './CommunicationConnectionsApp';

export interface ICommunicationConnectionsHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

/**
 * Resolve read-only mode: explicit `readOnly` manifest prop OR the host form /
 * control being non-editable (`context.mode.isControlDisabled` — true on
 * read-only forms and for users lacking Write privilege).
 */
function resolveReadOnly(context: ComponentFramework.Context<IInputs>): boolean {
  const explicit = context.parameters.readOnly?.raw === true;
  const inheritedDisabled = Boolean((context.mode as { isControlDisabled?: boolean }).isControlDisabled);
  return explicit || inheritedDisabled;
}

export const CommunicationConnectionsHost: React.FC<ICommunicationConnectionsHostProps> = ({ context, version }) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);
  const readOnly = useMemo(() => resolveReadOnly(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationConnectionsApp context={context} readOnly={readOnly} version={version} />
    </FluentProvider>
  );
};
