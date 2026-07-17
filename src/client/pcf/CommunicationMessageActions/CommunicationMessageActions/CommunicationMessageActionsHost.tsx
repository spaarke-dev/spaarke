/**
 * CommunicationMessageActionsHost — top-level React wrapper for the virtual PCF.
 * Theme + read-only resolution + FluentProvider (mirrors CommunicationActionsHost / RegardingResolverHost).
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationMessageActionsApp } from './CommunicationMessageActionsApp';

export interface ICommunicationMessageActionsHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

function resolveReadOnly(context: ComponentFramework.Context<IInputs>): boolean {
  return Boolean((context.mode as { isControlDisabled?: boolean }).isControlDisabled);
}

export const CommunicationMessageActionsHost: React.FC<ICommunicationMessageActionsHostProps> = ({
  context,
  version,
}) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);
  const readOnly = useMemo(() => resolveReadOnly(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationMessageActionsApp context={context} readOnly={readOnly} version={version} />
    </FluentProvider>
  );
};
