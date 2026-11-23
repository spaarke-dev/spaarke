/**
 * Side-pane lookup filters shared by wizard lookups (owner D-219, 2026-11-12: person lookups show active contacts only).
 */
import type { LookupOptions } from '../../types/serviceInterfaces';

/** FetchXML condition: an active contact (statecode 0). */
export const ACTIVE_CONTACT_CONDITION_XML = '<condition attribute="statecode" operator="eq" value="0"/>';

/** Limits a contact side pane to active contacts. */
export const ACTIVE_CONTACT_LOOKUP_FILTERS: NonNullable<LookupOptions['filters']> = [
  { entityLogicalName: 'contact', filterXml: `<filter>${ACTIVE_CONTACT_CONDITION_XML}</filter>` },
];

/**
 * An internal person who can take part in a Direct conversation (fm task 055): an enabled user who is not an application
 * user, other than the signed-in user. A convenience for the side pane only; the server applies the same rule when the
 * conversation is started (`POST /api/communications/threads/direct`).
 */
export const INTERNAL_USER_CONDITIONS_XML =
  '<condition attribute="isdisabled" operator="eq" value="0"/>' +
  '<condition attribute="applicationid" operator="null"/>' +
  '<condition attribute="systemuserid" operator="ne-userid"/>';

/** Limits a systemuser side pane to the people a Direct conversation can be started with. */
export const INTERNAL_USER_LOOKUP_FILTERS: NonNullable<LookupOptions['filters']> = [
  { entityLogicalName: 'systemuser', filterXml: `<filter>${INTERNAL_USER_CONDITIONS_XML}</filter>` },
];
