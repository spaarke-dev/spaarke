/**
 * ISS-008 (#2948): the communications grid page's record scoping and its "+ New Email" / "+ New Message" commands.
 * Each test fails if the branch it names is broken.
 */
import {
  IMPOSSIBLE_HOST_FILTER,
  NEW_MESSAGE_NO_PANEL_TEXT,
  NEW_MESSAGE_UNAVAILABLE_TEXT,
  buildHostCommands,
  hostFiltersFor,
  newEmail,
  newMessage,
  parseRegardingEnvelope,
  resolveHostRecord,
  type FormEntityLike,
  type HostCommandDeps,
} from '../hostCommands';

const MATTER_ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const OTHER_ID = '9a8b7c6d-1111-2222-3333-444455556666';

function scopedSearch(id = MATTER_ID): string {
  const data = new URLSearchParams({ regardingAttribute: 'sprk_regardingmatter', regardingValue: id }).toString();
  return `?data=${encodeURIComponent(data)}`;
}

function matterForm(id = `{${MATTER_ID.toUpperCase()}}`, name: unknown = 'MAT-000113'): FormEntityLike {
  return {
    getEntityName: () => 'sprk_matter',
    getId: () => id,
    getPrimaryAttributeValue: () => name,
  };
}

function fakeDeps(entity: FormEntityLike | undefined, panelResult: unknown = 'opened'): HostCommandDeps & {
  openEmailCompose: jest.Mock;
  openConversationPanel: jest.Mock;
  alert: jest.Mock;
  warn: jest.Mock;
} {
  return {
    readFormEntity: () => entity,
    openEmailCompose: jest.fn(),
    openConversationPanel: jest.fn(() => panelResult),
    alert: jest.fn(),
    warn: jest.fn(),
  };
}

describe('parseRegardingEnvelope', () => {
  it('returns none when neither key is present (global mode)', () => {
    expect(parseRegardingEnvelope('')).toEqual({ kind: 'none' });
    expect(parseRegardingEnvelope('?foo=bar')).toEqual({ kind: 'none' });
  });

  it('returns valid for both keys inside the data envelope (the onLoad script shape)', () => {
    expect(parseRegardingEnvelope(scopedSearch())).toEqual({
      kind: 'valid',
      hostFilters: [{ attribute: 'sprk_regardingmatter', operator: 'eq', value: MATTER_ID }],
    });
  });

  it('returns valid for both keys at the top level', () => {
    const env = parseRegardingEnvelope(`?regardingAttribute=sprk_regardingmatter&regardingValue=${MATTER_ID}`);
    expect(env.kind).toBe('valid');
  });

  it('returns malformed when only one key is present, at the top level or in the envelope', () => {
    expect(parseRegardingEnvelope('?regardingAttribute=sprk_regardingmatter')).toEqual({ kind: 'malformed' });
    expect(parseRegardingEnvelope(`?data=${encodeURIComponent(`regardingValue=${MATTER_ID}`)}`)).toEqual({
      kind: 'malformed',
    });
  });

  it('never mixes a top-level key with the other key from the data envelope', () => {
    const search = `?regardingAttribute=sprk_regardingmatter&data=${encodeURIComponent(`regardingValue=${MATTER_ID}`)}`;
    expect(parseRegardingEnvelope(search)).toEqual({ kind: 'malformed' });
  });
});

describe('hostFiltersFor', () => {
  it('scopes to the envelope, fails closed when malformed, and adds nothing in global mode', () => {
    const valid = parseRegardingEnvelope(scopedSearch());
    expect(hostFiltersFor(valid)).toEqual([{ attribute: 'sprk_regardingmatter', operator: 'eq', value: MATTER_ID }]);
    expect(hostFiltersFor({ kind: 'malformed' })).toBe(IMPOSSIBLE_HOST_FILTER);
    expect(hostFiltersFor({ kind: 'none' })).toBeUndefined();
  });
});

describe('resolveHostRecord', () => {
  const valid = parseRegardingEnvelope(scopedSearch());

  it('returns the form record when its id equals the scoped id (braces and casing ignored)', () => {
    expect(resolveHostRecord(valid, () => matterForm())).toEqual({
      entityType: 'sprk_matter',
      id: MATTER_ID,
      name: 'MAT-000113',
    });
  });

  it('returns undefined when the form id differs from the id the grid is scoped to', () => {
    expect(resolveHostRecord(valid, () => matterForm(OTHER_ID))).toBeUndefined();
  });

  it('returns undefined without a valid envelope, a form entity, or when the form read throws', () => {
    expect(resolveHostRecord({ kind: 'none' }, () => matterForm())).toBeUndefined();
    expect(resolveHostRecord({ kind: 'malformed' }, () => matterForm())).toBeUndefined();
    expect(resolveHostRecord(valid, () => undefined)).toBeUndefined();
    expect(
      resolveHostRecord(valid, () => {
        throw new Error('no form');
      })
    ).toBeUndefined();
  });

  it('drops a non-string or empty display name', () => {
    expect(resolveHostRecord(valid, () => matterForm(MATTER_ID, 42))?.name).toBeUndefined();
    expect(resolveHostRecord(valid, () => matterForm(MATTER_ID, ''))?.name).toBeUndefined();
  });
});

describe('buildHostCommands', () => {
  it('offers New Message only for a valid envelope (a record form)', () => {
    const ids = (search: string) =>
      buildHostCommands(parseRegardingEnvelope(search), fakeDeps(matterForm())).map(c => c.id);
    expect(ids(scopedSearch())).toEqual(['new-email', 'new-message']);
    expect(ids('')).toEqual(['new-email']);
    expect(ids('?regardingAttribute=sprk_regardingmatter')).toEqual(['new-email']);
  });

  it('wires each command to its handler', () => {
    const deps = fakeDeps(matterForm());
    const [email, message] = buildHostCommands(parseRegardingEnvelope(scopedSearch()), deps);
    void email.onInvoke();
    void message.onInvoke();
    expect(deps.openEmailCompose).toHaveBeenCalledTimes(1);
    expect(deps.openConversationPanel).toHaveBeenCalledTimes(1);
  });
});

describe('newEmail', () => {
  it('opens the composer Related to the host record on a record form', () => {
    const deps = fakeDeps(matterForm());
    newEmail(parseRegardingEnvelope(scopedSearch()), deps);
    expect(deps.openEmailCompose).toHaveBeenCalledWith({
      mode: 'new',
      regarding: { entityType: 'sprk_matter', id: MATTER_ID, name: 'MAT-000113' },
    });
    expect(deps.warn).not.toHaveBeenCalled();
  });

  it('opens without Related to in global mode, silently', () => {
    const deps = fakeDeps(matterForm());
    newEmail({ kind: 'none' }, deps);
    expect(deps.openEmailCompose).toHaveBeenCalledWith({ mode: 'new', regarding: undefined });
    expect(deps.warn).not.toHaveBeenCalled();
  });

  it('opens without Related to, and warns, when the host record cannot be read on a record form', () => {
    const deps = fakeDeps(matterForm(OTHER_ID));
    newEmail(parseRegardingEnvelope(scopedSearch()), deps);
    expect(deps.openEmailCompose).toHaveBeenCalledWith({ mode: 'new', regarding: undefined });
    expect(deps.warn).toHaveBeenCalledTimes(1);
  });
});

describe('newMessage', () => {
  it('opens the Messages panel for the host record and shows no alert', () => {
    const deps = fakeDeps(matterForm());
    newMessage(parseRegardingEnvelope(scopedSearch()), deps);
    expect(deps.openConversationPanel).toHaveBeenCalledWith({ entityType: 'sprk_matter', id: MATTER_ID });
    expect(deps.alert).not.toHaveBeenCalled();
  });

  it('shows the did-not-respond alert when no panel answers', () => {
    const deps = fakeDeps(matterForm(), 'no-panel');
    newMessage(parseRegardingEnvelope(scopedSearch()), deps);
    expect(deps.alert).toHaveBeenCalledWith(NEW_MESSAGE_NO_PANEL_TEXT);
  });

  it('shows the cannot-open alert, without calling the panel, when the host record cannot be read', () => {
    const deps = fakeDeps(undefined);
    newMessage(parseRegardingEnvelope(scopedSearch()), deps);
    expect(deps.openConversationPanel).not.toHaveBeenCalled();
    expect(deps.alert).toHaveBeenCalledWith(NEW_MESSAGE_UNAVAILABLE_TEXT);
  });
});
