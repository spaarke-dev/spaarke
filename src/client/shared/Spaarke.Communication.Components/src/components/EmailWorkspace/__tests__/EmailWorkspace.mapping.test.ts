/**
 * EmailWorkspace.mapping.test.ts
 *
 * Unit tests for the card-row review-dot wiring in `mapRowToEmailCardItem`
 * (owner UAT 2026-07-30 R2 item 5). The left-list status dot went missing
 * because the "does this row carry association data?" gate ignored the
 * DENORMALIZED `sprk_regardingrecordname`/`sprk_regardingrecordnumber` columns —
 * the ones a maker most commonly puts on an inbox saved view. These tests pin the
 * fix: a filed email whose view surfaces only the Related-to name now derives a
 * `reviewTone`, while a row with no association columns still derives none (so a
 * view that selects no association data never renders a misleading all-red list).
 */
import { mapRowToEmailCardItem } from '../EmailWorkspace.mapping';

const EMAIL_TYPE = 100000000;

const BASE_ROW = {
  sprk_communicationid: 'c1',
  sprk_from: 'jane.doe@example.com',
  sprk_subject: 'Quarterly filing update',
  sprk_communicationtype: EMAIL_TYPE,
} as const;

describe('mapRowToEmailCardItem — review dot wiring (owner UAT R2 item 5)', () => {
  it('derives a reviewTone from the DENORMALIZED regarding name alone (the regression fix)', () => {
    const item = mapRowToEmailCardItem({
      ...BASE_ROW,
      // No status / provenance / typed lookup columns — only the denorm name+number,
      // exactly what an inbox view's "Related to" column surfaces.
      sprk_regardingrecordname: 'Acme v Beta',
      sprk_regardingrecordnumber: 'MAT-1',
    });
    expect(item.reviewTone).toBeDefined();
  });

  it('derives a reviewTone from an association status column', () => {
    const item = mapRowToEmailCardItem({ ...BASE_ROW, sprk_associationstatus: 100000000 });
    expect(item.reviewTone).toBeDefined();
  });

  it('derives NO reviewTone (no dot) when the row carries no association data at all', () => {
    const item = mapRowToEmailCardItem({ ...BASE_ROW });
    expect(item.reviewTone).toBeUndefined();
  });
});
