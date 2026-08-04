/**
 * ensureAssociationColumns.ts
 *
 * Injects the association columns the left-list status dot (🔴/🟡/🟢) depends on
 * into a saved view's FetchXML BEFORE it runs (email-communication-solution-r5,
 * owner UAT 2026-08-03 Item 3). A maker-authored inbox view ("All Incoming
 * Email", "Email — Inbox", …) frequently selects only envelope columns and
 * omits the association status/provenance/typed-lookup columns — so
 * `deriveCardReviewTone` (`EmailWorkspace.mapping.ts`) had no data and returned
 * `undefined`, dropping the dot. Rather than force makers to hand-edit every
 * view, this hook augments the FetchXML at run time so the dot always resolves,
 * regardless of the maker's column set.
 *
 * Pure + defensive: never throws. Any parse/serialize failure (or a
 * `<parsererror>` from malformed XML) degrades to the ORIGINAL `fetchXml`
 * unchanged — the maker's view still runs, just without the injected columns.
 *
 * No React import (safe to unit-test without a DOM/provider — jsdom supplies
 * `DOMParser`/`XMLSerializer`).
 */
import { COMMUNICATION_REGARDING_FIELDS } from '../../logic/connections';

/**
 * The association attribute names the card status dot resolves from. Beyond the
 * denormalized name/number + raw status/provenance + `sprk_regardingrecordtype`
 * lookup, every typed regarding lookup (`sprk_regardingmatter`,
 * `sprk_regardingperson`, …) is included so `readFiledAssociations` can see the
 * filed record on a view that only surfaces those.
 */
const REQUIRED_ATTRIBUTES: ReadonlyArray<string> = [
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
  'sprk_associationstatus',
  'sprk_associationprovenance',
  'sprk_regardingrecordtype',
  ...COMMUNICATION_REGARDING_FIELDS.map(f => f.field),
];

/**
 * Return `fetchXml` with the association columns the card status dot needs
 * appended to its first `<entity>`, so the dot resolves regardless of the
 * maker's column set. Idempotent (never duplicates an already-selected column)
 * and a no-op when the entity uses `<all-attributes/>`. On ANY parse/serialize
 * error — or malformed XML that yields a `<parsererror>` — the ORIGINAL string
 * is returned unchanged (degrade to the maker's view, never throw).
 *
 * @param fetchXml The saved view's raw FetchXML.
 */
export function ensureAssociationColumns(fetchXml: string): string {
  try {
    const doc = new DOMParser().parseFromString(fetchXml, 'application/xml');

    // DOMParser emits a <parsererror> element in the doc on malformed XML —
    // treat that as a parse failure and degrade to the maker's view.
    if (doc.getElementsByTagName('parsererror').length > 0) {
      return fetchXml;
    }

    const entity = doc.getElementsByTagName('entity')[0];
    if (!entity) {
      return fetchXml;
    }

    // `<all-attributes/>` already selects every column — nothing to inject.
    if (entity.getElementsByTagName('all-attributes').length > 0) {
      return fetchXml;
    }

    const present = new Set(
      Array.from(entity.getElementsByTagName('attribute'))
        .map(attr => attr.getAttribute('name'))
        .filter((name): name is string => name != null)
    );

    for (const name of REQUIRED_ATTRIBUTES) {
      if (present.has(name)) continue;
      const attr = doc.createElement('attribute');
      attr.setAttribute('name', name);
      entity.appendChild(attr);
      present.add(name);
    }

    return new XMLSerializer().serializeToString(doc);
  } catch {
    // Never throw — a maker's view must still run if augmentation fails.
    return fetchXml;
  }
}
