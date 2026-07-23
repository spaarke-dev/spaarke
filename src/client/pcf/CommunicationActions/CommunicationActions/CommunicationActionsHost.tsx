/**
 * CommunicationActionsHost — top-level React wrapper for the virtual PCF.
 * Theme + read-only resolution + FluentProvider (mirrors RegardingResolverHost).
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationActionsApp } from './CommunicationActionsApp';

export interface ICommunicationActionsHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

function resolveReadOnly(context: ComponentFramework.Context<IInputs>): boolean {
  return Boolean((context.mode as { isControlDisabled?: boolean }).isControlDisabled);
}

export const CommunicationActionsHost: React.FC<ICommunicationActionsHostProps> = ({ context, version }) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);
  const readOnly = useMemo(() => resolveReadOnly(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationActionsApp context={context} readOnly={readOnly} version={version} />
    </FluentProvider>
  );
};
