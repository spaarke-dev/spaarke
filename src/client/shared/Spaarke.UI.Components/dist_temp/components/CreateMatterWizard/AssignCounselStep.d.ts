/**
 * AssignCounselStep.tsx
 * Follow-on step for "Assign Counsel" in the Create New Matter wizard.
 *
 * Uses IDataService (via searchContacts from matterService) to query
 * contact records filtered by name. Minimum 2 characters required
 * before a search fires. Results debounced 400ms.
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner, MessageBar
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import { type IContact } from './matterService';
import type { IDataService } from '../../types/serviceInterfaces';
export interface IAssignCounselStepProps {
    /** IDataService for Dataverse queries. */
    dataService: IDataService;
    /** Currently selected contact (or null). */
    selectedContact: IContact | null;
    /** Called when the user selects or clears a contact. */
    onContactChange: (contact: IContact | null) => void;
}
export declare const AssignCounselStep: React.FC<IAssignCounselStepProps>;
//# sourceMappingURL=AssignCounselStep.d.ts.map