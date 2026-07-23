/**
 * CommunicationConversationPanelHost — top-level React wrapper for the virtual
 * PCF. Theme resolution + FluentProvider (mirrors the sibling
 * `CommunicationTimelineRegardingHost`, task 021). The host `FluentProvider`
 * owns light/dark (ADR-021, NFR-04) — dark mode passes through to the preview,
 * every `<MessageQuickView/>` popover, and the conversation modal (all Fluent v9
 * semantic tokens, no hardcoded colors).
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationConversationPanelApp } from './CommunicationConversationPanelApp';

export interface ICommunicationConversationPanelHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationConversationPanelHost: React.FC<ICommunicationConversationPanelHostProps> = ({
  context,
  version,
}) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationConversationPanelApp context={context} version={version} />
    </FluentProvider>
  );
};
