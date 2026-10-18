/**
 * InvoiceSubmitPage.tsx
 *
 * SPA page for submitting an invoice PDF (ebilling-r1 task 051, FR-18 / FR-19 / FR-45). It hosts the shared
 * InvoiceSubmissionWizard (@spaarke/ui-components) and wires its three data callbacks to the BFF's task 044 external
 * routes through the SPA's own client (`web-api-client`: `bffApiCall`, the active backend's URL and token). The wizard
 * is entry-point-agnostic; the internal entry point (task 052) injects its own callbacks into the same component.
 *
 * Route: /invoices/submit[?submissionId={guid}]
 *
 *   - No submissionId: the wizard (Record, File, Details, then Processing and Result). Once the server accepts the
 *     submission the URL is rewritten to carry its id, so reloading or returning to the address shows it again.
 *   - submissionId: the Processing / Result screen alone, read from the server. Leaving the page and coming back shows
 *     the same answer (FR-18). Nothing is kept in the browser.
 *
 * FR-45: the first thing the page does is the availability probe (`firms` on /me, then the submissions list only when
 * `firms` is absent, to tell an off flag apart from an unknown state). A 503 `ebilling.disabled` replaces the page with a
 * plain "not available" state; the wizard is never shown. The client decides no authorization (ADR-002 / FR-17): it
 * shows what the server refused, by code.
 *
 * ADR-021: Fluent v9 tokens only. ADR-012: composes the shared component. The wrapper is styled, never the
 * FluentProvider (project gotcha F-10).
 */
import * as React from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Button, MessageBar, MessageBarBody, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { InvoiceSubmissionOutcome, InvoiceSubmissionWizard } from '@spaarke/ui-components/components/InvoiceSubmission';
import type { InvoiceSubmissionAccepted } from '@spaarke/ui-components/components/InvoiceSubmission';

import {
  getInvoiceSubmission,
  listInvoiceCoreRecords,
  listInvoiceFirms,
  probeInvoiceSubmission,
  submitInvoice,
} from '../api/web-api-client';
import { PageContainer, NavigationBar } from '../components';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The page's route; the Invoices widget's "Submit invoice" action opens it. */
export const INVOICE_SUBMIT_PATH = '/invoices/submit';

const useStyles = makeStyles({
  stack: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
  },
  // The embedded wizard fills its parent: give it a height that keeps the footer on screen.
  wizardFrame: {
    height: 'calc(100dvh - 260px)',
    minHeight: '480px',
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  resumeCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
});

const BREADCRUMBS = [{ label: 'My Projects', href: '/' }, { label: 'Submit Invoice' }];

type Availability = 'checking' | 'available' | 'disabled';

export const InvoiceSubmitPage: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();

  const submissionIdParam = React.useMemo(
    () => new URLSearchParams(location.search).get('submissionId'),
    [location.search]
  );
  const resumeId = submissionIdParam && GUID_PATTERN.test(submissionIdParam) ? submissionIdParam : null;
  const badResumeId = submissionIdParam !== null && resumeId === null;

  // FR-45: ask the server whether eBilling is on before showing anything to act on.
  const [availability, setAvailability] = React.useState<Availability>('checking');
  React.useEffect(() => {
    let cancelled = false;
    void probeInvoiceSubmission().then(answer => {
      if (!cancelled) setAvailability(answer === 'disabled' ? 'disabled' : 'available');
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const goHome = React.useCallback(() => navigate('/'), [navigate]);

  // Rewrite the address to carry the accepted submission's id WITHOUT a router navigation: the wizard stays mounted on
  // its result screen, while a reload or a return to this address lands on the same submission (FR-18).
  const handleSubmitted = React.useCallback((accepted: InvoiceSubmissionAccepted) => {
    window.history.replaceState(
      window.history.state,
      '',
      `${INVOICE_SUBMIT_PATH}?submissionId=${encodeURIComponent(accepted.submissionId)}`
    );
  }, []);

  if (availability === 'checking') {
    return (
      <PageContainer>
        <NavigationBar items={BREADCRUMBS} />
        <Spinner size="small" label="Checking invoice submission" />
      </PageContainer>
    );
  }

  if (availability === 'disabled') {
    return (
      <PageContainer>
        <NavigationBar items={BREADCRUMBS} />
        <div className={styles.stack}>
          <MessageBar intent="info" data-testid="invoice-submit-unavailable">
            <MessageBarBody>Invoice submission is not available.</MessageBarBody>
          </MessageBar>
          <div className={styles.actions}>
            <Button appearance="primary" onClick={goHome}>
              Back to workspace
            </Button>
          </div>
        </div>
      </PageContainer>
    );
  }

  if (badResumeId) {
    return (
      <PageContainer>
        <NavigationBar items={BREADCRUMBS} />
        <div className={styles.stack}>
          <MessageBar intent="error">
            <MessageBarBody>The submission id in the link is not valid.</MessageBarBody>
          </MessageBar>
          <div className={styles.actions}>
            <Button appearance="primary" onClick={() => navigate(INVOICE_SUBMIT_PATH)}>
              Submit an invoice
            </Button>
          </div>
        </div>
      </PageContainer>
    );
  }

  // Resume: the stored status and result, read from the server.
  if (resumeId) {
    return (
      <PageContainer>
        <NavigationBar items={BREADCRUMBS} />
        <div className={styles.resumeCard}>
          <Text as="h1" size={500} weight="semibold">
            Invoice submission
          </Text>
          <InvoiceSubmissionOutcome key={resumeId} submissionId={resumeId} getStatus={getInvoiceSubmission} />
          <div className={styles.actions}>
            <Button appearance="primary" onClick={() => navigate(INVOICE_SUBMIT_PATH)}>
              Submit another invoice
            </Button>
            <Button appearance="secondary" onClick={goHome}>
              Back to workspace
            </Button>
          </div>
        </div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <NavigationBar items={BREADCRUMBS} />
      <div className={styles.wizardFrame}>
        <InvoiceSubmissionWizard
          embedded
          open
          title="Submit invoice"
          onClose={goHome}
          doneLabel="Back to workspace"
          loadRecords={listInvoiceCoreRecords}
          loadFirms={listInvoiceFirms}
          submit={submitInvoice}
          getStatus={getInvoiceSubmission}
          onSubmitted={handleSubmitted}
        />
      </div>
    </PageContainer>
  );
};

export default InvoiceSubmitPage;
