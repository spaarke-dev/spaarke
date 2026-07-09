/**
 * CreateInvoiceStep.tsx
 * Entity-specific "Enter Info" form for the "Create New Invoice" wizard.
 *
 * Fields (owner-provided, Phase-0-validated manifest — spec FR-16 / design
 * §5.10 / notes/field-manifests/invoice.md):
 *   - Invoice Number (optional, Input)      -> sprk_invoicenumber
 *   - Name (REQUIRED, Input)                -> sprk_name (schema NOT NULL)
 *   - Description (optional, Textarea)      -> sprk_description
 *   - Vendor Org (optional, LookupField)     -> sprk_vendororg (-> sprk_organization)
 *   - Invoice Date (Input type="date", defaults to today) -> sprk_invoicedate
 *
 * AI prefill (spec FR-11 / design §5.7): `useAiPrefill` is fully wired (the
 * seam) but gated OFF via the local `prefillEnabled = false` constant — no
 * spinner, no network call while disabled (BFF=N this release). Flipping the
 * flag to `true` (once the BFF endpoint + JPS Action ship in a follow-on
 * project) is the ONLY change needed here.
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations (vendor org search)
 *   - authenticatedFetch / bffBaseUrl: forwarded to the (currently inert) prefill hook
 *
 * @see IDataService — high-level data access abstraction
 * @see useAiPrefill — the shared inert-seam hook
 */
import * as React from 'react';
import { Text, Input, Textarea, Field, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import { useAiPrefill, type IResolvedPrefillFields } from '../../hooks/useAiPrefill';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import { searchOrganizationsAsLookup } from '../CreateMatterWizard/matterService';
import { buildEmptyInvoiceForm } from './formTypes';
import type { ICreateInvoiceFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// AI prefill inert seam — spec FR-11 / design §5.7. Flip to `true` only once
// the BFF `/api/workspace/invoices/pre-fill` endpoint + JPS Action ship.
// ---------------------------------------------------------------------------
const PREFILL_ENABLED = false;

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateInvoiceStepProps {
  dataService: IDataService;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateInvoiceFormState) => void;
  initialFormValues?: ICreateInvoiceFormState;
  /** Files uploaded in the Add Files step — used for AI pre-fill when enabled. */
  uploadedFiles?: IUploadedFile[];
  /** Authenticated fetch function for BFF API calls (prefill seam only). */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** BFF API base URL (prefill seam only). */
  bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateInvoiceStep: React.FC<ICreateInvoiceStepProps> = ({
  dataService,
  onValidChange,
  onFormValues,
  initialFormValues,
  uploadedFiles = [],
  authenticatedFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateInvoiceFormState>(
    initialFormValues ?? buildEmptyInvoiceForm()
  );

  // Report validity + values whenever form changes. Name is the sole required
  // field (sprk_name is NOT NULL on the schema per the Phase-0 manifest).
  React.useEffect(() => {
    const isValid = formValues.name.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  // -- Field handlers ----------------------------------------------------

  const handleInvoiceNumberChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, invoiceNumber: e.target.value }));
  }, []);

  const handleNameChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, name: e.target.value }));
  }, []);

  const handleDescriptionChange = React.useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setFormValues(prev => ({ ...prev, description: e.target.value }));
  }, []);

  const handleVendorOrgChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      vendorOrgId: item?.id ?? '',
      vendorOrgName: item?.name ?? '',
    }));
  }, []);

  const handleSearchVendorOrgs = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(dataService, query),
    [dataService]
  );

  const handleInvoiceDateChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, invoiceDate: e.target.value }));
  }, []);

  // -- AI Pre-fill via shared hook (inert seam — spec FR-11) ---------------
  const handlePrefillApply = React.useCallback((resolved: IResolvedPrefillFields) => {
    for (const [key, value] of Object.entries(resolved)) {
      if (typeof value === 'string') {
        if (key === 'invoiceNumber') setFormValues(prev => ({ ...prev, invoiceNumber: value }));
        else if (key === 'name') setFormValues(prev => ({ ...prev, name: value }));
        else if (key === 'description') setFormValues(prev => ({ ...prev, description: value }));
      } else if (key === 'vendorOrgName') {
        setFormValues(prev => ({ ...prev, vendorOrgId: value.id, vendorOrgName: value.name }));
      }
    }
  }, []);

  const prefill = useAiPrefill({
    endpoint: '/api/workspace/invoices/pre-fill',
    // Gate #1: while PREFILL_ENABLED is false, the hook is handed an empty
    // array regardless of what the Add Files step collected — its internal
    // effect short-circuits on `uploadedFiles.length === 0` (no fetch, no
    // spinner). Flipping PREFILL_ENABLED to true is the only change needed
    // to feed the real uploadedFiles through.
    uploadedFiles: PREFILL_ENABLED ? uploadedFiles : [],
    authenticatedFetch: authenticatedFetch ?? ((...args: Parameters<typeof fetch>) => fetch(...args)),
    bffBaseUrl: bffBaseUrl ?? '',
    fieldExtractor: data => ({
      textFields: {
        invoiceNumber: data.invoiceNumber as string | undefined,
        name: data.name as string | undefined,
        description: data.description as string | undefined,
      },
      lookupFields: {
        vendorOrgName: data.vendorOrgName as string | undefined,
      },
    }),
    lookupResolvers: {
      vendorOrgName: v => searchOrganizationsAsLookup(dataService, v),
    },
    onApply: handlePrefillApply,
    // Gate #2: belt-and-suspenders — even if a future refactor stops gating
    // `uploadedFiles` above, this keeps the hook idle while disabled.
    skipIfInitialized: !PREFILL_ENABLED,
    logPrefix: 'CreateInvoice',
  });

  const vendorOrgValue: ILookupItem | null = formValues.vendorOrgId
    ? { id: formValues.vendorOrgId, name: formValues.vendorOrgName }
    : null;

  if (prefill.status === 'loading') {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: tokens.spacingVerticalL,
          padding: tokens.spacingVerticalXXL,
        }}
      >
        <Spinner size="medium" label="Analyzing uploaded files..." />
      </div>
    );
  }

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Invoice Details
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Enter the details for the new invoice.
        </Text>
      </div>

      <div className={styles.row}>
        <Field label="Invoice Number">
          <Input
            value={formValues.invoiceNumber}
            onChange={handleInvoiceNumberChange}
            placeholder="Enter invoice number"
            autoComplete="off"
          />
        </Field>
        <Field label="Invoice Date">
          <Input type="date" value={formValues.invoiceDate} onChange={handleInvoiceDateChange} />
        </Field>
      </div>

      <Field label="Name" required>
        <Input value={formValues.name} onChange={handleNameChange} placeholder="Enter invoice name" autoComplete="off" />
      </Field>

      <LookupField
        label="Vendor Organization"
        value={vendorOrgValue}
        onChange={handleVendorOrgChange}
        onSearch={handleSearchVendorOrgs}
        placeholder="Search organizations..."
      />

      <Field label="Description">
        <Textarea
          value={formValues.description}
          onChange={handleDescriptionChange}
          placeholder="Describe the invoice..."
          rows={4}
          resize="vertical"
        />
      </Field>
    </div>
  );
};
