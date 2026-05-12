/**
 * AddFilesStep.tsx
 * Step 2: "Add Files" -- upload new files to include with the work assignment.
 *
 * Documents from the associated record (step 1) are already available --
 * this step only handles NEW file uploads.
 *
 * This step is skippable (canAdvance always true).
 */
import * as React from 'react';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
export interface IAddFilesStepProps {
    /** Called with the list of uploaded files whenever it changes. */
    onUploadedFilesChange: (files: IUploadedFile[]) => void;
    initialUploadedFiles?: IUploadedFile[];
}
export declare const AddFilesStep: React.FC<IAddFilesStepProps>;
//# sourceMappingURL=AddFilesStep.d.ts.map