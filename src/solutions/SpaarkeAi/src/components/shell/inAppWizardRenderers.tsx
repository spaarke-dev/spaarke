/**
 * inAppWizardRenderers.tsx — the code-page wizards the Console hosts in-app
 * (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended 2026-10-07, launch rule (a)).
 *
 * `InAppWizardHost` (in `@spaarke/ui-components`) opens the Create wizards and Summarize Files itself.
 * Upload Documents, Find Similar and the Workspace layout wizard live in code-page solutions
 * (`DocumentUploadWizard`, `FindSimilarCodePage`, `WorkspaceLayoutWizard`) that a shared library
 * cannot import, so the Console — which may — supplies their renderers here. Each renders the SAME
 * component its code page mounts, parsed from the SAME launch data (`launchParams`), but in-app:
 * inside `SprkModal` (named size, explicit dismiss), under the Console's own theme, closing only
 * through the host's `onClose` (never the parent-document close-button hack, never `window.close()`).
 *
 * The code pages stay deployable and unchanged for the ribbon / subgrid scripts and the
 * SemanticSearchControl PCF (launch rule (b)); a host mounted WITHOUT these renderers keeps every
 * launch on `navigateTo`.
 */

import * as React from 'react';
import type { InAppWizardRenderer, InAppWizardRenderers } from '@spaarke/ui-components';

import { DocumentUploadWizardDialog } from '../../../../DocumentUploadWizard/src/DocumentUploadWizardDialog';
import { resolveUploadLaunchParams } from '../../../../DocumentUploadWizard/src/launchParams';
import { FindSimilarApp } from '../../../../FindSimilarCodePage/src/App';
import { parseFindSimilarLaunch } from '../../../../FindSimilarCodePage/src/launchParams';
import { App as WorkspaceLayoutWizardApp } from '../../../../WorkspaceLayoutWizard/src/App';
import { parseLayoutWizardData } from '../../../../WorkspaceLayoutWizard/src/launchParams';

/** Upload Documents (`sprk_documentuploadwizard`): the 3-step upload wizard, non-embedded. */
export const renderDocumentUploadWizard: InAppWizardRenderer = ({ data, onClose, bffBaseUrl, uiScale }) => {
  const { parentEntityType, parentEntityId, parentEntityName } = resolveUploadLaunchParams(data);
  return (
    <DocumentUploadWizardDialog
      parentEntityType={parentEntityType}
      parentEntityId={parentEntityId}
      parentEntityName={parentEntityName}
      onClose={onClose}
      embedded={false}
      uiScale={uiScale}
      bffBaseUrl={bffBaseUrl}
    />
  );
};

/** Find Similar (`sprk_findsimilar`): the lookup / upload form in SprkModal. */
export const renderFindSimilarWizard: InAppWizardRenderer = ({
  data,
  onClose,
  bffBaseUrl,
  tenantId,
  uiScale,
  authenticatedFetch,
  initialFileRefs,
}) => (
  <FindSimilarApp
    apiBaseUrl={bffBaseUrl}
    tenantId={tenantId ?? ''}
    authenticatedFetch={authenticatedFetch}
    initialFileRefs={initialFileRefs}
    initialDocument={parseFindSimilarLaunch(`?${data}`)}
    inApp={{ onClose, uiScale }}
  />
);

/** Workspace layout (`sprk_workspacelayoutwizard`): create / edit / save-as, non-embedded. */
export const renderWorkspaceLayoutWizard: InAppWizardRenderer = ({ data, onClose, uiScale, authenticatedFetch }) => {
  const params = parseLayoutWizardData(data);
  return (
    <WorkspaceLayoutWizardApp
      mode={params.mode}
      layoutId={params.layoutId}
      layoutTemplateId={params.layoutTemplateId}
      sectionsJson={params.sectionsJson}
      sourceName={params.sourceName}
      authenticatedFetch={authenticatedFetch}
      templateFilter={params.templateFilter}
      startAtStep={params.startAtStep ?? undefined}
      inApp={{ onClose, uiScale }}
    />
  );
};

/** The renderer set `ThreePaneShell` passes to `InAppWizardHost` (a module constant: stable identity). */
export const IN_APP_WIZARD_RENDERERS: InAppWizardRenderers = {
  sprk_documentuploadwizard: renderDocumentUploadWizard,
  sprk_findsimilar: renderFindSimilarWizard,
  sprk_workspacelayoutwizard: renderWorkspaceLayoutWizard,
};
