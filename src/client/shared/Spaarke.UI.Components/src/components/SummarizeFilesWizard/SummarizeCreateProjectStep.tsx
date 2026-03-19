/**
 * SummarizeCreateProjectStep.tsx
 * Follow-on step for "Create Project" in the Summarize Files wizard.
 *
 * Wraps the existing CreateProjectStep component, passing through
 * the uploaded files for AI pre-fill support.
 */
import * as React from 'react';
import { CreateProjectStep } from '../CreateProjectWizard/CreateProjectStep';
import type { ICreateProjectFormState } from '../CreateProjectWizard/projectFormTypes';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeCreateProjectStepProps {
  /** IDataService reference for Dataverse operations. */
  dataService: IDataService;
  /** Uploaded files from the wizard — forwarded for AI pre-fill. */
  uploadedFiles: IUploadedFile[];
  /** Called when form validity changes. */
  onValidChange: (isValid: boolean) => void;
  /** Called on every form change with latest values. */
  onFormValues: (values: ICreateProjectFormState) => void;
  /** Initial form values (preserved on back-navigation). */
  initialFormValues?: ICreateProjectFormState;
}

// ---------------------------------------------------------------------------
// SummarizeCreateProjectStep (exported)
// ---------------------------------------------------------------------------

export const SummarizeCreateProjectStep: React.FC<ISummarizeCreateProjectStepProps> = ({
  dataService,
  uploadedFiles,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  return (
    <CreateProjectStep
      dataService={dataService}
      onValidChange={onValidChange}
      onFormValues={onFormValues}
      uploadedFiles={uploadedFiles}
      initialFormValues={initialFormValues}
    />
  );
};
