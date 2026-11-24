import { createHostContext } from '../hostContext';

const cleanGuid = (id: string | null | undefined) => (id ? id.replace(/[{}]/g, '').trim().toLowerCase() : '');
const readers = (entity: { getId?: () => unknown; getEntityName?: () => unknown } | undefined) => ({
  readEntityId: () => entity?.getId?.(),
  readEntityName: () => entity?.getEntityName?.(),
  cleanGuid,
});

describe('createHostContext', () => {
  it('returns the cleaned record id and the entity name from the host form', () => {
    const { getHostRecordId, getHostEntityName } = createHostContext(
      readers({ getId: () => '{ABC-DEF}', getEntityName: () => 'sprk_communication' })
    );
    expect(getHostRecordId()).toBe('abc-def');
    expect(getHostEntityName()).toBe('sprk_communication');
  });

  it('returns undefined when there is no form, an empty value, or the host probe throws', () => {
    expect(createHostContext(readers(undefined)).getHostRecordId()).toBeUndefined();
    expect(
      createHostContext(readers({ getId: () => '', getEntityName: () => '' })).getHostEntityName()
    ).toBeUndefined();
    const throwing = createHostContext({
      readEntityId: () => {
        throw new Error('x');
      },
      readEntityName: () => {
        throw new Error('x');
      },
      cleanGuid,
    });
    expect(throwing.getHostRecordId()).toBeUndefined();
    expect(throwing.getHostEntityName()).toBeUndefined();
  });
});
