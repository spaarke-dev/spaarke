/**
 * launchAssociation — the Associate-To pre-fill the Create Project / Create Matter hosts take from their launch envelope
 * (unified-access-control-r2 task 195, #2352). Both hosts call this with their partner table; pinned:
 *   - a launch from the partner record pre-selects it and locks the step (braces/case cleaned; name defaults to '');
 *   - no launch record, another table, or an id that is not a GUID → nothing pre-selected, nothing locked.
 */
import { launchAssociationFromParams } from '../launchAssociation';

const MATTER_ID = 'e1950000-0000-4000-8000-0000000000aa';

describe('launchAssociationFromParams (task 195)', () => {
  it('a matter launch pre-selects the matter and locks the step (Create Project host)', () => {
    expect(
      launchAssociationFromParams(
        { entityType: 'sprk_matter', entityId: `{${MATTER_ID.toUpperCase()}}`, recordName: 'MAT-1' },
        'sprk_matter'
      )
    ).toEqual({
      initialAssociation: { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'MAT-1' },
      lockAssociation: true,
    });
  });

  it('a project launch pre-selects the project and locks the step (Create Matter host); the name defaults to ""', () => {
    expect(launchAssociationFromParams({ entityType: 'sprk_project', entityId: MATTER_ID }, 'sprk_project')).toEqual({
      initialAssociation: { entityType: 'sprk_project', recordId: MATTER_ID, recordName: '' },
      lockAssociation: true,
    });
  });

  it.each([
    ['no launch record', {}],
    ['an entity type with no id', { entityType: 'sprk_matter' }],
    ['an id with no entity type', { entityId: MATTER_ID }],
    ['another (unknown) entity type', { entityType: 'sprk_invoice', entityId: MATTER_ID, recordName: 'INV-1' }],
    ['the wizard’s own table', { entityType: 'sprk_project', entityId: MATTER_ID }],
    ['an id that is not a GUID', { entityType: 'sprk_matter', entityId: 'not-a-guid', recordName: 'MAT-1' }],
  ])('%s: nothing pre-selected, nothing locked', (_label, params) => {
    expect(launchAssociationFromParams(params as Record<string, string>, 'sprk_matter')).toEqual({
      initialAssociation: undefined,
      lockAssociation: false,
    });
  });
});
