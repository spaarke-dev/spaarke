/**
 * AssignResourcesStep.tsx
 * Follow-on step for assigning internal and external resources.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library since this
 * step is entity-agnostic — it uses lookup callbacks provided by the parent.
 *
 * @see CreateRecordWizard — wires search callbacks and form state
 */
import * as React from 'react';
import type { ILookupItem } from '../../../types/LookupTypes';
export interface IAssignResourcesStepProps {
    attorneyValue: ILookupItem | null;
    onAttorneyChange: (item: ILookupItem | null) => void;
    onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;
    paralegalValue: ILookupItem | null;
    onParalegalChange: (item: ILookupItem | null) => void;
    onSearchParalegals: (query: string) => Promise<ILookupItem[]>;
    outsideCounselValue: ILookupItem | null;
    onOutsideCounselChange: (item: ILookupItem | null) => void;
    onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;
    notifyResources: boolean;
    onNotifyChange: (checked: boolean) => void;
}
export declare const AssignResourcesStep: React.FC<IAssignResourcesStepProps>;
//# sourceMappingURL=AssignResourcesStep.d.ts.map