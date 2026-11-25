/**
 * Invoice Review Code Page entry point (ebilling-r1 task 110; FR-31, FR-32, D-58).
 *
 * Opened with an invoice id (`data=invoiceId%3D{guid}`, `?invoiceId=`, or `id` + `typename=sprk_invoice`, plus an optional `lineId` to preselect a line; see `services/launch.ts`). Every
 * call goes through `authenticatedFetch` to the BFF; the page never reads or writes Dataverse rows itself (ADR-002): the review, the
 * decisions, the corrections and the PDF bytes are all the server's.
 *
 * The page scaffolding is the e-billing pages' shared one (`src/solutions/_ebilling-shared/`, issue 2965): the theme, the async auth bootstrap
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028) that the page waits on before it mounts, a retry state
 * when it fails (fail closed), and the themed surface with its error boundary. The Navigator side pane is registered on mount
 * (docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_invoicereviewpage (see the build rename in package.json). Its navigation entry is task 112's.
 */

import * as React from 'react';
import { parseDataParams, createXrmNavigationService } from '@spaarke/ui-components';
import { useCodePageBootstrap } from '../../_ebilling-shared/codePageBootstrap';
import { CodePageMessage, CodePageSpinner, CodePageSurface, mountCodePage } from '../../_ebilling-shared/codePageChrome';
import { InvoiceReviewWithDecisions } from './components/InvoiceReviewWithDecisions';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createInvoiceReviewApi } from './services/invoiceReviewApi';
import { createInvoiceDecisionApi } from './services/decisionApi';
import { parseLaunch } from './services/launch';

/** Open the invoice's matter, project or work assignment in the host (Xrm.Navigation through the parent frame). */
function openRecord(entityName: string, id: string): void {
  createXrmNavigationService()
    .openRecord(entityName, id)
    .catch(err => console.warn('[InvoiceReview] The record could not be opened:', err));
}

/**
 * "Submit corrected invoice" (task 095f-2e2b): opens the Create Invoice wizard code page in Correction mode for this invoice, as a
 * dialog (the same `navigateTo` launch every other wizard uses), and resolves when the dialog closes. The wizard reads the invoice's
 * correction prefill itself; the launch carries only the invoice id.
 */
function openCorrectionWizard(invoiceId: string): Promise<{ confirmed?: boolean }> {
  return createXrmNavigationService().openDialog('sprk_createinvoicewizard', `correctionInvoiceId=${encodeURIComponent(invoiceId)}`, {
    width: { value: 60, unit: '%' },
    height: { value: 70, unit: '%' },
    title: 'Submit corrected invoice',
  });
}

function Root() {
  const launch = React.useMemo(() => parseLaunch(parseDataParams()), []);
  const { theme, ready, bootstrapError, retry } = useCodePageBootstrap({
    label: 'InvoiceReview',
    enabled: launch.ok,
    registerNavigator: true,
    setRuntimeConfig,
    ensureAuthInitialized,
  });

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createInvoiceReviewApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const decisionApi = React.useMemo(() => (ready ? createInvoiceDecisionApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <CodePageMessage testId="invoicereview-launch" title="Invoice Review" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <CodePageMessage
        testId="invoicereview-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the page."
        onRetry={retry}
      />
    );
  } else if (!api || !decisionApi) {
    body = <CodePageSpinner testId="invoicereview-bootstrapping" label="Loading…" />;
  } else {
    body = <InvoiceReviewWithDecisions invoiceId={launch.invoiceId} initialLineId={launch.lineId} api={api} decisionApi={decisionApi} openRecord={openRecord} openCorrectionWizard={openCorrectionWizard} />;
  }

  return (
    <CodePageSurface theme={theme} surfaceName="Invoice Review" subtle>
      {body}
    </CodePageSurface>
  );
}

mountCodePage('InvoiceReview', <Root />);
