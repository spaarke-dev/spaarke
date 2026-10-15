/**
 * R3 task 055 (FR-07 client) - `?customer={key}&record={type}:{id}` opens that record's detail view after sign-in.
 *
 * What runs for real: `RecordDeepLink`, `RecordOpenerProvider`, react-router's location, the `record-targets` parser, the
 * return-to helpers in msal-auth (the same ones AuthGuard uses to carry a URL across the sign-in redirect), the task 041
 * backend-selection seam (`resolveBackendSelection`), and, for the denied case, the real `RecordDetailModal`. What is
 * substituted: the network seam (`bffApiCall`) and, outside the denied case, the detail view (a stub that records which
 * entity and id it was asked to show).
 *
 * The SERVER decides access. A link can only ASK to open a record; these tests prove that a malformed, ambiguous or unknown
 * link asks for nothing, that a valid one asks for exactly the entity and id it names, that it never reaches the network by
 * itself, and that a record the caller cannot see shows the detail view's existing denied state.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MemoryRouter, useLocation } from 'react-router-dom';

const h = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  real: { value: false },
  // The recording stub reports a settled record (as the real modal does once the read answers) unless a test holds it back.
  settle: { value: true },
  stubMounts: { count: 0 },
}));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

// The detail view: a recording stub, or (h.real) the real modal for the denied-state case.
vi.mock('../src/components/detail/RecordDetailModal', async () => {
  const actual = await vi.importActual<typeof import('../src/components/detail/RecordDetailModal')>(
    '../src/components/detail/RecordDetailModal'
  );
  type Props = React.ComponentProps<typeof actual.RecordDetailModal>;
  const Stub = (props: Props): React.ReactElement => {
    React.useEffect(() => {
      h.stubMounts.count += 1;
    }, []);
    const { onSettled } = props;
    React.useEffect(() => {
      if (h.settle.value) onSettled?.();
    }, [onSettled]);
    return React.createElement(
      'div',
      {
        role: 'dialog',
        'aria-label': 'Record detail',
        'data-testid': 'detail',
        'data-entity': props.entityName,
        'data-id': props.recordIds[props.index],
      },
      React.createElement('button', { onClick: props.onClose }, 'Close detail')
    );
  };
  return {
    RecordDetailModal: (props: Props) =>
      h.real.value ? React.createElement(actual.RecordDetailModal, props) : React.createElement(Stub, props),
  };
});

import { ApiError } from '../src/types';
import { RecordDeepLink } from '../src/components/detail/RecordDeepLink';
import { RecordOpenerProvider } from '../src/components/detail/RecordOpener';
import {
  FEED_RECORD_TYPES,
  RECORD_LINK_TOKENS,
  hasRecordParam,
  parseRecordLink,
  recordTargetOfFeedItem,
  searchOfPath,
  withoutRecordParam,
} from '../src/components/detail/record-targets';
import { captureReturnTo, captureTeamsDeepLink, consumeReturnTo } from '../src/auth/msal-auth';
import { resolveBackendSelection, type Backend, type BackendDirectory } from '../src/auth/backend-selection';

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ID = guid(42);

/** Shows the router's current URL so a test can see what the deep link left in the address bar. */
const Where: React.FC = () => {
  const location = useLocation();
  return <output data-testid="where">{location.pathname + location.search}</output>;
};

function renderAt(url: string, teamsHost = false): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[url]}>
        <RecordOpenerProvider>
          <RecordDeepLink teamsHost={teamsHost} />
          <Where />
        </RecordOpenerProvider>
      </MemoryRouter>
    </FluentProvider>
  );
}

const where = (): string => screen.getByTestId('where').textContent ?? '';

beforeEach(() => {
  h.real.value = false;
  h.settle.value = true;
  h.stubMounts.count = 0;
  h.bffApiCall.mockReset();
  sessionStorage.clear();
  localStorage.clear();
});
afterEach(() => {
  vi.restoreAllMocks();
});

const SLOW = { timeout: 60000 };

// ---------------------------------------------------------------------------
// The parser
// ---------------------------------------------------------------------------

describe('parseRecordLink', () => {
  it.each(Object.entries(RECORD_LINK_TOKENS))('accepts the %s token (-> %s)', (token, entity) => {
    expect(parseRecordLink(`?record=${token}:${ID}`)).toEqual({ entityName: entity, recordId: ID });
  });

  it('accepts the three tokens the grant email writes, URL-encoded, with the customer key alongside', () => {
    for (const [token, entity] of [
      ['project', 'sprk_project'],
      ['matter', 'sprk_matter'],
      ['workassignment', 'sprk_workassignment'],
    ]) {
      const search = `?customer=${encodeURIComponent('acme-1')}&record=${encodeURIComponent(`${token}:${ID}`)}`;
      expect(parseRecordLink(search)).toEqual({ entityName: entity, recordId: ID });
    }
  });

  it('lower-cases the id', () => {
    expect(parseRecordLink(`?record=matter:${ID.toUpperCase()}`)?.recordId).toBe(ID);
  });

  it.each([
    ['no parameter', '?customer=acme'],
    ['empty search', ''],
    ['empty value', '?record='],
    ['no id', '?record=matter'],
    ['empty id', '?record=matter:'],
    ['no type', `?record=:${ID}`],
    ['not a GUID', '?record=matter:12345'],
    ['a GUID with a suffix', `?record=matter:${ID}x`],
    ['a GUID with extra path', `?record=matter:${ID}/../x`],
    ['an unknown type', `?record=user:${ID}`],
    ['an unknown type that looks near', `?record=matters:${ID}`],
    ['an upper-case type', `?record=Matter:${ID}`],
    ['an inherited property as a type', `?record=constructor:${ID}`],
    ['a second colon', `?record=matter:${ID}:extra`],
    ['two record parameters (ambiguous)', `?record=matter:${ID}&record=project:${ID}`],
    ['a URL in place of an id', '?record=matter:https://evil.example/x'],
    ['a script in place of an id', '?record=matter:<script>alert(1)</script>'],
    ['the feed route segment instead of the link token', `?record=matters:${ID}`],
  ])('ignores %s', (_label, search) => {
    expect(parseRecordLink(search)).toBeNull();
  });

  it("knows the feed record types (route segments) and only those five (the server's message roots)", () => {
    expect(Object.keys(FEED_RECORD_TYPES).sort()).toEqual([
      'invoices',
      'matters',
      'projects',
      'servicerequests',
      'workassignments',
    ]);
    expect(recordTargetOfFeedItem({ recordType: 'matters', recordId: ID })).toEqual({
      entityName: 'sprk_matter',
      recordId: ID,
    });
    expect(recordTargetOfFeedItem({ recordType: 'invoices', recordId: ID })).toEqual({
      entityName: 'sprk_invoice',
      recordId: ID,
    });
    expect(recordTargetOfFeedItem({ recordType: 'documents', recordId: ID })).toBeNull(); // no thread, no feed item
    expect(recordTargetOfFeedItem({ recordType: 'toString', recordId: ID })).toBeNull();
    expect(recordTargetOfFeedItem({ recordType: 'matters', recordId: 'nope' })).toBeNull();
  });

  it('helpers: drop only the record parameter, read the query of a path', () => {
    expect(withoutRecordParam(`?customer=acme&record=matter:${ID}&x=1`)).toBe('?customer=acme&x=1');
    expect(withoutRecordParam(`?record=matter:${ID}`)).toBe('');
    expect(hasRecordParam('?record=junk')).toBe(true);
    expect(hasRecordParam('?customer=a')).toBe(false);
    expect(searchOfPath('/?customer=a&record=b#frag')).toBe('?customer=a&record=b');
    expect(searchOfPath('/project/1')).toBe('');
  });
});

// ---------------------------------------------------------------------------
// Opening
// ---------------------------------------------------------------------------

describe('RecordDeepLink', () => {
  it.each([
    ['project', 'sprk_project'],
    ['matter', 'sprk_matter'],
    ['workassignment', 'sprk_workassignment'],
    ['document', 'sprk_document'],
    ['invoice', 'sprk_invoice'],
    ['servicerequest', 'sprk_servicerequest'],
  ])('a valid %s link opens the detail view for %s', SLOW, async (token, entity) => {
    renderAt(`/?customer=default&record=${token}:${ID}`);
    const detail = await screen.findByTestId('detail');
    expect(detail.getAttribute('data-entity')).toBe(entity);
    expect(detail.getAttribute('data-id')).toBe(ID);
  });

  it('drops `record` from the address bar once it has opened, and keeps the other parameters', SLOW, async () => {
    renderAt(`/?customer=default&record=matter:${ID}`);
    await screen.findByTestId('detail');
    await waitFor(() => expect(where()).toBe('/?customer=default'));
  });

  it('opens on any route, not only the home page', SLOW, async () => {
    renderAt(`/settings?record=project:${ID}`);
    expect((await screen.findByTestId('detail')).getAttribute('data-entity')).toBe('sprk_project');
    await waitFor(() => expect(where()).toBe('/settings'));
  });

  it.each([
    ['malformed id', '/?customer=default&record=matter:abc'],
    ['unknown type', `/?customer=default&record=user:${ID}`],
    ['no id', '/?customer=default&record=matter'],
    ['ambiguous', `/?record=matter:${ID}&record=project:${ID}`],
  ])('ignores a link with a %s: nothing opens and the URL is left as it was', SLOW, async (_label, url) => {
    renderAt(url);
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(where()).toBe(url);
  });

  it('a plain visit (no link) opens nothing', SLOW, async () => {
    renderAt('/');
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
  });

  it('never reaches the network by itself, whatever the link carries', SLOW, async () => {
    renderAt(`/?customer=${encodeURIComponent('https://evil.example')}&record=matter:${ID}&bff=https://evil.example`);
    await screen.findByTestId('detail');
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });

  it("a record the caller cannot see shows the detail view's own denied state", SLOW, async () => {
    h.real.value = true;
    h.bffApiCall.mockImplementation(async (p: string) => {
      if (p.endsWith('/dataverse/fetch')) return { entities: [] }; // outside the caller's set: no row
      throw new ApiError(500, 'not needed for the denied state');
    });
    renderAt(`/?record=matter:${ID}`);
    expect(await screen.findByText("You don't have access to this record", undefined, { timeout: 20000 })).toBeTruthy();
    // The read it made is the id-filtered module fetch on the matter entity, for exactly the id in the link.
    const fetchCall = h.bffApiCall.mock.calls.find(([p]) => String(p).endsWith('/dataverse/fetch'));
    const body = JSON.parse(String((fetchCall?.[1] as RequestInit).body));
    expect(body.entityName).toBe('sprk_matter');
    expect(body.fetchXml).toContain(ID);
  });

  it('a 403 from the read shows the same denied state', SLOW, async () => {
    h.real.value = true;
    h.bffApiCall.mockImplementation(async (p: string) => {
      if (p.endsWith('/dataverse/fetch')) throw new ApiError(403, '{}');
      throw new ApiError(500, 'not needed');
    });
    renderAt(`/?customer=default&record=project:${ID}`);
    expect(await screen.findByText("You don't have access to this record", undefined, { timeout: 20000 })).toBeTruthy();
    // A 403 is a terminal state: the link is spent.
    await waitFor(() => expect(where()).toBe('/?customer=default'));
  });
});

// ---------------------------------------------------------------------------
// The `record` parameter leaves the address bar only when the record is settled
// ---------------------------------------------------------------------------

describe('RecordDeepLink - the link is stripped only once the opener reaches a terminal state', () => {
  /** A fetch the test settles by hand, to hold the record in its loading state. */
  function holdFetch(): { settle: (rows: unknown[]) => void; fail: (error: unknown) => void } {
    let settle: (rows: unknown[]) => void = () => undefined;
    let fail: (error: unknown) => void = () => undefined;
    h.bffApiCall.mockImplementation((p: string) => {
      if (String(p).endsWith('/dataverse/fetch'))
        return new Promise((resolve, reject) => {
          settle = rows => resolve({ entities: rows });
          fail = reject;
        });
      return Promise.reject(new ApiError(500, 'not needed'));
    });
    return { settle: rows => settle(rows), fail: error => fail(error) };
  }
  const MATTER_ROW = { sprk_matterid: ID, sprk_mattername: 'Acme v. Widget' };

  it(
    'keeps `record` in the URL while the read is pending (a token redirect then still carries it), strips it once loaded',
    SLOW,
    async () => {
      h.real.value = true;
      const read = holdFetch();
      renderAt(`/?customer=default&record=matter:${ID}`);
      await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled());
      await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
      // Still reading: the address bar still names the record, so a redirect's return-to would keep the link.
      expect(where()).toBe(`/?customer=default&record=matter:${ID}`);

      await act(async () => {
        read.settle([MATTER_ROW]);
      });
      await waitFor(() => expect(where()).toBe('/?customer=default'));
    }
  );

  it('keeps `record` while pending and strips it after a 403', SLOW, async () => {
    h.real.value = true;
    const read = holdFetch();
    renderAt(`/?customer=default&record=matter:${ID}`);
    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled());
    expect(where()).toBe(`/?customer=default&record=matter:${ID}`);

    await act(async () => {
      read.fail(new ApiError(403, '{}'));
    });
    expect(await screen.findByText("You don't have access to this record", undefined, { timeout: 20000 })).toBeTruthy();
    await waitFor(() => expect(where()).toBe('/?customer=default'));
  });

  it.each([
    ['no row for the caller (denied)', (read: ReturnType<typeof holdFetch>) => read.settle([])],
    ['a 404 (not found)', (read: ReturnType<typeof holdFetch>) => read.fail(new ApiError(404, '{}'))],
  ])('strips it for a terminal outcome: %s', SLOW, async (_label, finish) => {
    h.real.value = true;
    const read = holdFetch();
    renderAt(`/?record=matter:${ID}`);
    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled());
    expect(where()).toBe(`/?record=matter:${ID}`);
    await act(async () => {
      finish(read);
    });
    await waitFor(() => expect(where()).toBe('/'));
  });

  it('a read that FAILED (retryable) is not terminal: the link stays, so a reload tries again', SLOW, async () => {
    h.real.value = true;
    const read = holdFetch();
    renderAt(`/?record=matter:${ID}`);
    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled());
    await act(async () => {
      read.fail(new ApiError(500, '{}'));
    });
    expect(await screen.findByTestId('record-detail-error', undefined, { timeout: 20000 })).toBeTruthy();
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(where()).toBe(`/?record=matter:${ID}`);
  });

  it('closing the view before it settled also ends the link', SLOW, async () => {
    h.settle.value = false; // the stub never reports a settled record
    renderAt(`/?customer=default&record=matter:${ID}`);
    await screen.findByTestId('detail');
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(where()).toBe(`/?customer=default&record=matter:${ID}`);

    fireEvent.click(screen.getByRole('button', { name: 'Close detail' }));
    await waitFor(() => expect(screen.queryByTestId('detail')).toBeNull());
    expect(where()).toBe('/?customer=default');
  });

  it('opens exactly once, and does not reopen after the link is spent', SLOW, async () => {
    h.settle.value = false;
    renderAt(`/?record=matter:${ID}`);
    await screen.findByTestId('detail');
    // Re-renders and router updates while it is open (the link is still in the URL) must not open it again.
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(h.stubMounts.count).toBe(1);

    fireEvent.click(screen.getByRole('button', { name: 'Close detail' }));
    await waitFor(() => expect(where()).toBe('/'));
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(h.stubMounts.count).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// Surviving sign-in, and Teams
// ---------------------------------------------------------------------------

describe('RecordDeepLink - across sign-in', () => {
  it(
    'the return-to helpers AuthGuard uses carry the whole link across the redirect, and the restored URL opens the record',
    SLOW,
    async () => {
      const link = `/?customer=default&record=${encodeURIComponent(`matter:${ID}`)}`;
      window.history.replaceState({}, '', link);
      captureReturnTo(); // what AuthGuard does before the sign-in redirect
      window.history.replaceState({}, '', '/'); // the redirect returns to the origin, link lost
      const restored = consumeReturnTo(); // what AuthGuard does afterwards
      expect(restored).toBe(link);
      expect(consumeReturnTo()).toBeNull(); // once
      renderAt(restored as string);
      expect((await screen.findByTestId('detail')).getAttribute('data-id')).toBe(ID);
    }
  );

  it(
    'Teams: a captured record link is restored and opened (the account gate that normally does this never runs there)',
    SLOW,
    async () => {
      captureTeamsDeepLink(`/?customer=default&record=${encodeURIComponent(`servicerequest:${ID}`)}`);
      renderAt('/', true);
      const detail = await screen.findByTestId('detail');
      expect(detail.getAttribute('data-entity')).toBe('sprk_servicerequest');
      expect(detail.getAttribute('data-id')).toBe(ID);
      await waitFor(() => expect(where()).toBe('/?customer=default'));
      expect(sessionStorage.length).toBe(0);
    }
  );

  it('Teams: a captured route that is not a record link is left alone', SLOW, async () => {
    captureTeamsDeepLink('/project/abc');
    renderAt('/', true);
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(where()).toBe('/');
    expect(consumeReturnTo()).toBe('/project/abc'); // still there for its own owner
  });

  it('Teams: a captured record link with an invalid record is ignored', SLOW, async () => {
    captureTeamsDeepLink('/?record=matter:not-a-guid');
    renderAt('/', true);
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(where()).toBe('/');
  });

  it('the browser host does not consume a captured route (its account gate does)', SLOW, async () => {
    captureTeamsDeepLink(`/?record=matter:${ID}`);
    renderAt('/', false);
    await act(async () => new Promise(resolve => setTimeout(resolve, 100)));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(consumeReturnTo()).toBe(`/?record=matter:${ID}`);
  });
});

// ---------------------------------------------------------------------------
// The customer key (task 041 seam)
// ---------------------------------------------------------------------------

describe('?customer= with a record link (task 041 seam)', () => {
  const backend = (key: string): Backend => ({
    customerKey: key,
    displayName: key,
    bffBaseUrl: `https://bff-${key}.example.test`,
    bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
  });
  const directory = (known: Backend[]): BackendDirectory => ({
    resolveWorkforce: async () => known,
    resolveByKey: async key => known.find(b => b.customerKey === key) ?? null,
  });

  it('an unknown customer key is ignored; the record parameter never influences the selection', SLOW, async () => {
    const dir = directory([backend('acme'), backend('beta')]);
    const noKey = await resolveBackendSelection('ciam', { directory: dir, search: '' });
    expect(noKey.kind).toBe('pick'); // two backends, none chosen
    const unknown = await resolveBackendSelection('ciam', {
      directory: dir,
      search: `?customer=nope&record=matter:${ID}`,
    });
    expect(unknown).toEqual(noKey); // the unknown key changed nothing
  });

  it('a known customer key selects that backend, whatever the record parameter says', SLOW, async () => {
    const dir = directory([backend('acme'), backend('beta')]);
    for (const record of [`matter:${ID}`, 'garbage', `user:${ID}`]) {
      const selection = await resolveBackendSelection('ciam', {
        directory: dir,
        search: `?customer=beta&record=${record}`,
      });
      expect(selection.kind).toBe('selected');
      if (selection.kind === 'selected') expect(selection.backend.customerKey).toBe('beta');
    }
  });

  it('a customer key that is a URL is not a key', SLOW, async () => {
    const dir = directory([backend('acme'), backend('beta')]);
    const selection = await resolveBackendSelection('ciam', {
      directory: dir,
      search: `?customer=${encodeURIComponent('https://evil.example')}&record=matter:${ID}`,
    });
    expect(selection.kind).toBe('pick');
  });
});
