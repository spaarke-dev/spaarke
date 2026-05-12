/**
 * CreateRecordWizard.tsx
 * Reusable multi-step wizard for creating Dataverse records.
 *
 * Extracts ~265 LOC of duplicated boilerplate from entity-specific wizards:
 *   - File upload reducer + state
 *   - SPE container resolution
 *   - Reset-on-open
 *   - Follow-on step sync (dynamic add/remove via shellRef)
 *   - Assign Resources / Draft Summary / Send Email step rendering
 *   - Email pre-fill
 *   - Ref-based stale closure prevention
 *
 * Each entity provides only:
 *   - config.infoStep: entity-specific form (canAdvance + renderContent)
 *   - config.onFinish: record creation + success screen
 *   - Search callbacks (contacts, organizations, users)
 *
 * Steps (with optional associateToStep):
 *   [0] Associate To -- optional; only present when config.associateToStep is set
 *   [1] Add file(s)  -- always skip-able (canAdvance: true)
 *   [2] Entity info  -- from config.infoStep
 *   [3] Next Steps   -- follow-on action card selection
 *   [4+] Dynamic     -- Assign Resources, Draft Summary, Send Email
 *
 * Steps (without associateToStep):
 *   [0] Add file(s)  -- always skip-able (canAdvance: true)
 *   [1] Entity info  -- from config.infoStep
 *   [2] Next Steps   -- follow-on action card selection
 *   [3+] Dynamic     -- Assign Resources, Draft Summary, Send Email
 *
 * @see WizardShell -- underlying generic dialog shell
 * @see ADR-012 -- Shared Component Library
 */
import * as React from 'react';
import type { ICreateRecordWizardProps } from './types';
export declare const CreateRecordWizard: React.FC<ICreateRecordWizardProps>;
export default CreateRecordWizard;
//# sourceMappingURL=CreateRecordWizard.d.ts.map