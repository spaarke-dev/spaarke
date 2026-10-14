/**
 * RecordDeepLink - opens the record a `?record={type}:{id}` link names (spaarke-SPA-external-access-platform-r3 task 055,
 * FR-07: the grant email's `{PortalUrl}/?customer={key}&record={type}:{id}`).
 *
 * Mounted inside the auth gate, so it runs only for a signed-in caller. By then the link has already done its two other
 * jobs elsewhere, and this component does neither of them:
 *   - CUSTOMER. `?customer=` is read once, at bootstrap, by the task 041 seam (`resolveBackendSelection`): the key only
 *     chooses among the backends the directory/config returned, and an unknown key is ignored. It is never a host or scope.
 *   - SURVIVING SIGN-IN. `AuthGuard` captures `pathname + search` (customer and record included) before the sign-in
 *     redirect and restores it afterwards (`captureReturnTo` / `consumeReturnTo`), so this component simply sees the
 *     restored URL. Inside Teams the account gate never runs, so the Teams sub-page id (captured to the same key by
 *     `captureTeamsDeepLink`) is restored here, and ONLY when it carries a record link (anything else is left alone).
 *
 * What it does: parse `record` (`record-targets.ts`: exact token, GUID id, one parameter; anything else is ignored and the
 * URL is left as it is), hand the target to the shared opener (once per link), and drop `record` from the address bar so a
 * reload or a back-navigation does not reopen it - but only once the record has reached a terminal state (loaded, denied,
 * not found, or closed), never at the moment it opens: the record's read, or the bell's first poll, can trigger a token
 * redirect, and the return-to it captures is the address bar as it stands. The detail view reads the record with the caller's own token; a record outside the
 * caller's set shows its existing denied state. Nothing from the link is a URL, a host or a scope.
 */
import * as React from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { consumeReturnToWhere } from '../../auth/msal-auth';
import { useOpenRecord } from './RecordOpener';
import { parseRecordLink, searchOfPath, withoutRecordParam } from './record-targets';

export const RecordDeepLink: React.FC<{ teamsHost?: boolean }> = ({ teamsHost = false }) => {
  const location = useLocation();
  const navigate = useNavigate();
  const openRecord = useOpenRecord();
  // The record link this component has opened and not yet stripped ("{entity}:{id}"): one open per link, however many
  // times the effect below re-runs (a StrictMode double effect, a router update, the account gate's own restore).
  const openedRef = React.useRef<string | null>(null);
  // The address bar as of the latest render, so a strip that runs later (once the record settles) acts on the URL as it is
  // THEN, not on the one that was current when the record was opened.
  const locationRef = React.useRef(location);
  locationRef.current = location;

  // Teams: the account gate (and so its restore) never runs here; restore a captured record link ourselves.
  React.useEffect(() => {
    if (!teamsHost) return;
    const target = consumeReturnToWhere(path => parseRecordLink(searchOfPath(path)) !== null);
    if (target) navigate(target, { replace: true });
  }, [teamsHost, navigate]);

  React.useEffect(() => {
    const target = parseRecordLink(location.search);
    if (!target) {
      openedRef.current = null; // the link is gone (stripped, or navigated away): a later link may open again
      return; // no link, or an invalid one: ignored, URL untouched
    }
    const key = `${target.entityName}:${target.recordId}`;
    if (openedRef.current === key) return; // already opened, still waiting to settle
    openedRef.current = key;

    // Drop `record` from the address bar so a reload or a back-navigation does not reopen it - but only once the record
    // has reached a terminal state (loaded, denied, not found, or closed). While it is still being read, a token redirect
    // (the read's, or the bell's first poll) captures the CURRENT URL as its return-to and would lose the link if it were
    // already stripped. Only if the bar still names this record: if it has moved on, there is nothing of ours to remove.
    const strip = (): void => {
      const now = locationRef.current;
      const current = parseRecordLink(now.search);
      if (!current || `${current.entityName}:${current.recordId}` !== key) return;
      // Same page, the record parameter removed (built first: the removed-routes source scan reads navigate() literally).
      const withoutRecord = {
        pathname: now.pathname,
        search: withoutRecordParam(now.search),
        hash: now.hash,
      };
      navigate(withoutRecord, { replace: true });
    };
    openRecord(target, { onTerminal: strip });
  }, [location.pathname, location.search, location.hash, navigate, openRecord]);

  return null;
};

export default RecordDeepLink;
