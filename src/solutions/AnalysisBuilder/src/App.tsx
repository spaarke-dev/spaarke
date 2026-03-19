/**
 * App.tsx — Analysis Builder Code Page (Thin Wrapper)
 *
 * Delegates all UI and logic to PlaybookLibraryShell from the shared library.
 * Parses URL parameters and wires up Xrm service adapters.
 *
 * Replaces the former ~508 LOC monolith (UDSS-022).
 */

import * as React from "react";
import { PlaybookLibraryShell } from "@spaarke/ui-components/components/PlaybookLibraryShell";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";

export const App: React.FC = () => {
  const params = React.useMemo(() => parseDataParams(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);

  return (
    <PlaybookLibraryShell
      entityType={params.entityType || "sprk_document"}
      entityId={params.documentId || params.entityId || ""}
      entityDisplayName={params.documentName}
      dataService={dataService}
      authenticatedFetch={fetch.bind(window)}
      bffBaseUrl={params.apiBaseUrl || ""}
      onClose={() => {
        try { window.close(); } catch { window.history.back(); }
      }}
    />
  );
};
