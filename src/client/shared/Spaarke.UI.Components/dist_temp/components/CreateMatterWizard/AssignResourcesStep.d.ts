/**
 * AssignResourcesStep.tsx
 * Follow-on step for "Assign Resources" in the Create New Matter wizard.
 *
 * Uses LookupField for each assignment. Values are lifted to WizardDialog
 * form state (AI pre-fill populates these from CreateRecordStep).
 *
 * Constraints:
 *   - Fluent v9: Text, Checkbox -- ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */
import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
export interface IAssignResourcesStepProps {
    /** Assigned Attorney lookup value. */
    attorneyValue: ILookupItem | null;
    /** Called when attorney changes. */
    onAttorneyChange: (item: ILookupItem | null) => void;
    /** Search function for attorney (contacts). */
    onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;
    /** Whether attorney was AI pre-filled. */
    isAttorneyAiPrefilled?: boolean;
    /** Assigned Paralegal lookup value. */
    paralegalValue: ILookupItem | null;
    /** Called when paralegal changes. */
    onParalegalChange: (item: ILookupItem | null) => void;
    /** Search function for paralegal (contacts). */
    onSearchParalegals: (query: string) => Promise<ILookupItem[]>;
    /** Whether paralegal was AI pre-filled. */
    isParalegalAiPrefilled?: boolean;
    /** Assigned Outside Counsel lookup value. */
    outsideCounselValue: ILookupItem | null;
    /** Called when outside counsel changes. */
    onOutsideCounselChange: (item: ILookupItem | null) => void;
    /** Search function for outside counsel (organizations). */
    onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;
    /** Whether outside counsel was AI pre-filled. */
    isOutsideCounselAiPrefilled?: boolean;
    /** Whether "Notify assigned resources" is checked. */
    notifyResources: boolean;
    /** Called when notify toggle changes. */
    onNotifyChange: (checked: boolean) => void;
}
export declare const AssignResourcesStep: React.FC<IAssignResourcesStepProps>;
//# sourceMappingURL=AssignResourcesStep.d.ts.map