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
