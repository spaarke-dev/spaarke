/**
 * Invoice Files Code Page entry point (ebilling-r1 task 151e).
 *
 * Embedded in the `sprk_invoice` main form (Files section) as a web resource control with "Pass record object-type code and unique
 * identifier as parameters", so the URL carries `id` and `typename` (see `services/launch.ts`). Lists the invoice's servable files from
 * `GET /api/finance/invoices/{invoiceId}/files`; Open and Download go through the content route. Every call goes through
 * `authenticatedFetch` to the BFF; the page never reads or writes Dataverse rows itself (ADR-002) - the file list is the server's
 * decision, and there is deliberately no `sprk_storedfile` subgrid (no role grants Read on it).
 *
 * The page scaffolding is the e-billing pages' shared one (`src/solutions/_ebilling-shared/`, issue 2965): the theme, the async auth bootstrap
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028) that the page waits on before it mounts, a retry state
 * when it fails (fail closed), and the themed surface with its error boundary. The Navigator side pane is the form's own concern, so it is
 * not registered here (this is an embedded section, not a page the user navigates to).
 *
 * Web resource: sprk_invoicefiles (see the build rename in package.json).
 */

import * as React from 'react';
import { parseDataParams, createXrmNavigationService } from '@spaarke/ui-components';
import { useCodePageBootstrap } from '../../_ebilling-shared/codePageBootstrap';
import { CodePageMessage, CodePageSpinner, CodePageSurface, mountCodePage } from '../../_ebilling-shared/codePageChrome';
import { InvoiceFilesPanel } from './components/InvoiceFilesPanel';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createInvoiceFilesApi } from './services/invoiceFilesApi';
import { browserDeps } from './services/fileActions';
import { parseLaunch } from './services/launch';

/** Save a blob as a file (an object URL and a temporary link); the page is same-origin with the app, so the download is the browser's own. */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Open the source email Document's form in the host (Xrm.Navigation.openForm through the parent frame). */
async function openDocument(documentId: string): Promise<void> {
  // A failure propagates: the panel shows the user a message (InvoiceFilesPanel.openSourceEmail).
  await createXrmNavigationService().openRecord('sprk_document', documentId);
}

function Root() {
  const launch = React.useMemo(() => parseLaunch(parseDataParams()), []);
  // An unsaved or foreign record needs no auth: the page only says so.
  const { theme, ready, bootstrapError, retry } = useCodePageBootstrap({
    label: 'InvoiceFiles',
    enabled: launch.ok,
    setRuntimeConfig,
    ensureAuthInitialized,
  });

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createInvoiceFilesApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const actionDeps = React.useMemo(() => (api ? browserDeps(api, saveBlob) : null), [api]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <CodePageMessage testId="invoicefiles-launch" title="Files" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <CodePageMessage
        testId="invoicefiles-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the form."
        onRetry={retry}
      />
    );
  } else if (!api || !actionDeps) {
    body = <CodePageSpinner testId="invoicefiles-bootstrapping" label="Loading files…" />;
  } else {
    body = <InvoiceFilesPanel invoiceId={launch.invoiceId} api={api} actionDeps={actionDeps} openDocument={openDocument} />;
  }

  return (
    <CodePageSurface theme={theme} surfaceName="Invoice Files">
      {body}
    </CodePageSurface>
  );
}

mountCodePage('InvoiceFiles', <Root />);
