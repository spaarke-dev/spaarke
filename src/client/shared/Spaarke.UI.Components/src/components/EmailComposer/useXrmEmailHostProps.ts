/**
 * useXrmEmailHostProps — the Xrm-backed host wiring every Dataverse mount of the Email surface needs (#2973).
 *
 * `EmailWorkspace` (`@spaarke/communication-components`) is host-agnostic (ADR-012): it takes its adapters and
 * compose handlers as props. Its Dataverse hosts — the SpaarkeAi `email` widget, the LegalWorkspace `email` section
 * and the standalone `sprk_emailpage` code page (both its reading surface and its compose-only window) — each built
 * the same set by hand, so every new compose capability (local-attachment upload, share links, templates, AI draft)
 * had to be added to each host, and a host that missed one showed a composer without it. This hook builds the set
 * once; hosts spread its result and add only what differs per host (`webApi`, `authenticatedFetch`, `bffBaseUrl`,
 * selection callbacks).
 *
 * It lives here, beside `createXrmEmailComposeHandlers`, because every piece is an Xrm adapter from this package;
 * the component package stays free of Xrm.
 */
import * as React from 'react';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { XrmDataverseClient } from '../../services/XrmDataverseClient';
import { searchUsersAndContacts } from '../../services/userLookup';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { ILookupItem } from '../../types/LookupTypes';
import { createXrmDataService } from '../../utils/adapters/xrmDataServiceAdapter';
import { createXrmNavigationService } from '../../utils/adapters/xrmNavigationServiceAdapter';
import { getXrm } from '../../utils/xrmContext';
import {
  createXrmEmailComposeHandlers,
  resolveCurrentUserEmail,
  type XrmEmailComposeHandlers,
} from './createXrmEmailComposeHandlers';

export interface XrmEmailHostProps extends XrmEmailComposeHandlers {
  /** Xrm-backed Dataverse client — one per mount (`XrmDataverseClient` is meant to be instantiated once). */
  dataverseClient: XrmDataverseClient;
  dataService: IDataService;
  navigationService: INavigationService;
  /** Recipient typeahead (users and contacts). */
  onSearchRecipients: (query: string) => Promise<ILookupItem[]>;
  /** The signed-in user's mailbox for the compose "From:" row; `undefined` until resolved (or when unset). */
  fromMailbox?: string;
  /** The org URL, for attachment and record deep links; empty outside a Dataverse host. */
  dataverseUrl: string;
}

export interface UseXrmEmailHostPropsOptions {
  /** Auth-aware fetch (ADR-028). With `bffBaseUrl`, enables upload, template render and AI draft. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** BFF base URL (host only, no `/api`). `null`, `undefined` or empty while it is still resolving. */
  bffBaseUrl?: string | null;
}

/**
 * Build the Xrm-backed props for an Email surface mount. The adapters are created once per mount; the compose
 * handlers are rebuilt when `authenticatedFetch` or `bffBaseUrl` changes (for example once the BFF URL resolves);
 * the "From:" mailbox resolves once, asynchronously.
 */
export function useXrmEmailHostProps({
  authenticatedFetch,
  bffBaseUrl,
}: UseXrmEmailHostPropsOptions): XrmEmailHostProps {
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const onSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(dataService, query),
    [dataService]
  );

  // Pass auth + BFF URL so the factory also builds the BFF-backed handlers (local-attachment upload, template
  // render, AI draft). An empty or unresolved URL leaves them out, exactly as before.
  const composeHandlers = React.useMemo(
    () => createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl: bffBaseUrl || undefined }),
    [authenticatedFetch, bffBaseUrl]
  );

  const dataverseUrl = React.useMemo(
    () => getXrm('clientUrl')?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '',
    []
  );

  const [fromMailbox, setFromMailbox] = React.useState<string | undefined>();
  React.useEffect(() => {
    let cancelled = false;
    void resolveCurrentUserEmail().then(email => {
      if (!cancelled) setFromMailbox(email);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  return React.useMemo(
    () => ({
      ...composeHandlers,
      dataverseClient,
      dataService,
      navigationService,
      onSearchRecipients,
      fromMailbox,
      dataverseUrl,
    }),
    [composeHandlers, dataverseClient, dataService, navigationService, onSearchRecipients, fromMailbox, dataverseUrl]
  );
}
