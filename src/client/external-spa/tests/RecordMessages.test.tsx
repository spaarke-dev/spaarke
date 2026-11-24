/**
 * R3 task 051 (FR-04 read thread, FR-11 post) — the Messages section of the record detail view.
 *
 * What runs for real: `RecordMessages` (list, compose, error states), `messages-client` (paths, body, ProblemDetails
 * parsing), the shared `sanitizeEmailHtml` (DOMPurify in jsdom), `BffAccessState` and the Fluent v9 components. What is
 * substituted: the network seam (`bffApiCall`, answering the external message routes and throwing `ApiError` exactly
 * as bff-client builds it - the raw ProblemDetails body is the message) and MSAL's `useMsal` (BffAccessState reads it).
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, loginRedirect } = vi.hoisted(() => ({ bffApiCall: vi.fn(), loginRedirect: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect }, accounts: [], inProgress: 'none' }),
}));

import { ApiError } from '../src/types';
import { setActiveLoginScope } from '../src/auth/msal-auth';
import { TeamsHostProvider } from '../src/components/auth/TeamsHostContext';
import { BffAccessState } from '../src/components/auth/BffAccessState';
import { RecordMessages } from '../src/components/detail/RecordMessages';
import { MAX_MESSAGE_BODY_LENGTH, MESSAGE_ROOTS, hasNewerMessages } from '../src/api/messages-client';
import { NEW_MESSAGE_CHECK_INTERVAL_MS, type ThreadRefreshAction } from '../src/components/detail/RecordMessages';
import { MessagesSection } from '../src/components/record/sections/MessagesSection';
import { getRecordDetailEntity } from '../src/components/detail/RecordDetailFields';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';

// ---------------------------------------------------------------------------
// Fixtures + helpers
// ---------------------------------------------------------------------------

const RECORD_ID = '00000000-0000-4000-8000-000000000201';
const LIST_PATH = `/api/v1/external/matters/${RECORD_ID}/messages`;

const PLAIN = 100000000;
const HTML = 100000001;

interface Dto {
  sprk_communicationid: string;
  sprk_subject?: string | null;
  sprk_body?: string | null;
  sprk_bodyformat?: number | null;
  sprk_direction?: number | null;
  sendername?: string | null;
  createdon?: string | null;
  isMine: boolean;
}

const dto = (n: number, over: Partial<Dto> = {}): Dto => ({
  sprk_communicationid: `00000000-0000-4000-8000-0000000009${String(n).padStart(2, '0')}`,
  sprk_body: `body ${n}`,
  sprk_bodyformat: PLAIN,
  sendername: `Sender ${n}`,
  createdon: `2026-03-0${n}T10:00:00Z`,
  isMine: false,
  ...over,
});

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', ...extra });

let thread: Dto[];
let truncated: boolean;

const gets = (): unknown[][] =>
  bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === undefined);
const posts = (): unknown[][] =>
  bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'POST');

/** GET answers the current `thread`; POST is scripted per test (default: 201 that appends a server-built message). */
function installNetwork(post?: (body: { sprk_body: string }) => unknown | Promise<unknown>): void {
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path !== LIST_PATH) throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${path}`);
    if (options?.method === 'POST') {
      const body = JSON.parse(String(options.body)) as { sprk_body: string };
      if (post) return post(body);
      thread = [
        ...thread,
        dto(thread.length + 1, { sprk_body: body.sprk_body, isMine: true, sendername: 'Server Name' }),
      ];
      return { ...dto(99, { sprk_body: body.sprk_body, isMine: true, sendername: null }) };
    }
    return truncated ? { value: thread, truncated: true } : { value: thread };
  });
}

function renderMessages(props: Partial<React.ComponentProps<typeof RecordMessages>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <RecordMessages entity="sprk_matter" recordId={RECORD_ID} {...props} />
    </FluentProvider>
  );
}

const box = (): HTMLTextAreaElement => screen.getByRole('textbox', { name: 'Write a message' }) as HTMLTextAreaElement;
const sendButton = (): HTMLButtonElement => screen.getByRole('button', { name: /^Send/ }) as HTMLButtonElement;
const type = (value: string): void => {
  fireEvent.change(box(), { target: { value } });
};

beforeEach(() => {
  thread = [];
  truncated = false;
  bffApiCall.mockReset();
  loginRedirect.mockReset();
  loginRedirect.mockResolvedValue(undefined);
  installNetwork();
});

// ---------------------------------------------------------------------------
// The thread
// ---------------------------------------------------------------------------

describe('the thread', () => {
  it('lists the messages oldest-to-newest with sender and time', async () => {
    thread = [dto(1), dto(2, { sendername: 'Dana Okafor' }), dto(3)];
    renderMessages();

    const items = await screen.findAllByTestId('record-message');
    expect(items).toHaveLength(3);
    expect(items.map(li => li.textContent)).toEqual([
      expect.stringContaining('body 1'),
      expect.stringContaining('body 2'),
      expect.stringContaining('body 3'),
    ]);
    expect(within(items[1]).getByTestId('record-message-sender').textContent).toBe('Dana Okafor');
    const time = within(items[1]).getByTestId('record-message-time');
    expect(time.textContent).toBe(new Date('2026-03-02T10:00:00Z').toLocaleString());
    expect(time.getAttribute('datetime')).toBe('2026-03-02T10:00:00.000Z');
  });

  it('reads the external plane GET for the record, once', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    expect(gets().map(c => c[0])).toEqual([LIST_PATH]);
    expect(posts()).toHaveLength(0);
  });

  it('own-vs-other follows isMine, never the sender name', async () => {
    thread = [
      // Another person whose display name happens to read "You": still not mine.
      dto(1, { sendername: 'You', isMine: false }),
      // Mine, with a different name on the row: still mine.
      dto(2, { sendername: 'Someone Else Entirely', isMine: true }),
      // No sender recorded.
      dto(3, { sendername: null, isMine: false }),
    ];
    renderMessages();

    const items = await screen.findAllByTestId('record-message');
    expect(items.map(li => li.getAttribute('data-mine'))).toEqual(['false', 'true', 'false']);
    const senders = items.map(li => within(li).getByTestId('record-message-sender').textContent);
    expect(senders).toEqual(['You', 'You', 'Unknown sender']);
  });

  it('shows an empty state with the compose box', async () => {
    renderMessages();
    expect(await screen.findByTestId('record-messages-empty')).toBeTruthy();
    expect(box()).toBeTruthy();
  });

  it('says a truncated thread is incomplete', async () => {
    thread = [dto(1)];
    truncated = true;
    renderMessages();
    await screen.findByTestId('record-message');
    expect(screen.getByTestId('truncated-list-notice').textContent).toContain('This list is incomplete');
  });

  it.each(Object.entries(MESSAGE_ROOTS))('%s maps to the %s route segment', async (entity, root) => {
    bffApiCall.mockImplementation(async () => ({ value: [] }));
    renderMessages({ entity });
    await screen.findByTestId('record-messages-empty');
    expect(bffApiCall.mock.calls[0][0]).toBe(`/api/v1/external/${root}/${RECORD_ID}/messages`);
  });

  it('renders nothing, and reads nothing, for an entity with no thread (a document)', () => {
    renderMessages({ entity: 'sprk_document' });
    expect(screen.queryByTestId('record-detail-messages')).toBeNull();
    expect(bffApiCall).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Bodies: nothing executable reaches the DOM (review 033 K1)
// ---------------------------------------------------------------------------

describe('message bodies', () => {
  const SCRIPT_HTML =
    '<p>Hello <b>team</b></p>' +
    '<script>window.__pwned = "script"</script>' +
    '<img src="x" onerror="window.__pwned = \'onerror\'">' +
    '<a href="javascript:window.__pwned=\'href\'" onclick="window.__pwned=\'click\'">link</a>' +
    '<svg onload="window.__pwned = \'svg\'"><circle onmouseover="window.__pwned = \'x\'" /></svg>' +
    '<iframe src="https://evil.invalid"></iframe><form action="https://evil.invalid"><input name="x"></form>';

  /** Every element under `root` with an `on*` attribute, and every script/iframe/object/embed/form element. */
  const executable = (root: Element): string[] => {
    const found: string[] = [];
    for (const el of Array.from(root.querySelectorAll('*'))) {
      if (['SCRIPT', 'IFRAME', 'OBJECT', 'EMBED', 'FORM', 'STYLE'].includes(el.tagName)) found.push(el.tagName);
      for (const attr of Array.from(el.attributes)) {
        if (/^on/i.test(attr.name)) found.push(`${el.tagName}[${attr.name}]`);
        if (/^\s*javascript:/i.test(attr.value)) found.push(`${el.tagName}[${attr.name}=${attr.value}]`);
      }
    }
    return found;
  };

  it('an HTML body with script and handlers is sanitized: no script element, no event-handler attribute, no javascript: link', async () => {
    delete (window as unknown as { __pwned?: string }).__pwned;
    thread = [dto(1, { sprk_body: SCRIPT_HTML, sprk_bodyformat: HTML })];
    renderMessages();

    const html = await screen.findByTestId('record-message-html');
    // The safe markup survives, so this is sanitizing and not blanking.
    expect(html.querySelector('b')?.textContent).toBe('team');
    expect(html.textContent).toContain('Hello team');
    // Nothing executable anywhere in the section.
    expect(executable(screen.getByTestId('record-detail-messages'))).toEqual([]);
    expect(document.querySelectorAll('script')).toHaveLength(0);
    // The link is hardened (and its javascript: href is gone).
    const link = html.querySelector('a');
    expect(link?.getAttribute('href')).toBeNull();
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect((window as unknown as { __pwned?: string }).__pwned).toBeUndefined();
  });

  it('a plain-text body is a text node: markup in it is shown literally and creates no element', async () => {
    const payload = '<script>window.__pwned = "text"</script><img src=x onerror="window.__pwned=1"> a & b';
    thread = [dto(1, { sprk_body: payload, sprk_bodyformat: PLAIN })];
    renderMessages();

    const text = await screen.findByTestId('record-message-text');
    expect(text.textContent).toBe(payload);
    expect(text.children).toHaveLength(0);
    expect(screen.queryByTestId('record-message-html')).toBeNull();
    expect(executable(screen.getByTestId('record-detail-messages'))).toEqual([]);
  });

  it('a body with no format is treated as plain text, never as HTML', async () => {
    thread = [dto(1, { sprk_body: '<b>not bold</b>', sprk_bodyformat: null })];
    renderMessages();
    const text = await screen.findByTestId('record-message-text');
    expect(text.textContent).toBe('<b>not bold</b>');
    expect(text.querySelector('b')).toBeNull();
  });

  // jsdom does no layout, so these cannot show that the overlay is actually clipped on screen. They prove the two things
  // that make it so in a browser: the HTML body sits inside its message box, and its host element carries the
  // containment styles (contain: paint = containing block for fixed/absolute descendants + paint clipping).
  const OVERLAY_HTML =
    '<div id="overlay" style="position:fixed;inset:0;z-index:2147483647;background:#fff">' +
    '<p>Session expired. Sign in to continue</p><input name="password"></div>';

  /** The CSS text of every stylesheet rule whose selector uses one of the element's classes (Griffel emits atomic rules). */
  const cssOf = (el: Element): string => {
    const classes = new Set(Array.from(el.classList));
    const rules: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      for (const rule of Array.from(sheet.cssRules)) {
        const selector = (rule as CSSStyleRule).selectorText ?? '';
        if (Array.from(selector.matchAll(/\.([\w-]+)/g)).some(m => classes.has(m[1]))) rules.push(rule.cssText);
      }
    }
    return rules.join('\n').replace(/\s+/g, ' ');
  };

  it('an HTML body is confined to its message box: the host element carries paint containment and a positioned, clipped box', async () => {
    thread = [dto(1, { sprk_body: OVERLAY_HTML, sprk_bodyformat: HTML })];
    renderMessages();

    const host = await screen.findByTestId('record-message-html');
    const css = cssOf(host);
    expect(css).toMatch(/contain:\s*paint/);
    expect(css).toMatch(/position:\s*relative/);
    expect(css).toMatch(/overflow-y:\s*hidden/);
    expect(css).toMatch(/overflow-x:\s*auto/);
  });

  it('a body with style="position:fixed" renders inside its message container, and the shared sanitizer has already dropped the overlay styles (DOM structure)', async () => {
    thread = [dto(1, { sprk_body: OVERLAY_HTML, sprk_bodyformat: HTML })];
    renderMessages();

    const host = await screen.findByTestId('record-message-html');
    const overlay = host.querySelector('div');
    // Defence in depth: the shared sanitizer (ISS-003) strips the overlay declarations at source, so the box is an
    // ordinary in-flow block ...
    expect(overlay).not.toBeNull();
    expect(overlay?.textContent).toContain('Sign in to continue');
    expect(overlay?.getAttribute('style') ?? '').not.toMatch(/position|inset|z-index/);
    // ... and the containment on the host (previous test) would still clip it if a future sanitizer change let one through.
    // It is a descendant of the host, which is inside its message <li>, which is inside the Messages section.
    expect(host.contains(overlay)).toBe(true);
    expect(overlay?.closest('li[data-testid="record-message"]')).not.toBeNull();
    expect(overlay?.closest('[data-testid="record-detail-messages"]')).not.toBeNull();
    // A direct child of the host: not hoisted out to the document body or a portal.
    expect(overlay?.parentElement).toBe(host);
  });

  it('an HTML body cannot phone home on open: a remote tracking pixel and a CSS url() background are not loaded (ISS-003)', async () => {
    thread = [
      dto(1, {
        sprk_body:
          '<p>Hi</p><img src="https://t.example/p.gif" alt="pixel" width="1" height="1">' +
          '<div style="background-image:url(https://t.example/bg.gif);color:#333">styled</div>',
        sprk_bodyformat: HTML,
      }),
    ];
    renderMessages();

    const host = await screen.findByTestId('record-message-html');
    expect(host.textContent).toContain('Hi');
    expect(host.querySelector('img')?.hasAttribute('src')).toBe(false);
    expect(host.querySelector('img')?.getAttribute('alt')).toBe('pixel');
    expect(host.querySelector('div')?.getAttribute('style')).toBe('color:#333');
    expect(host.innerHTML).not.toContain('t.example');
  });

  it('a message I just typed that contains markup is posted as typed and rendered as text after the re-read', async () => {
    const payload = '<img src=x onerror="window.__pwned=2">';
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type(payload);
    fireEvent.click(sendButton());

    const text = await screen.findByTestId('record-message-text');
    expect(text.textContent).toBe(payload);
    expect(text.children).toHaveLength(0);
    expect(executable(screen.getByTestId('record-detail-messages'))).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Sending
// ---------------------------------------------------------------------------

describe('sending', () => {
  it('posts the text once, re-reads the thread, and shows the server row (not the 201 body)', async () => {
    thread = [dto(1)];
    // The 201 body is deliberately unlike the thread row: null sendername, a body marker that must never render.
    installNetwork(body => {
      thread = [...thread, dto(2, { sprk_body: body.sprk_body, isMine: true, sendername: 'Server Name' })];
      return dto(99, { sprk_body: 'FROM-THE-201-BODY', isMine: true, sendername: null });
    });
    renderMessages();
    await screen.findByText('body 1');

    type('  Thanks, received.  ');
    fireEvent.click(sendButton());

    await screen.findByText('Thanks, received.');
    expect(posts()).toHaveLength(1);
    expect(posts()[0][0]).toBe(LIST_PATH);
    expect(JSON.parse(String((posts()[0][1] as RequestInit).body))).toEqual({ sprk_body: 'Thanks, received.' });
    // Re-read after the 201: the initial GET plus one more.
    expect(gets()).toHaveLength(2);
    expect(screen.queryByText('FROM-THE-201-BODY')).toBeNull();
    // The new row is the server's (isMine from the thread), the box is empty, and Send is off again.
    const items = screen.getAllByTestId('record-message');
    expect(items).toHaveLength(2);
    expect(items[1].getAttribute('data-mine')).toBe('true');
    expect(box().value).toBe('');
    expect(sendButton().disabled).toBe(true);
  });

  it('the POST body carries the text only - no attachment field of any kind', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('hello');
    fireEvent.click(sendButton());
    await screen.findByTestId('record-message');

    const body = JSON.parse(String((posts()[0][1] as RequestInit).body)) as Record<string, unknown>;
    expect(Object.keys(body)).toEqual(['sprk_body']);
  });

  it('there is no attachment control', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    const section = screen.getByTestId('record-detail-messages');
    expect(section.querySelectorAll('input[type="file"]')).toHaveLength(0);
    expect(within(section).queryByRole('button', { name: /attach|file|upload/i })).toBeNull();
    expect(within(section).queryByLabelText(/attach|file|upload/i)).toBeNull();
    expect(section.textContent).not.toMatch(/attach/i);
  });

  it('Send is disabled for an empty body and for a whitespace-only body', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    expect(sendButton().disabled).toBe(true);

    type('   \n\t  ');
    expect(sendButton().disabled).toBe(true);
    fireEvent.click(sendButton());
    expect(posts()).toHaveLength(0);

    type('x');
    expect(sendButton().disabled).toBe(false);
  });

  it('a body of exactly 10,000 characters can be sent; 10,001 cannot', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');

    type('a'.repeat(MAX_MESSAGE_BODY_LENGTH + 1));
    expect(sendButton().disabled).toBe(true);
    // Numbers are formatted for the reader's locale (the component calls toLocaleString), so the expected text is built
    // the same way and the test does not depend on the runner's locale.
    expect(screen.getByTestId('record-messages-counter').textContent).toContain(`${(1).toLocaleString()} over`);
    fireEvent.click(sendButton());
    expect(posts()).toHaveLength(0);

    type('a'.repeat(MAX_MESSAGE_BODY_LENGTH));
    expect(sendButton().disabled).toBe(false);
    expect(screen.getByTestId('record-messages-counter').textContent).toBe(
      `${MAX_MESSAGE_BODY_LENGTH.toLocaleString()} / ${MAX_MESSAGE_BODY_LENGTH.toLocaleString()}`
    );
    fireEvent.click(sendButton());
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(JSON.parse(String((posts()[0][1] as RequestInit).body)).sprk_body).toHaveLength(MAX_MESSAGE_BODY_LENGTH);
  });

  it('Send is disabled while a post is in flight, and a double-click posts once', async () => {
    let finish!: (v: unknown) => void;
    const inFlight = new Promise(resolve => {
      finish = resolve;
    });
    installNetwork(() => inFlight);
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('only once please');

    // Two clicks in the same tick: before React has re-rendered the disabled state.
    const button = sendButton();
    act(() => {
      button.click();
      button.click();
    });

    expect(posts()).toHaveLength(1);
    expect(sendButton().disabled).toBe(true);
    expect(sendButton().textContent).toContain('Sending');
    // The draft is not editable mid-send, so it cannot be changed under the request.
    expect(box().disabled).toBe(true);
    fireEvent.click(sendButton());
    expect(posts()).toHaveLength(1);

    await act(async () => {
      thread = [dto(1, { sprk_body: 'only once please', isMine: true })];
      finish(dto(99));
      await inFlight;
    });
    await screen.findByText('only once please');
    expect(posts()).toHaveLength(1);
    expect(box().disabled).toBe(false);
  });

  it('can send a second message after the first (the in-flight guard is released)', async () => {
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('one');
    fireEvent.click(sendButton());
    await screen.findByText('one');
    type('two');
    fireEvent.click(sendButton());
    await screen.findByText('two');
    expect(posts()).toHaveLength(2);
  });

  it('if the post worked but the re-read fails, says so, keeps the thread, and clears the sent draft', async () => {
    thread = [dto(1)];
    let readFails = false;
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (options?.method === 'POST') {
        readFails = true;
        return dto(99);
      }
      if (readFails) throw new ApiError(500, 'boom');
      return { value: thread };
    });
    renderMessages();
    await screen.findByText('body 1');
    type('sent fine');
    fireEvent.click(sendButton());

    await screen.findByTestId('record-messages-refresh-failed');
    expect(screen.getByText('body 1')).toBeTruthy();
    expect(box().value).toBe('');
    expect(posts()).toHaveLength(1);

    readFails = false;
    thread = [dto(1), dto(2, { sprk_body: 'sent fine', isMine: true })];
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await screen.findByText('sent fine');
    expect(screen.queryByTestId('record-messages-refresh-failed')).toBeNull();
  });
});

describe('a send that worked while the thread is not ready', () => {
  it('a failed load, then a successful send whose re-read also fails: the sent notice is shown and the draft is cleared', async () => {
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'POST') return dto(99);
      throw new ApiError(500, 'boom');
    });
    renderMessages();
    await screen.findByTestId('record-messages-error');
    // The load-error bar does not read as a failed send.
    expect(screen.getByTestId('record-messages-error').textContent).not.toMatch(/sent/i);

    type('hello from an errored thread');
    fireEvent.click(sendButton());

    const notice = await screen.findByTestId('record-messages-refresh-failed');
    expect(notice.textContent).toContain('Your message was sent');
    expect(posts()).toHaveLength(1);
    expect(box().value).toBe('');
    expect(screen.queryByTestId('record-messages-send-error')).toBeNull();
    // The load error is still shown separately, with its own Try again.
    expect(screen.getByTestId('record-messages-error')).toBeTruthy();
  });

  it('Try again after that reads the thread, shows the sent message, and drops the notice', async () => {
    let readsWork = false;
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'POST') {
        thread = [dto(1, { sprk_body: 'queued', isMine: true })];
        return dto(99);
      }
      if (!readsWork) throw new ApiError(500, 'boom');
      return { value: thread };
    });
    renderMessages();
    await screen.findByTestId('record-messages-error');
    type('queued');
    fireEvent.click(sendButton());
    await screen.findByTestId('record-messages-refresh-failed');

    readsWork = true;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText('queued');
    expect(screen.queryByTestId('record-messages-refresh-failed')).toBeNull();
    expect(screen.queryByTestId('record-messages-error')).toBeNull();
  });

  it('a send during the first read whose re-read fails shows the notice and the load error, not a spinner for ever', async () => {
    let releaseFirst!: () => void;
    const firstRead = new Promise<void>(resolve => {
      releaseFirst = resolve;
    });
    let reads = 0;
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'POST') return dto(99);
      reads++;
      if (reads === 1) {
        await firstRead;
        return { value: [] };
      }
      throw new ApiError(500, 'boom');
    });
    renderMessages();
    // The compose box is available while the first read is still pending.
    await screen.findByText('Loading messages...');
    type('fast typist');
    fireEvent.click(sendButton());

    await screen.findByTestId('record-messages-refresh-failed');
    await screen.findByTestId('record-messages-error');
    expect(screen.queryByText('Loading messages...')).toBeNull();
    releaseFirst();
  });

  it('a 401 on the re-read after a send is the shared sign-in state, not "could not be refreshed"', async () => {
    let afterPost = false;
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'POST') {
        afterPost = true;
        return dto(99);
      }
      if (afterPost) throw new ApiError(401, '');
      return { value: [] };
    });
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('last words');
    fireEvent.click(sendButton());

    await screen.findByTestId('bff-access-signin');
    expect(screen.queryByTestId('record-messages-refresh-failed')).toBeNull();
    expect(posts()).toHaveLength(1);
  });
});

describe('onSendingChange', () => {
  it('reports true while a post is in flight and false when it settles', async () => {
    let finish!: (v: unknown) => void;
    const inFlight = new Promise(resolve => {
      finish = resolve;
    });
    installNetwork(() => inFlight);
    const onSendingChange = vi.fn();
    renderMessages({ onSendingChange });
    await screen.findByTestId('record-messages-empty');
    expect(onSendingChange).toHaveBeenLastCalledWith(false);

    type('hold the door');
    fireEvent.click(sendButton());
    await waitFor(() => expect(onSendingChange).toHaveBeenLastCalledWith(true));

    await act(async () => {
      thread = [dto(1, { sprk_body: 'hold the door', isMine: true })];
      finish(dto(99));
      await inFlight;
    });
    await screen.findByText('hold the door');
    expect(onSendingChange).toHaveBeenLastCalledWith(false);
  });

  it('reports false again after a refused send', async () => {
    installNetwork(() => {
      throw new ApiError(429, '');
    });
    const onSendingChange = vi.fn();
    renderMessages({ onSendingChange });
    await screen.findByTestId('record-messages-empty');
    type('nope');
    fireEvent.click(sendButton());
    await screen.findByTestId('record-messages-send-error');
    expect(onSendingChange.mock.calls.map(c => c[0])).toContain(true);
    expect(onSendingChange).toHaveBeenLastCalledWith(false);
  });

  it('reports false when unmounted mid-send, so a host never stays held', async () => {
    installNetwork(() => new Promise(() => undefined));
    const onSendingChange = vi.fn();
    const { unmount } = renderMessages({ onSendingChange });
    await screen.findByTestId('record-messages-empty');
    type('abandoned');
    fireEvent.click(sendButton());
    await waitFor(() => expect(onSendingChange).toHaveBeenLastCalledWith(true));
    unmount();
    expect(onSendingChange).toHaveBeenLastCalledWith(false);
  });
});

// ---------------------------------------------------------------------------
// Refused sends keep the draft
// ---------------------------------------------------------------------------

describe('a refused send shows an inline error and keeps the draft', () => {
  async function attempt(error: unknown, draft = 'my careful draft'): Promise<HTMLElement> {
    installNetwork(() => {
      throw error;
    });
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type(draft);
    fireEvent.click(sendButton());
    const alert = await screen.findByTestId('record-messages-send-error');
    // The same draft is still in the box and can be sent again.
    expect(box().value).toBe(draft);
    expect(sendButton().disabled).toBe(false);
    expect(box().disabled).toBe(false);
    return alert;
  }

  it('403 (no Create): the server detail is shown inline', async () => {
    const detail = 'Your access level does not permit posting messages on this record.';
    const alert = await attempt(
      new ApiError(403, problem(403, { detail, reasonCode: 'sdap.access.deny.insufficient_rights' }))
    );
    expect(alert.textContent).toContain(detail);
    expect(alert.textContent).toContain('kept');
    // An inline error, not a replacement of the thread or the compose box.
    expect(screen.getByTestId('record-messages-empty')).toBeTruthy();
    expect(screen.queryByTestId('bff-access-denied')).toBeNull();
  });

  it('403 with the generic detail says the caller may not post, in words', async () => {
    const alert = await attempt(
      new ApiError(
        403,
        problem(403, { detail: 'Access denied', reasonCode: 'sdap.external.messages.record_not_accessible' })
      )
    );
    expect(alert.textContent).toContain('You do not have permission to post messages on this record');
    expect(alert.textContent).not.toContain('Access denied');
  });

  it('429 (throttled): "too many messages, try again shortly", draft kept', async () => {
    const alert = await attempt(new ApiError(429, ''));
    expect(alert.textContent).toContain('too many messages');
    expect(alert.textContent).toContain('try again shortly');
  });

  it('400 shows the server detail', async () => {
    const detail = 'sprk_body is required and may be at most 10000 characters.';
    const alert = await attempt(new ApiError(400, problem(400, { detail, errorCode: 'EXT_MESSAGES_INVALID_BODY' })));
    expect(alert.textContent).toContain(detail);
  });

  it('409 (owner refusal) shows the server detail', async () => {
    const detail = 'The message cannot be filed because no owning team could be resolved for this record.';
    const alert = await attempt(new ApiError(409, problem(409, { detail })));
    expect(alert.textContent).toContain(detail);
  });

  it('500 / 502 / a network failure show a generic retry sentence, not the raw body', async () => {
    for (const error of [
      new ApiError(
        500,
        problem(500, { detail: 'internal stack trace here', errorCode: 'EXT_MESSAGES_INTERNAL_ERROR' })
      ),
      new ApiError(502, 'bad gateway'),
      new TypeError('Failed to fetch'),
    ]) {
      bffApiCall.mockReset();
      const { unmount } = (() => {
        installNetwork(() => {
          throw error;
        });
        return renderMessages();
      })();
      await screen.findByTestId('record-messages-empty');
      type('keep me');
      fireEvent.click(sendButton());
      const alert = await screen.findByTestId('record-messages-send-error');
      expect(alert.textContent).toContain('could not be sent just now');
      expect(alert.textContent).not.toContain('stack trace');
      expect(box().value).toBe('keep me');
      unmount();
    }
  });

  it('the error clears when the retry is sent and succeeds', async () => {
    let fail = true;
    installNetwork(body => {
      if (fail) throw new ApiError(429, '');
      thread = [dto(1, { sprk_body: body.sprk_body, isMine: true })];
      return dto(99);
    });
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('retry me');
    fireEvent.click(sendButton());
    await screen.findByTestId('record-messages-send-error');

    fail = false;
    fireEvent.click(sendButton());
    await screen.findByText('retry me');
    expect(screen.queryByTestId('record-messages-send-error')).toBeNull();
    expect(posts()).toHaveLength(2);
  });

  it('a 401 on send is the shared sign-in state, and the draft is still there', async () => {
    installNetwork(() => {
      throw new ApiError(401, '');
    });
    renderMessages();
    await screen.findByTestId('record-messages-empty');
    type('draft');
    fireEvent.click(sendButton());
    await screen.findByTestId('bff-access-signin');
    expect(box().value).toBe('draft');
  });
});

// ---------------------------------------------------------------------------
// The list refused / failed
// ---------------------------------------------------------------------------

describe('the thread cannot be read', () => {
  it('a 403 on the list is the shared denied state: no messages and no compose box', async () => {
    bffApiCall.mockImplementation(async () => {
      throw new ApiError(403, problem(403, { reasonCode: 'sdap.external.messages.record_not_accessible' }));
    });
    renderMessages();
    const denied = await screen.findByTestId('bff-access-denied');
    expect(denied.textContent).toContain('You do not have permission to view messages');
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.queryByTestId('record-message')).toBeNull();
  });

  it('a 401 on the list is the shared sign-in state', async () => {
    bffApiCall.mockImplementation(async () => {
      throw new ApiError(401, '');
    });
    renderMessages();
    await screen.findByTestId('bff-access-signin');
    expect(screen.getByRole('button', { name: 'Sign in again' })).toBeTruthy();
  });

  it('a 404 says messages are not available for the record, with no compose box', async () => {
    bffApiCall.mockImplementation(async () => {
      throw new ApiError(404, problem(404, { errorCode: 'EXT_MESSAGES_ROOT_NOT_SUPPORTED' }));
    });
    renderMessages();
    await screen.findByTestId('record-messages-unavailable');
    expect(screen.queryByRole('textbox')).toBeNull();
  });

  it('a 5xx is an error with "Try again", which reads again', async () => {
    let fail = true;
    bffApiCall.mockImplementation(async () => {
      if (fail) throw new ApiError(500, 'boom');
      return { value: [dto(1)] };
    });
    renderMessages();
    await screen.findByTestId('record-messages-error');
    expect(screen.queryByTestId('record-messages-empty')).toBeNull();

    fail = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText('body 1');
    expect(screen.queryByTestId('record-messages-error')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// "Sign in again" inside the Teams host (NFR-04: a redirect inside the Teams iframe is never allowed)
// ---------------------------------------------------------------------------

describe('"Sign in again" follows the host App provides (no prop is passed through the widget registry)', () => {
  const SCOPE = 'api://aaaaaaaa-0000-0000-0000-00000000000a/user_impersonation';

  /** Renders under the host provider exactly as App does; RecordMessages itself is given no host prop. */
  function renderHosted(teamsHost: boolean) {
    return render(
      <FluentProvider theme={webLightTheme}>
        <TeamsHostProvider teamsHost={teamsHost}>
          <RecordMessages entity="sprk_matter" recordId={RECORD_ID} />
        </TeamsHostProvider>
      </FluentProvider>
    );
  }

  /** window.location.reload is not configurable in jsdom; swap the whole object for the duration of the test. */
  async function withReloadSpy(run: (reload: ReturnType<typeof vi.fn>) => Promise<void>): Promise<void> {
    const reload = vi.fn();
    const realLocation = window.location;
    Object.defineProperty(window, 'location', { configurable: true, value: { ...realLocation, reload } });
    try {
      await run(reload);
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
    }
  }

  const refuse401 = (): void => {
    bffApiCall.mockImplementation(async () => {
      throw new ApiError(401, '');
    });
  };

  beforeEach(() => {
    setActiveLoginScope(SCOPE);
  });

  it('Teams: a 401 on the list then "Sign in again" reloads the tab and never calls loginRedirect', async () => {
    refuse401();
    await withReloadSpy(async reload => {
      renderHosted(true);
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
      expect(reload).toHaveBeenCalledTimes(1);
      expect(loginRedirect).not.toHaveBeenCalled();
    });
  });

  it('Teams: a 401 on send then "Sign in again" reloads the tab and never calls loginRedirect', async () => {
    installNetwork(() => {
      throw new ApiError(401, '');
    });
    await withReloadSpy(async reload => {
      renderHosted(true);
      await screen.findByTestId('record-messages-empty');
      type('draft');
      fireEvent.click(sendButton());
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
      expect(reload).toHaveBeenCalledTimes(1);
      expect(loginRedirect).not.toHaveBeenCalled();
      expect(box().value).toBe('draft');
    });
  });

  it('browser: a 401 then "Sign in again" still calls loginRedirect and never reloads', async () => {
    refuse401();
    await withReloadSpy(async reload => {
      renderHosted(false);
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).toHaveBeenCalledWith({ scopes: [SCOPE], prompt: 'select_account' });
      expect(reload).not.toHaveBeenCalled();
    });
  });

  it('no provider at all keeps the browser behaviour', async () => {
    refuse401();
    await withReloadSpy(async reload => {
      renderMessages();
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(reload).not.toHaveBeenCalled();
    });
  });

  it('an explicit teamsHost prop overrides the provided value, in both directions', async () => {
    await withReloadSpy(async reload => {
      // Provider says browser, prop says Teams -> reload.
      const first = render(
        <TeamsHostProvider teamsHost={false}>
          <BffAccessState failure="signin" teamsHost />
        </TeamsHostProvider>
      );
      fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));
      expect(reload).toHaveBeenCalledTimes(1);
      expect(loginRedirect).not.toHaveBeenCalled();
      first.unmount();

      // Provider says Teams, prop says browser -> redirect.
      render(
        <TeamsHostProvider teamsHost>
          <BffAccessState failure="signin" teamsHost={false} />
        </TeamsHostProvider>
      );
      fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));
      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(reload).toHaveBeenCalledTimes(1);
    });
  });
});

// ---------------------------------------------------------------------------
// R4 task 034 (FR-21): Refresh, and the "new message" indicator
// ---------------------------------------------------------------------------

describe('hasNewerMessages (what a background read is compared on)', () => {
  const m = (n: number, over: Partial<Dto> = {}) => dto(n, over);

  it('an empty thread on screen: any message found is new; nothing found is not', () => {
    expect(hasNewerMessages([], [m(1)])).toBe(true);
    expect(hasNewerMessages([], [])).toBe(false);
  });

  it('the same messages are not new, nor are fewer (a message that went away is not a new message)', () => {
    expect(hasNewerMessages([m(1), m(2)], [m(1), m(2)])).toBe(false);
    expect(hasNewerMessages([m(1), m(2)], [m(1)])).toBe(false);
  });

  it('a message that is not on screen and was created after the newest one on screen is new', () => {
    expect(hasNewerMessages([m(1), m(2)], [m(1), m(2), m(3)])).toBe(true);
  });

  it('a message that is not on screen but is OLDER than the newest one shown is not new', () => {
    expect(hasNewerMessages([m(2), m(3)], [m(1), m(2), m(3)])).toBe(false);
  });

  it('a message created in the SAME second as the newest one on screen (second precision) is new when its id is not on screen', () => {
    const at = '2026-03-05T10:00:00Z';
    expect(hasNewerMessages([m(1, { createdon: at })], [m(1, { createdon: at }), m(2, { createdon: at })])).toBe(true);
    // The message already on screen is still not new, whatever its date.
    expect(hasNewerMessages([m(1, { createdon: at })], [m(1, { createdon: at })])).toBe(false);
  });

  it('a message with no usable date never raises the indicator over a dated thread', () => {
    expect(hasNewerMessages([m(1)], [m(1), m(2, { createdon: null })])).toBe(false);
    expect(hasNewerMessages([m(1)], [m(1), m(2, { createdon: 'not a date' })])).toBe(false);
  });
});

describe('Refresh and the new-message check (task 034, FR-21)', () => {
  const INTERVAL = NEW_MESSAGE_CHECK_INTERVAL_MS;
  let visibility: 'visible' | 'hidden';

  /** Runs pending promise continuations and advances the fake clock by `ms`. */
  const flush = (ms = 0): Promise<void> =>
    act(async () => {
      await vi.advanceTimersByTimeAsync(ms);
    });
  const setVisibility = async (value: 'visible' | 'hidden'): Promise<void> => {
    visibility = value;
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
  };
  /** The reads the timer made: the ones that asked for `auth.noInteraction`. */
  const backgroundReads = (): unknown[][] =>
    gets().filter(c => (c[2] as { noInteraction?: boolean } | undefined)?.noInteraction === true);
  const region = (): HTMLElement => screen.getByTestId('record-messages-new-region');
  const indicator = (): HTMLElement | null => screen.queryByTestId('record-messages-new');
  const shownBodies = (): string[] => screen.queryAllByTestId('record-message').map(li => li.textContent ?? '');

  beforeEach(() => {
    vi.useFakeTimers();
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
  });
  afterEach(() => {
    vi.useRealTimers();
    delete (document as unknown as Record<string, unknown>).visibilityState;
  });

  async function open(props: Partial<React.ComponentProps<typeof RecordMessages>> = {}) {
    const report = vi.fn<(action: ThreadRefreshAction | null) => void>();
    const view = renderMessages({ onRefreshActionChange: report, ...props });
    await flush();
    const action = (): ThreadRefreshAction | null => report.mock.calls.at(-1)?.[0] ?? null;
    return { view, report, action };
  }
  /** Lets the user press the thread's Refresh. */
  const press = async (action: () => ThreadRefreshAction | null): Promise<void> => {
    await act(async () => {
      action()?.onClick();
      await vi.advanceTimersByTimeAsync(0);
    });
  };

  // ---- Refresh ----

  it('Refresh is offered once the thread is shown, re-reads it and REPLACES the list (never appends)', async () => {
    thread = [dto(1), dto(2)];
    const { action } = await open();
    expect(action()).toEqual({ onClick: expect.any(Function), busy: false });
    expect(shownBodies()).toHaveLength(2);

    thread = [dto(3)];
    await press(action);
    expect(gets()).toHaveLength(2);
    expect(shownBodies()).toHaveLength(1);
    expect(shownBodies()[0]).toContain('body 3');
    // A read the user asked for is the ordinary read: no `auth` argument.
    expect(gets()[1]).toEqual([LIST_PATH]);
  });

  it('Refresh is not offered while the thread is not shown (a failed load)', async () => {
    bffApiCall.mockImplementation(async () => {
      throw new ApiError(500, 'boom');
    });
    const { action } = await open();
    expect(screen.getByTestId('record-messages-error')).toBeTruthy();
    expect(action()).toBeNull();
  });

  it('a second Refresh while one read is in flight makes no second read; it works again once the read settles', async () => {
    let reads = 0;
    let release!: () => void;
    bffApiCall.mockImplementation(async () => {
      reads++;
      if (reads === 2) await new Promise<void>(resolve => (release = resolve));
      return { value: thread };
    });
    const { action } = await open();
    expect(reads).toBe(1);

    await press(action);
    expect(reads).toBe(2);
    expect(action()?.busy).toBe(true);
    await press(action);
    await press(action);
    expect(reads).toBe(2);

    release();
    await flush();
    expect(action()?.busy).toBe(false);
    await press(action);
    expect(reads).toBe(3);
  });

  // ---- A failed re-read says only what is true (review fix round 1, F1) ----

  describe('a failed refresh says only what is true', () => {
    const PLAIN_COPY = 'The messages could not be refreshed just now.';
    const failReadsFromNow = (): void => {
      bffApiCall.mockImplementation(async () => {
        throw new ApiError(500, 'boom');
      });
    };
    const noticeText = (): string => screen.getByTestId('record-messages-refresh-failed').textContent ?? '';

    it('the header Refresh failing does not claim a message was sent', async () => {
      thread = [dto(1)];
      const { action } = await open();
      failReadsFromNow();
      await press(action);

      expect(noticeText()).toContain(PLAIN_COPY);
      expect(noticeText()).not.toMatch(/was sent/);
      expect(shownBodies()).toHaveLength(1);
    });

    it('"Show new messages" failing does not claim a message was sent', async () => {
      thread = [dto(1)];
      await open();
      thread = [dto(1), dto(2)];
      await flush(INTERVAL);
      expect(indicator()).toBeTruthy();

      failReadsFromNow();
      await act(async () => {
        fireEvent.click(screen.getByRole('button', { name: 'Show new messages' }));
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(noticeText()).toContain(PLAIN_COPY);
      expect(noticeText()).not.toMatch(/was sent/);
    });

    it('the record toolbar Refresh (refreshSignal) failing does not claim a message was sent', async () => {
      thread = [dto(1)];
      const { view } = await open({ refreshSignal: 0 });
      failReadsFromNow();
      view.rerender(
        <FluentProvider theme={webLightTheme}>
          <RecordMessages entity="sprk_matter" recordId={RECORD_ID} refreshSignal={1} />
        </FluentProvider>
      );
      await flush();

      expect(noticeText()).toContain(PLAIN_COPY);
      expect(noticeText()).not.toMatch(/was sent/);
    });

    it('a re-read that fails right after a send still says the message was sent', async () => {
      thread = [dto(1)];
      let posted = false;
      bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
        if (options?.method === 'POST') {
          posted = true;
          return dto(99);
        }
        if (posted) throw new ApiError(500, 'boom');
        return { value: thread };
      });
      await open();
      await act(async () => {
        fireEvent.change(box(), { target: { value: 'sent' } });
      });
      await act(async () => {
        fireEvent.click(sendButton());
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(noticeText()).toContain('Your message was sent');
    });
  });

  // ---- The indicator ----

  it('a background read that finds a newer message raises the indicator in a polite live region and leaves the list alone', async () => {
    thread = [dto(1)];
    await open();
    const live = region();
    expect(live.getAttribute('role')).toBe('status');
    expect(live.getAttribute('aria-live')).toBe('polite');
    expect(live.textContent).toBe('');
    expect(indicator()).toBeNull();

    thread = [dto(1), dto(2)];
    await flush(INTERVAL - 1);
    expect(gets()).toHaveLength(1);
    await flush(1);

    expect(gets()).toHaveLength(2);
    expect(backgroundReads()).toHaveLength(1);
    // The initial read is the ordinary one; the background one is the same route with `auth.noInteraction` and nothing else.
    expect(gets()[0]).toEqual([LIST_PATH]);
    expect(gets()[1]).toEqual([LIST_PATH, {}, { noInteraction: true }]);
    // The region that announces was already there, and now holds the text; the list on screen is untouched.
    expect(region()).toBe(live);
    expect(live.textContent).toContain('New messages are available.');
    expect(shownBodies()).toHaveLength(1);
    expect(shownBodies()[0]).toContain('body 1');
  });

  it('"Show new messages" runs the refresh: the list shows the new message, the indicator clears, focus stays in the section', async () => {
    thread = [dto(1)];
    await open();
    thread = [dto(1), dto(2)];
    await flush(INTERVAL);
    expect(indicator()).toBeTruthy();

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Show new messages' }));
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(shownBodies()).toHaveLength(2);
    expect(indicator()).toBeNull();
    expect(document.activeElement).toBe(region());
    // Exactly one read for the click (plus the initial and the background one).
    expect(gets()).toHaveLength(3);
  });

  it('Refresh in the section header clears the indicator too', async () => {
    thread = [dto(1)];
    const { action } = await open();
    thread = [dto(1), dto(2)];
    await flush(INTERVAL);
    expect(indicator()).toBeTruthy();

    await press(action);
    expect(indicator()).toBeNull();
    expect(shownBodies()).toHaveLength(2);
  });

  it('a background read with nothing new shows nothing', async () => {
    thread = [dto(1), dto(2)];
    await open();
    await flush(INTERVAL);
    expect(backgroundReads()).toHaveLength(1);
    expect(indicator()).toBeNull();
    expect(region().textContent).toBe('');
    expect(shownBodies()).toHaveLength(2);
  });

  it('a successful send clears the indicator, even when the re-read after it fails', async () => {
    thread = [dto(1)];
    let posted = false;
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'POST') {
        posted = true;
        return dto(99);
      }
      if (posted) throw new ApiError(500, 'boom');
      return { value: thread };
    });
    await open();
    thread = [dto(1), dto(2)];
    await flush(INTERVAL);
    expect(indicator()).toBeTruthy();

    await act(async () => {
      fireEvent.change(box(), { target: { value: 'sent' } });
    });
    await act(async () => {
      fireEvent.click(sendButton());
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(posts()).toHaveLength(1);
    expect(screen.getByTestId('record-messages-refresh-failed')).toBeTruthy();
    expect(indicator()).toBeNull();
  });

  it('a record change clears the indicator and stops the checks for the record before', async () => {
    const OTHER = '00000000-0000-4000-8000-000000000202';
    const OTHER_PATH = `/api/v1/external/matters/${OTHER}/messages`;
    thread = [dto(1)];
    bffApiCall.mockImplementation(async (path: string) => ({ value: path === LIST_PATH ? thread : [] }));
    const { view } = await open();
    thread = [dto(1), dto(2)];
    await flush(INTERVAL);
    expect(indicator()).toBeTruthy();

    view.rerender(
      <FluentProvider theme={webLightTheme}>
        <RecordMessages entity="sprk_matter" recordId={OTHER} />
      </FluentProvider>
    );
    await flush();
    expect(indicator()).toBeNull();
    const before = gets().length;

    await flush(3 * INTERVAL);
    const after = gets().slice(before - 1);
    expect(after.length).toBeGreaterThan(1);
    expect(after.every(c => c[0] === OTHER_PATH)).toBe(true);
  });

  it('a background answer that arrives after a refresh started is dropped', async () => {
    let reads = 0;
    let releaseBackground!: () => void;
    bffApiCall.mockImplementation(async () => {
      reads++;
      if (reads === 2) {
        // The background read: slow, and it saw three messages.
        await new Promise<void>(resolve => (releaseBackground = resolve));
        return { value: [dto(1), dto(2), dto(3)] };
      }
      return { value: [dto(1)] };
    });
    const { action } = await open();
    await flush(INTERVAL);
    expect(reads).toBe(2);

    // The user refreshes while it is in flight (the server now says one message); then the old answer lands.
    await press(action);
    expect(reads).toBe(3);
    releaseBackground();
    await flush();
    expect(indicator()).toBeNull();
    expect(shownBodies()).toHaveLength(1);
  });

  // ---- When the checks run ----

  it('makes no read while the document is hidden, and checks once it is visible again', async () => {
    thread = [dto(1)];
    await open();
    await setVisibility('hidden');
    thread = [dto(1), dto(2)];
    await flush(3 * INTERVAL);
    expect(gets()).toHaveLength(1);

    await setVisibility('visible');
    await flush();
    expect(gets()).toHaveLength(2);
    expect(indicator()).toBeTruthy();
  });

  it('keeps one interval between two reads of any kind: a refresh moves the next check back', async () => {
    const { action } = await open();
    await flush(INTERVAL - 10_000);
    await press(action);
    expect(gets()).toHaveLength(2);

    // The timer fires at the original time (10 s on): too soon after the refresh, so it waits.
    await flush(10_000);
    expect(gets()).toHaveLength(2);
    await flush(INTERVAL - 10_001);
    expect(gets()).toHaveLength(2);
    await flush(1);
    expect(gets()).toHaveLength(3);
  });

  it('stops after the component unmounts', async () => {
    const { view } = await open();
    view.unmount();
    await flush(5 * INTERVAL);
    expect(gets()).toHaveLength(1);
  });

  // ---- A failing background read ----

  it('a failing background read is silent: no error, no refresh-failed notice, no sign-in state, the list stays', async () => {
    thread = [dto(1)];
    let fail = false;
    bffApiCall.mockImplementation(async () => {
      if (fail) throw new ApiError(500, 'boom');
      return { value: thread };
    });
    await open();
    fail = true;
    await flush(INTERVAL);

    expect(backgroundReads()).toHaveLength(1);
    expect(screen.queryByTestId('record-messages-error')).toBeNull();
    expect(screen.queryByTestId('record-messages-refresh-failed')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Sign in again' })).toBeNull();
    expect(indicator()).toBeNull();
    expect(shownBodies()).toHaveLength(1);
  });

  it('stops after three failures in a row, and starts again when a refresh the user asked for succeeds', async () => {
    thread = [dto(1)];
    let fail = false;
    bffApiCall.mockImplementation(async () => {
      if (fail) throw new ApiError(503, 'unavailable');
      return { value: thread };
    });
    const { action } = await open();
    fail = true;
    await flush(10 * INTERVAL);
    expect(backgroundReads()).toHaveLength(3);

    fail = false;
    await press(action);
    thread = [dto(1), dto(2)];
    await flush(INTERVAL);
    expect(backgroundReads()).toHaveLength(4);
    expect(indicator()).toBeTruthy();
  });

  it('a 401 on a background read stops the checks at once and shows no sign-in state', async () => {
    let fail = false;
    bffApiCall.mockImplementation(async () => {
      if (fail) throw new ApiError(401, 'expired');
      return { value: thread };
    });
    await open();
    fail = true;
    await flush(5 * INTERVAL);

    expect(backgroundReads()).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Sign in again' })).toBeNull();
    expect(screen.queryByTestId('record-messages-error')).toBeNull();
  });

  // ---- The record template's section ----

  describe('in the record template (MessagesSection)', () => {
    const section = (over: Partial<RecordSectionContext> = {}): RecordSectionContext => ({
      config: undefined as never,
      detail: getRecordDetailEntity('sprk_matter')!,
      recordId: RECORD_ID,
      record: {} as never,
      title: 'Matter MAT-1',
      callerContext: null,
      caller: null,
      onBusyChange: vi.fn(),
      onRecordDeleted: vi.fn(),
      refreshSection: vi.fn(),
      refreshRecord: vi.fn(),
      full: false,
      refreshSignal: 0,
      offerAction: () => undefined,
      headerActions: [],
      readOnlyNote: null,
      ...over,
    });
    const renderSection = () =>
      render(
        <FluentProvider theme={webLightTheme}>
          <MessagesSection {...section()} />
        </FluentProvider>
      );
    const refreshButton = (): HTMLButtonElement =>
      screen.getByRole('button', { name: 'Refresh messages' }) as HTMLButtonElement;

    it('shows Refresh in the section header with no create right, and it re-reads and replaces the thread', async () => {
      thread = [dto(1), dto(2)];
      renderSection();
      await flush();
      // No `canCreate` on the read: New message is not offered, Refresh still is (a read action, FR-06).
      expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
      expect(shownBodies()).toHaveLength(2);

      thread = [dto(3)];
      await act(async () => {
        fireEvent.click(refreshButton());
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(shownBodies()).toHaveLength(1);
      expect(shownBodies()[0]).toContain('body 3');
    });

    it('keeps Refresh enabled and focused while its read is in flight, and a second click makes no second read', async () => {
      let reads = 0;
      let release!: () => void;
      bffApiCall.mockImplementation(async () => {
        reads++;
        if (reads === 2) await new Promise<void>(resolve => (release = resolve));
        return { value: thread };
      });
      renderSection();
      await flush();

      // A keyboard user has the button focused when they press it: focus must stay on it while the read runs.
      refreshButton().focus();
      await act(async () => {
        fireEvent.click(refreshButton());
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(reads).toBe(2);
      expect(refreshButton().disabled).toBe(false);
      expect(document.activeElement).toBe(refreshButton());
      fireEvent.click(refreshButton());
      expect(reads).toBe(2);

      release();
      await flush();
      expect(refreshButton().disabled).toBe(false);
      expect(document.activeElement).toBe(refreshButton());
    });

    it('makes no background read while the section is collapsed, and resumes when it is expanded', async () => {
      renderSection();
      await flush();
      fireEvent.click(screen.getByTestId('section-toggle-messages'));
      await flush(3 * INTERVAL);
      expect(gets()).toHaveLength(1);

      fireEvent.click(screen.getByTestId('section-toggle-messages'));
      await flush(INTERVAL);
      expect(backgroundReads()).toHaveLength(1);
    });
  });
});
