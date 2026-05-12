/**
 * CreateRecordStep.tsx
 * Step 2 of the "Create New Matter" wizard -- 2-column form with lookup fields.
 *
 * Layout (CSS Grid):
 *   +---------------------------+------------------------------+
 *   |  Matter Type (lookup)     |  Practice Area (lookup)       |
 *   +---------------------------+------------------------------+
 *   |  Matter Name (Input, full-width) *                       |
 *   +---------------------------+------------------------------+
 *   |  Assigned Attorney (lookup)|  Assigned Paralegal (lookup) |
 *   +---------------------------+------------------------------+
 *   |  Summary (Textarea, full-width) + "Generate with AI"     |
 *   +----------------------------------------------------------+
 *
 * Lookup fields use LookupField component with debounced Dataverse search.
 * Summary has an AI generate button that calls BFF endpoint.
 *
 * Form validation:
 *   Required: Matter Type, Practice Area, Matter Name
 *   -> `onValidChange(true)` emitted when all three have values
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Label, Skeleton, Button, Spinner
 *   - makeStyles with semantic tokens -- ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */
import * as React from 'react';
import { ICreateRecordStepProps } from './formTypes';
export declare const CreateRecordStep: React.FC<ICreateRecordStepProps>;
export { CreateRecordStep as default };
//# sourceMappingURL=CreateRecordStep.d.ts.map