/**
 * LookupField.tsx
 * Thin wrapper around the shared @spaarke/ui-components LookupField.
 *
 * Maps the legacy `isAiPrefilled` prop to the shared component's
 * `labelExtra` slot, rendering the workspace-specific AiFieldTag.
 */
import * as React from 'react';
import { LookupField as SharedLookupField, } from '../LookupField';
import { AiFieldTag } from './AiFieldTag';
export const LookupField = ({ isAiPrefilled, ...rest }) => {
    return (React.createElement(SharedLookupField, { ...rest, labelExtra: isAiPrefilled ? React.createElement(AiFieldTag, null) : undefined }));
};
LookupField.displayName = 'LookupField';
//# sourceMappingURL=LookupField.js.map