/**
 * AssignWorkFollowOnStep.tsx
 * Follow-on step for creating a Work Assignment linked to the parent entity.
 *
 * Replaces the old "Assign Resources" step. Collects all fields needed to
 * create a sprk_workassignment Dataverse record, linked to the parent matter
 * or project via N:1 relationship.
 *
 * Fields:
 *   - Name (required, free text)
 *   - Description (optional, multi-line)
 *   - Matter Type (optional, lookup — auto-filled from parent matter)
 *   - Practice Area (optional, lookup — auto-filled from parent record)
 *   - Priority (option set: Low / Normal / High / Critical; defaults to Normal)
 *   - Response Due Date (optional, date picker)
 *   - Assigned Attorney (optional, contact lookup)
 *   - Assigned Paralegal (optional, contact lookup)
 *   - Assigned Outside Counsel (optional, organization lookup)
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors
 *   - makeStyles with semantic tokens throughout
 *   - ADR-021: dark mode support via colorNeutral/colorBrand tokens
 *   - ADR-012: shared library component, no solution-specific imports
 */
import * as React from 'react';
import type { ILookupItem } from '../../../types/LookupTypes';
export declare const WORK_ASSIGNMENT_PRIORITY: {
    readonly Low: 100000000;
    readonly Normal: 100000001;
    readonly High: 100000002;
    readonly Critical: 100000003;
};
export type WorkAssignmentPriorityValue = (typeof WORK_ASSIGNMENT_PRIORITY)[keyof typeof WORK_ASSIGNMENT_PRIORITY];
export interface IAssignWorkFollowOnStepProps {
    /** Name of the work assignment (required). */
    nameValue: string;
    onNameChange: (value: string) => void;
    /** Description (optional, multi-line). */
    descriptionValue: string;
    onDescriptionChange: (value: string) => void;
    /** Matter Type lookup. Auto-filled from the parent matter/project form. */
    matterTypeValue: ILookupItem | null;
    onMatterTypeChange: (item: ILookupItem | null) => void;
    onSearchMatterTypes: (query: string) => Promise<ILookupItem[]>;
    /** Practice Area lookup. Auto-filled from the parent matter/project form. */
    practiceAreaValue: ILookupItem | null;
    onPracticeAreaChange: (item: ILookupItem | null) => void;
    onSearchPracticeAreas: (query: string) => Promise<ILookupItem[]>;
    /** Priority option set value. Defaults to Normal (100000001). */
    priorityValue: WorkAssignmentPriorityValue;
    onPriorityChange: (value: WorkAssignmentPriorityValue) => void;
    /** Response Due Date (ISO date string, e.g. "2026-04-15"). */
    responseDueDateValue: string;
    onResponseDueDateChange: (value: string) => void;
    /** Assigned Attorney (contact lookup). */
    attorneyValue: ILookupItem | null;
    onAttorneyChange: (item: ILookupItem | null) => void;
    onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;
    /** Assigned Paralegal (contact lookup). */
    paralegalValue: ILookupItem | null;
    onParalegalChange: (item: ILookupItem | null) => void;
    onSearchParalegals: (query: string) => Promise<ILookupItem[]>;
    /** Assigned Outside Counsel (organization lookup). */
    outsideCounselValue: ILookupItem | null;
    onOutsideCounselChange: (item: ILookupItem | null) => void;
    onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;
}
export declare const AssignWorkFollowOnStep: React.FC<IAssignWorkFollowOnStepProps>;
//# sourceMappingURL=AssignWorkFollowOnStep.d.ts.map