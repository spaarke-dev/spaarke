/**
 * AP Export Code Page entry point (ebilling-r1 task 151b, EB-D-181; FR-41, FR-42).
 *
 * A dialog page: the ribbon script `sprk_/scripts/apexport_ribbon.js` opens it with `Xrm.Navigation.navigateTo`
 * (target 2) from the sprk_invoice views and form, with `data = "mode=<export|markpaid>&ids=..."`. ADR-050: a hostless
 * ribbon script launches with navigateTo; the platform dialog carries the title and the close button.
 *
 * The page scaffolding is the e-billing pages' shared one (`src/solutions/_ebilling-shared/`, issue 2965): the theme, the async auth bootstrap
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028), and the themed surface with its error boundary. The
 * page mounts only after the bootstrap resolves; if it fails the page shows a retry state (fail closed). Every export, payment and download
 * goes through `authenticatedFetch` to the BFF; the page never writes Dataverse rows itself (ADR-002). The only `Xrm.WebApi` use is
 * the read-only display of the selection (`services/invoices.ts`).
 *
 * The Navigator side pane is registered on mount (root CLAUDE.md section 17, docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_apexport (see the build rename in package.json).
 */

import * as React from 'react';
import { parseDataParams, createXrmNavigationService } from '@spaarke/ui-components';
import { useCodePageBootstrap } from '../../_ebilling-shared/codePageBootstrap';
import { CodePageMessage, CodePageSpinner, CodePageSurface } from '../../_ebilling-shared/codePageChrome';
import { mountWizardPage } from '@spaarke/ui-components/utils/mountWizardPage';
import { App } from './App';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createApExportApi } from './services/apExportApi';
import { parseLaunch } from './services/launch';

/** Close the host navigateTo dialog; the ribbon script then refreshes the grid or form. */
function closeDialog(): void {
  try {
    createXrmNavigationService().closeDialog({ confirmed: true });
  } catch (err) {
    console.warn('[ApExport] The dialog could not be closed:', err);
  }
}

function Root() {
  const launch = React.useMemo(() => parseLaunch(parseDataParams()), []);
  const { theme, ready, bootstrapError, retry } = useCodePageBootstrap({
    label: 'ApExport',
    registerNavigator: true,
    setRuntimeConfig,
    ensureAuthInitialized,
  });

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createApExportApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (bootstrapError) {
    body = (
      <CodePageMessage
        testId="apexport-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or close this dialog and try again."
        onRetry={retry}
        density="roomy"
      />
    );
  } else if (!api) {
    body = <CodePageSpinner testId="apexport-loading" label="Loading…" density="roomy" />;
  } else {
    body = <App api={api} launch={launch} onClose={closeDialog} />;
  }

  return (
    <CodePageSurface theme={theme} surfaceName="AP Export">
      {body}
    </CodePageSurface>
  );
}

mountWizardPage('ApExport', Root);
