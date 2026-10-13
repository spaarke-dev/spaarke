/**
 * trademarkTerritories - the Trademark Search territory (jurisdiction) list (r3 task 053; owner decision 2026-10-12,
 * answer 4: the list is FIXED in R3 and configurable per customer later).
 *
 * This is the ONE module that owns the list, mirroring `policyTopics.ts`. The CODES are the stored value
 * (`answers.territories`), so they are stable: never rename or reuse one. A later per-customer/per-environment source
 * replaces `getTrademarkTerritories()` without a data migration, because the stored answers hold codes and the detail view
 * maps codes to labels. `other` is special: it asks for free text (`territoriesOther`) and is always offered last.
 *
 * Labels follow `notes/intake-form-design.md` section 4.2. There is deliberately NO trademark-class list: the design note
 * (section 4.1, takeaway 2) has the requester describe the goods and services in words and Legal assign the Nice classes.
 */

export interface TrademarkTerritory {
  /** Stable code stored in the answers. */
  code: string;
  /** What the requester reads. */
  label: string;
}

/** The code that asks for free text. */
export const TRADEMARK_TERRITORY_OTHER = 'other';

const FIXED_TRADEMARK_TERRITORIES: readonly TrademarkTerritory[] = [
  { code: 'us', label: 'United States' },
  { code: 'ca', label: 'Canada' },
  { code: 'eu', label: 'European Union' },
  { code: 'uk', label: 'United Kingdom' },
  { code: 'cn', label: 'China' },
  { code: 'jp', label: 'Japan' },
  { code: 'in', label: 'India' },
  { code: 'au', label: 'Australia' },
  { code: 'worldwide', label: 'Worldwide / online only' },
  { code: TRADEMARK_TERRITORY_OTHER, label: 'Other' },
];

/** The territories the wizard offers. The seam a per-customer source replaces (keep `other` last). */
export function getTrademarkTerritories(): readonly TrademarkTerritory[] {
  return FIXED_TRADEMARK_TERRITORIES;
}

/** The label for a code, or `undefined` for a code that is not in the list. */
export function trademarkTerritoryLabel(code: string): string | undefined {
  return getTrademarkTerritories().find(t => t.code === code)?.label;
}
