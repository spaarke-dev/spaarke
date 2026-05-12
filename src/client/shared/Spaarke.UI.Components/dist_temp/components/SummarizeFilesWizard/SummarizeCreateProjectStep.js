/**
 * SummarizeCreateProjectStep.tsx
 * Follow-on step for "Create Project" in the Summarize Files wizard.
 *
 * Wraps the existing CreateProjectStep component, passing through
 * the uploaded files for AI pre-fill support.
 */
import * as React from 'react';
import { CreateProjectStep } from '../CreateProjectWizard/CreateProjectStep';
// ---------------------------------------------------------------------------
// SummarizeCreateProjectStep (exported)
// ---------------------------------------------------------------------------
export const SummarizeCreateProjectStep = ({ dataService, uploadedFiles, onValidChange, onFormValues, initialFormValues, }) => {
    return (React.createElement(CreateProjectStep, { dataService: dataService, onValidChange: onValidChange, onFormValues: onFormValues, uploadedFiles: uploadedFiles, initialFormValues: initialFormValues }));
};
//# sourceMappingURL=SummarizeCreateProjectStep.js.map