/**
 * LookupField.tsx
 * Thin wrapper around the shared @spaarke/ui-components LookupField.
 *
 * Maps the legacy `isAiPrefilled` prop to the shared component's
 * `labelExtra` slot, rendering the workspace-specific AiFieldTag.
 */
import * as React from 'react';
import { type ILookupFieldProps as ISharedLookupFieldProps } from '../LookupField';
export type { ILookupItem } from '../../types/LookupTypes';
export interface ILookupFieldProps extends Omit<ISharedLookupFieldProps, 'labelExtra'> {
    /** Whether this field was AI-pre-filled (shows AiFieldTag badge). */
    isAiPrefilled?: boolean;
}
export declare const LookupField: React.FC<ILookupFieldProps>;
//# sourceMappingURL=LookupField.d.ts.map