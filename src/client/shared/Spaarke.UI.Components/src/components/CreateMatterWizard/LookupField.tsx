/**
 * LookupField.tsx
 * Thin wrapper around the shared @spaarke/ui-components LookupField.
 *
 * Maps the legacy `isAiPrefilled` prop to the shared component's
 * `labelExtra` slot, rendering the workspace-specific AiFieldTag.
 */

import * as React from 'react';
import {
  LookupField as SharedLookupField,
  type ILookupFieldProps as ISharedLookupFieldProps,
} from '../LookupField';
import { AiFieldTag } from './AiFieldTag';

// Re-export the shared type for convenience
export type { ILookupItem } from '../../types/LookupTypes';

export interface ILookupFieldProps extends Omit<ISharedLookupFieldProps, 'labelExtra'> {
  /** Whether this field was AI-pre-filled (shows AiFieldTag badge). */
  isAiPrefilled?: boolean;
}

export const LookupField: React.FC<ILookupFieldProps> = ({
  isAiPrefilled,
  ...rest
}) => {
  return (
    <SharedLookupField
      {...rest}
      labelExtra={isAiPrefilled ? <AiFieldTag /> : undefined}
    />
  );
};

LookupField.displayName = 'LookupField';
