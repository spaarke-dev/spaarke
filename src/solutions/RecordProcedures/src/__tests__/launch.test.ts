import { cleanId, parseLaunch } from '../services/launch';
import { MATTER_ID, PROJECT_ID, WA_ID } from './fixtures';

describe('parseLaunch', () => {
  it('reads the form-passed id (braced, upper case) as a bare lower-case id, and picks the route by the table', () => {
    expect(parseLaunch({ id: `{${MATTER_ID.toUpperCase()}}`, typename: 'sprk_matter' })).toEqual({ ok: true, core: 'matters', table: 'sprk_matter', recordId: MATTER_ID });
    expect(parseLaunch({ id: PROJECT_ID, typename: 'sprk_project' })).toMatchObject({ ok: true, core: 'projects', table: 'sprk_project' });
    expect(parseLaunch({ id: WA_ID, typename: 'sprk_workassignment' })).toMatchObject({ ok: true, core: 'workassignments', table: 'sprk_workassignment' });
  });

  it('accepts a recordId for standalone use', () => {
    expect(parseLaunch({ recordId: MATTER_ID, typename: 'sprk_matter' })).toMatchObject({ ok: true, recordId: MATTER_ID });
  });

  it('an unsaved form (no id) is a prompt, not an error and not a BFF call', () => {
    expect(parseLaunch({ typename: 'sprk_matter' })).toMatchObject({ ok: false, kind: 'unsaved', message: 'Save the matter to see its Procedures.' });
    expect(parseLaunch({ typename: 'sprk_workassignment' })).toMatchObject({ ok: false, kind: 'unsaved', message: expect.stringContaining('work assignment') });
  });

  it('an id that is not a GUID is a launch error', () => {
    expect(parseLaunch({ id: 'not-a-guid', typename: 'sprk_matter' })).toMatchObject({ ok: false, kind: 'invalid' });
    expect(parseLaunch({ id: `${MATTER_ID},${MATTER_ID}`, typename: 'sprk_matter' })).toMatchObject({ ok: false, kind: 'invalid' });
  });

  it('a table other than the three core records, or none, is a launch error (the page never guesses a route)', () => {
    expect(parseLaunch({ id: MATTER_ID, typename: 'sprk_invoice' })).toMatchObject({ ok: false, kind: 'invalid' });
    expect(parseLaunch({ id: MATTER_ID })).toMatchObject({ ok: false, kind: 'invalid' });
    expect(parseLaunch({ id: MATTER_ID, typename: 'constructor' })).toMatchObject({ ok: false, kind: 'invalid' });
  });

  it('cleanId strips braces and lower-cases', () => {
    expect(cleanId(' {ABC} ')).toBe('abc');
  });
});
