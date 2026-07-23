/**
 * CommunicationTimelineRegardingHost — top-level React wrapper for the
 * virtual PCF. Theme resolution + FluentProvider (mirrors the sibling
 * `CommunicationTimelineHost`, task 021).
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationTimelineRegardingApp } from './CommunicationTimelineRegardingApp';

export interface ICommunicationTimelineRegardingHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationTimelineRegardingHost: React.FC<ICommunicationTimelineRegardingHostProps> = ({
  context,
  version,
}) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationTimelineRegardingApp context={context} version={version} />
    </FluentProvider>
  );
};
