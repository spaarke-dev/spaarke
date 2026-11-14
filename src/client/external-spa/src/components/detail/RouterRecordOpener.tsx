import * as React from 'react';
import { useNavigate } from 'react-router-dom';
import { RecordOpenerProvider } from './RecordOpener';

/**
 * The shell's one record opener, given the router's `navigate` (mount it inside the router). The record template and
 * `RecordOpener` import no router, so the template's "Submit invoice" action (R4 task 007, FR-10) leaves through this, as
 * the Invoices widget does through its host. Used by `App.tsx` and by the shell test (`tests/InvoiceSubmitShell.test.tsx`),
 * so the test runs the production bridge rather than a copy.
 */
export const RouterRecordOpener: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const navigate = useNavigate();
  return <RecordOpenerProvider onNavigate={navigate}>{children}</RecordOpenerProvider>;
};
