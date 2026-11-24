/**
 * ISS-008 (#2948): how the Email code page reads its launch — compose mode and its "Related to" record (#2908),
 * a single record, or the list. Each test fails if the branch it names is broken.
 */
import { resolveEmailLaunch, type HostFormXrm } from '../emailLaunch';
import { composeRegardingParams } from '@spaarke/ui-components';

const COMM_ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const MATTER_ID = '9a8b7c6d-1111-2222-3333-444455556666';
const noHost = (): HostFormXrm | null => null;

const regardingQuery = composeRegardingParams({ entityType: 'sprk_matter', id: MATTER_ID, name: 'MAT-000113' });
const expectedRegarding = { entityType: 'sprk_matter', id: MATTER_ID, name: 'MAT-000113' };

describe('compose mode', () => {
  it('reads compose and Related to from the data envelope (openEmailCompose / the secure-child ribbon)', () => {
    const data = `compose=new&${regardingQuery}`;
    expect(resolveEmailLaunch(`?data=${encodeURIComponent(data)}`, noHost)).toEqual({
      hideList: false,
      compose: { mode: 'new', sourceId: undefined, regarding: expectedRegarding },
    });
  });

  it('reads compose and Related to from top-level params', () => {
    const launch = resolveEmailLaunch(`?compose=new&${regardingQuery}`, noHost);
    expect(launch.compose).toEqual({ mode: 'new', sourceId: undefined, regarding: expectedRegarding });
  });

  it('reads Related to only from the scope the compose key came from', () => {
    // compose at the top level, regarding only inside data: not used.
    const search = `?compose=new&data=${encodeURIComponent(regardingQuery)}`;
    expect(resolveEmailLaunch(search, noHost).compose?.regarding).toBeUndefined();
  });

  it('opens a New email with no Related to when none is passed', () => {
    expect(resolveEmailLaunch('?data=compose%3Dnew', noHost).compose).toEqual({
      mode: 'new',
      sourceId: undefined,
      regarding: undefined,
    });
  });

  it('ignores Related to for reply, reply-all and forward, and keeps their source id', () => {
    for (const mode of ['reply', 'replyAll', 'forward']) {
      const data = `compose=${mode}&id={${COMM_ID.toUpperCase()}}&${regardingQuery}`;
      expect(resolveEmailLaunch(`?data=${encodeURIComponent(data)}`, noHost).compose).toEqual({
        mode,
        sourceId: COMM_ID,
        regarding: undefined,
      });
    }
  });

  it('treats an unknown compose mode as a New email', () => {
    expect(resolveEmailLaunch('?compose=bogus', noHost).compose?.mode).toBe('new');
  });

  it('drops an invalid Related to record but still opens the composer', () => {
    const data = 'compose=new&regardingType=sprk_matter&regardingId=not-a-guid';
    const launch = resolveEmailLaunch(`?data=${encodeURIComponent(data)}`, noHost);
    expect(launch.compose).toEqual({ mode: 'new', sourceId: undefined, regarding: undefined });
  });
});

describe('record and list mode', () => {
  it('reads a bare record id from data, with a folded single flag', () => {
    expect(resolveEmailLaunch(`?data=${encodeURIComponent(`${COMM_ID}&single=1`)}`, noHost)).toEqual({
      initialSelectedId: COMM_ID,
      hideList: true,
    });
    expect(resolveEmailLaunch(`?data=${COMM_ID}`, noHost)).toEqual({ initialSelectedId: COMM_ID, hideList: false });
  });

  it('falls back to the id param, with view=record at the top level', () => {
    expect(resolveEmailLaunch(`?id=${COMM_ID}&view=record`, noHost)).toEqual({
      initialSelectedId: COMM_ID,
      hideList: true,
    });
  });

  it('reads the embedded sprk_communication form record when no param gives an id', () => {
    const host: HostFormXrm = {
      Utility: { getPageContext: () => ({ input: { entityName: 'sprk_communication', entityId: `{${COMM_ID}}` } }) },
    };
    expect(resolveEmailLaunch('', () => host)).toEqual({ initialSelectedId: COMM_ID, hideList: false });
  });

  it('ignores a page context for another table and uses the form entity id instead', () => {
    const host: HostFormXrm = {
      Utility: { getPageContext: () => ({ input: { entityName: 'sprk_matter', entityId: MATTER_ID } }) },
      Page: { data: { entity: { getId: () => COMM_ID } } },
    };
    expect(resolveEmailLaunch('', () => host).initialSelectedId).toBe(COMM_ID);
  });

  it('shows the list when nothing resolves, even with a single flag, and when the host read throws', () => {
    expect(resolveEmailLaunch('?single=1', noHost)).toEqual({ initialSelectedId: undefined, hideList: false });
    expect(
      resolveEmailLaunch('', () => {
        throw new Error('not embedded');
      })
    ).toEqual({ initialSelectedId: undefined, hideList: false });
  });
});
