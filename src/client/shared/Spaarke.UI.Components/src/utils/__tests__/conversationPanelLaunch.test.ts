/**
 * conversationPanelLaunch.test — opening the CommunicationConversationPanel's Messages modal from another control on
 * the same form (#2908: the communications grid's "+ New Message").
 *
 * Contract:
 *  - a panel subscribed for the same record takes the request (opens; `handled`), whatever the GUID's case or braces;
 *  - a panel for another record does not (must not fire), and an unsubscribed panel no longer answers;
 *  - with no panel answering, the result is `no-panel` and nothing else happens: no reload, no navigation (option C);
 *  - an invalid host, or a request with a malformed payload, opens nothing.
 */
import {
  CONVERSATION_PANEL_OPEN_EVENT,
  openConversationPanel,
  requestConversationPanelOpen,
  subscribeConversationPanelOpen,
} from '../conversationPanelLaunch';

const MATTER_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_ID = '44444444-4444-4444-8444-444444444444';

/** A stand-in for the top window: a real EventTarget plus a location we can watch. */
function fakeTop() {
  const target = new EventTarget() as EventTarget & {
    location: { href: string; assign: jest.Mock };
    CustomEvent: typeof CustomEvent;
  };
  target.location = { href: 'https://org.crm.dynamics.com/main.aspx?appid=app-1', assign: jest.fn() };
  target.CustomEvent = CustomEvent;
  return target as unknown as Window & { location: { href: string; assign: jest.Mock } };
}

describe('conversationPanelLaunch', () => {
  afterEach(() => jest.restoreAllMocks());

  it('a panel for the same record opens and the request reports handled (any case, braces)', () => {
    const top = fakeTop();
    const onOpen = jest.fn();
    subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: `{${MATTER_ID.toUpperCase()}}` }, onOpen, top);

    expect(requestConversationPanelOpen({ entityType: 'SPRK_MATTER', id: MATTER_ID }, top)).toBe(true);
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('a panel for another record does not open, and the request is not handled (must not fire)', () => {
    const top = fakeTop();
    const onOpen = jest.fn();
    subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: OTHER_ID }, onOpen, top);
    subscribeConversationPanelOpen({ entityType: 'sprk_project', id: MATTER_ID }, onOpen, top);

    expect(requestConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, top)).toBe(false);
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('after unsubscribing, the panel no longer answers', () => {
    const top = fakeTop();
    const onOpen = jest.fn();
    const off = subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, onOpen, top);
    off();

    expect(requestConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, top)).toBe(false);
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('openConversationPanel opens in place when a panel answers, and does not navigate', () => {
    const top = fakeTop();
    subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, jest.fn(), top);

    expect(openConversationPanel({ entityType: 'sprk_matter', id: MATTER_ID }, top)).toBe('opened');
    expect(top.location.assign).not.toHaveBeenCalled();
  });

  it('with no panel on the page, it reports no-panel and does not reload or navigate', () => {
    const top = fakeTop();
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(openConversationPanel({ entityType: 'sprk_matter', id: MATTER_ID }, top)).toBe('no-panel');
    expect(top.location.assign).not.toHaveBeenCalled();
  });

  it('an invalid host opens nothing and does not navigate', () => {
    const top = fakeTop();
    const onOpen = jest.fn();
    subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, onOpen, top);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(openConversationPanel({ entityType: 'sprk_matter', id: 'nope' }, top)).toBe('no-panel');
    expect(openConversationPanel({ entityType: 'sprk_matter"><x', id: MATTER_ID }, top)).toBe('no-panel');
    expect(onOpen).not.toHaveBeenCalled();
    expect(top.location.assign).not.toHaveBeenCalled();
  });

  it('a request with a malformed payload is ignored by the panel', () => {
    const top = fakeTop();
    const onOpen = jest.fn();
    subscribeConversationPanelOpen({ entityType: 'sprk_matter', id: MATTER_ID }, onOpen, top);

    top.dispatchEvent(new CustomEvent(CONVERSATION_PANEL_OPEN_EVENT, { detail: `sprk_matter:${MATTER_ID}` }));
    top.dispatchEvent(new CustomEvent(CONVERSATION_PANEL_OPEN_EVENT, { detail: null }));
    top.dispatchEvent(
      new CustomEvent(CONVERSATION_PANEL_OPEN_EVENT, { detail: { entityType: 'sprk_matter', id: 'x' } })
    );

    expect(onOpen).not.toHaveBeenCalled();
  });
});
