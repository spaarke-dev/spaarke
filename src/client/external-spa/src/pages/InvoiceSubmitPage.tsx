/**
 * InvoiceSubmitPage.tsx
 *
 * SPA page for submitting an invoice PDF (ebilling-r1 task 051, FR-18 / FR-19 / FR-45). It hosts the shared
 * InvoiceSubmissionWizard (@spaarke/ui-components) and wires its three data callbacks to the BFF's task 044 external
 * routes through the SPA's own client (`web-api-client`: `bffApiCall`, the active backend's URL and token). The wizard
 * is entry-point-agnostic; the internal entry point (task 052) injects its own callbacks into the same component.
 *
 * Route: /invoices/submit[?submissionId={guid}][?invoiceRecord={kind}:{guid}]
 *
 *   - No submissionId: the wizard (Record, File, Details, then Processing and Result). Once the server accepts the
 *     submission the URL is rewritten to carry its id, so reloading or returning to the address shows it again.
 *   - submissionId: the Processing / Result screen alone, read from the server. Leaving the page and coming back shows
 *     the same answer (FR-18). Nothing is kept in the browser. It wins over `invoiceRecord` when both are present.
 *   - invoiceRecord (spaarke-SPA-external-access-platform-r4 task 007, FR-10; agreed with ebilling-r1): an address
 *     that names a record opens the wizard with that record preselected (no in-app link builds one since task 010: the
 *     record view's "Submit invoice" opens a modal instead; a saved or pasted address still works). `kind` is matter | project | workassignment and the
 *     id a GUID; anything else is ignored and the wizard starts empty. No name travels in the address: the option is
 *     taken from this page's own `loadRecords` result (the records the caller can submit for), so a record the caller
 *     cannot submit for is simply not preselected. A caller with several firms is never given a firm by the link: the
 *     firm step stays first, and the record stays chosen only if the CHOSEN firm's list contains it (the wizard drops a
 *     choice its list does not offer). See {@link usePreselectedRecord}.
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
import type {
  InvoiceCoreRecordList,
  InvoiceCoreRecordOption,
  InvoiceFirmOption,
  InvoiceRecordQuery,
  InvoiceSubmissionAccepted,
} from '@spaarke/ui-components/components/InvoiceSubmission';

import {
  getInvoiceSubmission,
  listInvoiceCoreRecords,
  listInvoiceFirms,
  probeInvoiceSubmission,
  submitInvoice,
} from '../api/web-api-client';
import { PageContainer, NavigationBar } from '../components';
import {
  GUID_PATTERN,
  INVOICE_RECORD_PARAM,
  INVOICE_SUBMIT_PATH,
  parseInvoiceRecordParam,
  type InvoiceRecordRef,
} from './invoiceSubmitLink';

/** What `usePreselectedRecord` reads ahead, handed to the wizard's first identical call and then dropped. */
interface Prefetched {
  firms?: Promise<InvoiceFirmOption[] | null>;
  records: Map<string, Promise<InvoiceCoreRecordList>>;
}

/**
 * The wizard seeds its chosen record when it MOUNTS (`initialRecord`), and the option (name, number) is known only from a
 * `loadRecords` result, so for a `?invoiceRecord=` link the page reads the lists itself first and mounts the wizard after:
 *   - the firms (`loadFirms`, one `/me` read), then the records of the one firm of a single-firm caller (or with the
 *     firms unknown, unnarrowed), or of EVERY firm of a several-firm caller;
 *   - the option is the first one in those lists with the link's kind and id. Reading every firm's list picks no firm: the
 *     wizard still opens on the firm step, and drops the preselected record when the CHOSEN firm's list does not offer it;
 *   - each result is handed to the wizard's first identical call, so nothing is read twice; a failed read is not kept (the
 *     wizard asks again and shows the failure itself).
 * Without a valid link nothing is read here and the wizard gets `listInvoiceCoreRecords` / `listInvoiceFirms` unchanged.
 */
function usePreselectedRecord(
  link: InvoiceRecordRef | null,
  enabled: boolean
): {
  ready: boolean;
  initialRecord: InvoiceCoreRecordOption | undefined;
  loadRecords: (query: InvoiceRecordQuery) => Promise<InvoiceCoreRecordList>;
  loadFirms: () => Promise<InvoiceFirmOption[] | null>;
} {
  const linkKind = link?.kind;
  const linkId = link?.recordId.toLowerCase();
  const linkKey = linkKind && linkId ? `${linkKind}:${linkId}` : null;
  const prefetched = React.useRef<Prefetched>({ records: new Map() });
  const [settled, setSettled] = React.useState<{ key: string; record: InvoiceCoreRecordOption | undefined } | null>(
    null
  );

  React.useEffect(() => {
    if (!linkKind || !linkId || !enabled) return;
    let cancelled = false;
    const cache: Prefetched = { records: new Map() };
    prefetched.current = cache;
    void (async () => {
      const firmsRead = listInvoiceFirms();
      cache.firms = firmsRead;
      let firms: InvoiceFirmOption[] = [];
      try {
        firms = (await firmsRead) ?? [];
      } catch {
        cache.firms = undefined;
      }
      const firmIds: Array<string | undefined> =
        firms.length > 1 ? firms.map(f => f.firmId) : firms.length === 1 ? [firms[0].firmId] : [undefined];
      const reads = firmIds.map(firmId => {
        const read = listInvoiceCoreRecords(firmId ? { firmId } : {});
        cache.records.set(firmId ?? '', read);
        return read;
      });
      const outcomes = await Promise.allSettled(reads);
      outcomes.forEach((outcome, i) => {
        if (outcome.status === 'rejected') cache.records.delete(firmIds[i] ?? '');
      });
      if (cancelled) return;
      let record: InvoiceCoreRecordOption | undefined;
      for (const outcome of outcomes) {
        if (outcome.status !== 'fulfilled') continue;
        record = outcome.value.records.find(r => r.kind === linkKind && r.recordId.toLowerCase() === linkId);
        if (record) break;
      }
      setSettled({ key: `${linkKind}:${linkId}`, record });
    })();
    return () => {
      cancelled = true;
    };
  }, [linkKind, linkId, enabled]);

  const loadRecords = React.useCallback((query: InvoiceRecordQuery): Promise<InvoiceCoreRecordList> => {
    const key = query.firmId ?? '';
    const read = prefetched.current.records.get(key);
    if (!read) return listInvoiceCoreRecords(query);
    prefetched.current.records.delete(key);
    return read;
  }, []);
  const loadFirms = React.useCallback((): Promise<InvoiceFirmOption[] | null> => {
    const read = prefetched.current.firms;
    if (!read) return listInvoiceFirms();
    prefetched.current.firms = undefined;
    return read;
  }, []);

  if (!linkKey) {
    return { ready: true, initialRecord: undefined, loadRecords: listInvoiceCoreRecords, loadFirms: listInvoiceFirms };
  }
  const ready = settled?.key === linkKey;
  return { ready, initialRecord: ready ? settled.record : undefined, loadRecords, loadFirms };
}

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

  // R4 task 007: an optional `?invoiceRecord={kind}:{guid}` preselect. `submissionId` (valid or not) wins: the wizard is not shown then.
  const recordLink = React.useMemo(
    () => parseInvoiceRecordParam(new URLSearchParams(location.search).get(INVOICE_RECORD_PARAM)),
    [location.search]
  );
  const preselect = usePreselectedRecord(recordLink, availability === 'available' && submissionIdParam === null);

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

  // A `?invoiceRecord=` link: the lists are read first (the wizard seeds its chosen record when it mounts).
  if (!preselect.ready) {
    return (
      <PageContainer>
        <NavigationBar items={BREADCRUMBS} />
        <Spinner size="small" label="Loading your records" />
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
          initialRecord={preselect.initialRecord}
          loadRecords={preselect.loadRecords}
          loadFirms={preselect.loadFirms}
          submit={submitInvoice}
          getStatus={getInvoiceSubmission}
          onSubmitted={handleSubmitted}
        />
      </div>
    </PageContainer>
  );
};

export default InvoiceSubmitPage;
