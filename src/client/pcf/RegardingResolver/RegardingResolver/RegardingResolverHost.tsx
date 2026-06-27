/**
 * RegardingResolverHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - Theme resolution (memoized off context, ADR-021)
 *  - Read-only mode resolution (manifest `readOnly` prop OR `mode.isControlDisabled`)
 *  - FluentProvider + RegardingResolverApp composition
 *
 * Per ADR-022: virtual controls return React elements from updateView()
 * rather than rendering imperatively. The host component is the place where
 * async work (effects), context-derived state (memos), and the Fluent theme
 * boundary live.
 */

import * as React from 'react';
import { useMemo } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { resolveThemeWithUserPreference } from '@spaarke/ui-components/dist/utils/themeStorage';
import { IInputs } from './generated/ManifestTypes';
import { RegardingResolverApp } from './RegardingResolverApp';

export interface IRegardingResolverHostProps {
  context: ComponentFramework.Context<IInputs>;
  /** Called when the user selects (or clears) a regarding target. */
  onRecordTypeChanged: (value: ComponentFramework.LookupValue | null) => void;
  version: string;
}

/**
 * Resolves the read-only mode for the picker (FR-24).
 *
 * Returns `true` when EITHER:
 *   1. The maker explicitly set the `readOnly` manifest property, OR
 *   2. The host form / control is in non-edit mode (Dataverse signals this
 *      via `context.mode.isControlDisabled` on read-only forms or for users
 *      whose role lacks Write privilege on the field).
 */
function resolveReadOnly(context: ComponentFramework.Context<IInputs>): boolean {
  const explicit = context.parameters.readOnly?.raw === true;
  // mode.isControlDisabled is `true` on read-only forms and when the user
  // lacks edit permissions per Dataverse FLS. Cast to satisfy older typings.
  const inheritedDisabled = Boolean((context.mode as { isControlDisabled?: boolean }).isControlDisabled);
  return explicit || inheritedDisabled;
}

export const RegardingResolverHost: React.FC<IRegardingResolverHostProps> = ({
  context,
  onRecordTypeChanged,
  version,
}) => {
  const theme = useMemo(() => resolveThemeWithUserPreference(context), [context]);
  const readOnly = useMemo(() => resolveReadOnly(context), [context]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%', width: '100%' }}>
      <RegardingResolverApp
        context={context}
        readOnly={readOnly}
        onRecordTypeChanged={onRecordTypeChanged}
        version={version}
      />
    </FluentProvider>
  );
};
