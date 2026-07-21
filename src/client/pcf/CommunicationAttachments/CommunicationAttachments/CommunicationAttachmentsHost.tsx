/**
 * CommunicationAttachmentsHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that a virtual control returns from updateView():
 *  - Theme resolution (memoized off context, ADR-021)
 *  - FluentProvider + CommunicationAttachmentsApp composition
 *
 * Mirrors CommunicationConnectionsHost (the proven pattern on this same OOB form).
 * Note: this is a read-only preview/open viewer with no mutating actions, so
 * there is no read-only gating — attachments open regardless of form lock state.
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { CommunicationAttachmentsApp } from './CommunicationAttachmentsApp';

export interface ICommunicationAttachmentsHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationAttachmentsHost: React.FC<ICommunicationAttachmentsHostProps> = ({ context, version }) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <CommunicationAttachmentsApp context={context} version={version} />
    </FluentProvider>
  );
};
