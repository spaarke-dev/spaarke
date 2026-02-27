/**
 * InvoicesTab — embedded tab wrapper for the Invoices record list.
 *
 * Uses useInvoicesList with broad filter and renders RecordCardList
 * with RecordCard items.
 */

import * as React from "react";
import { ReceiptRegular } from "@fluentui/react-icons";
import { DataverseService } from "../../services/DataverseService";
import { useInvoicesList } from "../../hooks/useInvoicesList";
import { RecordCard } from "./RecordCard";
import { RecordCardList } from "./RecordCardList";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IInvoicesTabProps {
  service: DataverseService;
  userId: string;
  contactId: string | null;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const InvoicesTab: React.FC<IInvoicesTabProps> = ({
  service,
  userId,
  contactId,
  onCountChange,
  onRefetchReady,
}) => {
  const { invoices, isLoading, error, totalCount, refetch } = useInvoicesList(
    service,
    userId,
    contactId,
    { top: 50 }
  );

  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [refetch, onRefetchReady]);

  return (
    <RecordCardList
      totalCount={totalCount}
      isLoading={isLoading}
      error={error}
      ariaLabel="Invoices list"
    >
      {invoices.map((invoice) => {
        const dateStr = invoice.sprk_invoicedate
          ? new Date(invoice.sprk_invoicedate).toLocaleDateString()
          : undefined;

        return (
          <RecordCard
            key={invoice.sprk_invoiceid}
            icon={ReceiptRegular}
            iconLabel="Invoice"
            entityName="sprk_invoice"
            entityId={invoice.sprk_invoiceid}
            title={invoice.sprk_name}
            primaryFields={[
              invoice.sprk_invoicenumber,
              dateStr,
              invoice.sprk_vendororg,
            ].filter(Boolean) as string[]}
            statusBadge={invoice.statuscodeName || undefined}
            description={invoice.sprk_description}
          />
        );
      })}
    </RecordCardList>
  );
};
