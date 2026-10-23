/**
 * R3 task 051 (FR-04 read thread, FR-11 post) — the Messages section of the record detail view.
 *
 * What runs for real: `RecordMessages` (list, compose, error states), `messages-client` (paths, body, ProblemDetails
 * parsing), the shared `sanitizeEmailHtml` (DOMPurify in jsdom), `BffAccessState` and the Fluent v9 components. What is
 * substituted: the network seam (`bffApiCall`, answering the external message routes and throwing `ApiError` exactly
 * as bff-client builds it - the raw ProblemDetails body is the message) and MSAL's `useMsal` (BffAccessState reads it).
 */
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
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
import { MAX_MESSAGE_BODY_LENGTH, MESSAGE_ROOTS } from '../src/api/messages-client';

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
