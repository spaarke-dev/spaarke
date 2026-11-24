import { createHostContext } from '../hostContext';

const cleanGuid = (id: string | null | undefined) => (id ? id.replace(/[{}]/g, '').trim().toLowerCase() : '');
const xrmWith = (entity: unknown) => () => ({ Page: { data: { entity } } });

describe('createHostContext', () => {
  it('returns the cleaned record id and the entity name from the host form', () => {
    const { getHostRecordId, getHostEntityName } = createHostContext({
      getXrm: xrmWith({ getId: () => '{ABC-DEF}', getEntityName: () => 'sprk_communication' }),
      cleanGuid,
    });
    expect(getHostRecordId()).toBe('abc-def');
    expect(getHostEntityName()).toBe('sprk_communication');
  });

  it('returns undefined when there is no form, an empty value, or Xrm throws', () => {
    expect(createHostContext({ getXrm: () => undefined, cleanGuid }).getHostRecordId()).toBeUndefined();
    expect(
      createHostContext({
        getXrm: xrmWith({ getId: () => '', getEntityName: () => '' }),
        cleanGuid,
      }).getHostEntityName()
    ).toBeUndefined();
    const throwing = createHostContext({
      getXrm: xrmWith({
        getId: () => {
          throw new Error('x');
        },
      }),
      cleanGuid,
    });
    expect(throwing.getHostRecordId()).toBeUndefined();
  });
});
