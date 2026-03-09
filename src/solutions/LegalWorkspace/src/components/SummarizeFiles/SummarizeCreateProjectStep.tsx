/**
 * SummarizeCreateProjectStep.tsx
 * Follow-on step for "Create Project" in the Summarize Files wizard.
 *
 * Wraps the existing CreateProjectStep component, passing through
 * the uploaded files for AI pre-fill support.
 */
import * as React from 'react';
import { CreateProjectStep } from '../CreateProject/CreateProjectStep';
import type { ICreateProjectFormState } from '../CreateProject/projectFormTypes';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeCreateProjectStepProps {
  /** Xrm.WebApi reference for Dataverse operations. */
  webApi: IWebApi;
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
  webApi,
  uploadedFiles,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  return (
    <CreateProjectStep
      webApi={webApi}
      onValidChange={onValidChange}
      onFormValues={onFormValues}
      uploadedFiles={uploadedFiles}
      initialFormValues={initialFormValues}
    />
  );
};
