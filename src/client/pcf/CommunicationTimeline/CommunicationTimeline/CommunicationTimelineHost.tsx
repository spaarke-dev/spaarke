/**
 * CommunicationTimelineHost — top-level React wrapper for the virtual PCF.
 * Theme resolution + FluentProvider (mirrors CommunicationMessageActionsHost).
 *
 * No read-only resolution here (deviation from CommunicationMessageActionsHost):
 * the shared `<CommunicationTimeline/>` component has no `disabled`/`readOnly`
 * prop to thread through — see CommunicationTimelineApp.tsx file header.
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationTimelineApp } from './CommunicationTimelineApp';

export interface ICommunicationTimelineHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationTimelineHost: React.FC<ICommunicationTimelineHostProps> = ({ context, version }) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationTimelineApp context={context} version={version} />
    </FluentProvider>
  );
};
