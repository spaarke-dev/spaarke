/**
 * focusReturn.iframe.test.ts - ontology task 238 (c).
 *
 * A surface inside an iframe (the Document Relationship Viewer's composer, framed by Semantic Search's Find Similar
 * dialog) must end with keyboard focus on its trigger, with the FRAME holding the top-level focus. jsdom does not move
 * top-level focus, so the frame's focus is modelled the way a browser behaves: `element.focus()` records the element as
 * the frame document's activeElement but does not make the frame the focused frame (`doc.hasFocus()`); only the frame
 * window's own `focus()` does, and the lookup pane that just closed leaves the frame without it.
 */
import { scheduleFocusReturn, FRAMED_FOCUS_CONFIRM_MS, FRAMED_FOCUS_RETURN_MAX_ATTEMPTS } from '../focusReturn';

interface Frame {
  iframe: HTMLIFrameElement;
  win: Window;
  doc: Document;
  /** True while this frame holds the top-level focus (what `doc.hasFocus()` reports). */
  topLevelFocus: () => boolean;
  /** The platform pane took the top-level focus away from the frame. */
  paneTakesFocus: () => void;
  winFocus: jest.Mock;
}

function makeFrame(): Frame {
  const iframe = document.createElement('iframe');
  document.body.appendChild(iframe);
  const win = iframe.contentWindow as Window;
  const doc = iframe.contentDocument as Document;
  let hasTopLevelFocus = false;
  const winFocus = jest.fn(() => {
    hasTopLevelFocus = true;
  });
  Object.defineProperty(win, 'focus', { value: winFocus, configurable: true });
  Object.defineProperty(doc, 'hasFocus', { value: () => hasTopLevelFocus, configurable: true });
  // The frame's timers run on the (fake) clock of the test window.
  Object.defineProperty(win, 'setTimeout', {
    value: (fn: () => void, ms?: number) => window.setTimeout(fn, ms),
    configurable: true,
  });
  Object.defineProperty(win, 'clearTimeout', { value: (id: number) => window.clearTimeout(id), configurable: true });
  return {
    iframe,
    win,
    doc,
    topLevelFocus: () => hasTopLevelFocus,
    paneTakesFocus: () => {
      hasTopLevelFocus = false;
    },
    winFocus,
  };
}

function addButton(doc: Document, label = 'To'): HTMLButtonElement {
  const button = doc.createElement('button');
  button.textContent = label;
  doc.body.appendChild(button);
  return button;
}

const advance = (ms: number) => jest.advanceTimersByTime(ms);

/** Models what the TOP page reports as its focused element (jsdom does not move it when a frame is focused). */
function setTopActiveElement(el: Element | null): () => void {
  Object.defineProperty(document, 'activeElement', { get: () => el, configurable: true });
  return () => {
    delete (document as unknown as Record<string, unknown>).activeElement;
  };
}

describe('scheduleFocusReturn in an iframe (task 238 c)', () => {
  let frame: Frame;
  beforeEach(() => {
    jest.useFakeTimers();
    frame = makeFrame();
  });
  afterEach(() => {
    frame.iframe.remove();
    jest.useRealTimers();
  });

  it('takes the frame the top-level focus before focusing the trigger (activeElement alone is not enough)', () => {
    const to = addButton(frame.doc);
    frame.paneTakesFocus();

    scheduleFocusReturn({ doc: frame.doc, target: () => to });
    advance(0);

    expect(frame.winFocus).toHaveBeenCalled();
    expect(frame.doc.activeElement).toBe(to);
    expect(frame.topLevelFocus()).toBe(true);
  });

  it('keeps going when the trigger already is the frame document activeElement but the frame lost the focus', () => {
    // After the pane closes the frame document still reports the To button as active; the keys go to the page.
    const to = addButton(frame.doc);
    to.focus();
    frame.paneTakesFocus();
    const root = frame.doc.body;

    scheduleFocusReturn({ doc: frame.doc, target: () => to, isKept: active => !!active && root.contains(active) });
    advance(0);

    expect(frame.topLevelFocus()).toBe(true);
    expect(frame.doc.activeElement).toBe(to);
  });

  it('still lands when the frame is inert for longer than the top-level window of 10 x 50 ms', () => {
    const to = addButton(frame.doc);
    let inert = true;
    const realFocus = to.focus.bind(to);
    jest.spyOn(to, 'focus').mockImplementation(() => {
      if (!inert) realFocus();
    });
    frame.paneTakesFocus();
    window.setTimeout(() => {
      inert = false; // the parent page lifts `inert` from its dialog late
    }, 900);

    scheduleFocusReturn({ doc: frame.doc, target: () => to });
    advance(1000);

    expect(frame.doc.activeElement).toBe(to);
    expect(frame.topLevelFocus()).toBe(true);
  });

  it('puts focus back when the platform takes it from the frame right after it landed (top page shows the iframe)', () => {
    const restoreTop = setTopActiveElement(frame.iframe);
    try {
      const to = addButton(frame.doc);
      scheduleFocusReturn({ doc: frame.doc, target: () => to });
      advance(0);
      expect(frame.doc.activeElement).toBe(to);

      // The pane's own focus restore arrives late and leaves nobody focused in the frame.
      advance(40);
      to.blur();
      frame.paneTakesFocus();
      advance(FRAMED_FOCUS_CONFIRM_MS + 200);

      expect(frame.doc.activeElement).toBe(to);
      expect(frame.topLevelFocus()).toBe(true);
    } finally {
      restoreTop();
    }
  });

  it('puts focus back when the top page has nobody focused (body) after the platform took it', () => {
    const restoreTop = setTopActiveElement(document.body);
    try {
      const to = addButton(frame.doc);
      scheduleFocusReturn({ doc: frame.doc, target: () => to });
      advance(0);
      advance(40);
      to.blur();
      frame.paneTakesFocus();
      advance(FRAMED_FOCUS_CONFIRM_MS + 200);

      expect(frame.doc.activeElement).toBe(to);
      expect(frame.topLevelFocus()).toBe(true);
    } finally {
      restoreTop();
    }
  });

  it('does not take the focus back when the top page focused a real element after the frame landed', () => {
    const parentButton = document.createElement('button');
    document.body.appendChild(parentButton);
    const restoreTop = setTopActiveElement(frame.iframe);
    try {
      const to = addButton(frame.doc);
      scheduleFocusReturn({ doc: frame.doc, target: () => to });
      advance(0);
      expect(frame.winFocus).toHaveBeenCalledTimes(1);

      // The user clicks a button in the parent dialog: the frame loses the top-level focus.
      restoreTop();
      setTopActiveElement(parentButton);
      frame.paneTakesFocus();
      advance(FRAMED_FOCUS_CONFIRM_MS + 2000);

      expect(frame.winFocus).toHaveBeenCalledTimes(1);
      expect(frame.topLevelFocus()).toBe(false);
    } finally {
      delete (document as unknown as Record<string, unknown>).activeElement;
      parentButton.remove();
    }
  });

  it('does not take the focus when the top document cannot be read (cross-origin)', () => {
    const to = addButton(frame.doc);
    frame.paneTakesFocus();
    // Reading the top page's focused element throws, as it does when the top page is another origin.
    Object.defineProperty(document, 'activeElement', {
      get: () => {
        throw new Error('SecurityError: Blocked a frame from accessing a cross-origin frame');
      },
      configurable: true,
    });

    try {
      scheduleFocusReturn({ doc: frame.doc, target: () => to });
      advance(2000);

      expect(frame.winFocus).not.toHaveBeenCalled();
      expect(frame.doc.activeElement).not.toBe(to);
    } finally {
      delete (document as unknown as Record<string, unknown>).activeElement;
    }
  });

  it('does not fight a host that deliberately focused another element in the frame', () => {
    const to = addButton(frame.doc);
    const other = addButton(frame.doc, 'host chose this');
    jest.spyOn(to, 'focus').mockImplementation(() => undefined); // swallowed: inert

    scheduleFocusReturn({ doc: frame.doc, target: () => to });
    advance(0);
    other.focus();
    advance(1000);

    expect(frame.doc.activeElement).toBe(other);
  });

  it('gives up after the bounded number of attempts when focus never lands', () => {
    const to = addButton(frame.doc);
    const focusSpy = jest.spyOn(to, 'focus').mockImplementation(() => undefined);

    scheduleFocusReturn({ doc: frame.doc, target: () => to });
    advance(10_000);

    expect(focusSpy).toHaveBeenCalledTimes(FRAMED_FOCUS_RETURN_MAX_ATTEMPTS);
  });

  it('cancel stops the pending attempt', () => {
    const to = addButton(frame.doc);
    const cancel = scheduleFocusReturn({ doc: frame.doc, target: () => to });
    cancel();
    advance(1000);

    expect(frame.winFocus).not.toHaveBeenCalled();
    expect(frame.doc.activeElement).not.toBe(to);
  });
});

describe('scheduleFocusReturn in the top-level window is unchanged (task 238 c)', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('does not touch window.focus and stops after 10 attempts', () => {
    const button = document.createElement('button');
    document.body.appendChild(button);
    const winFocus = jest.spyOn(window, 'focus').mockImplementation(() => undefined);
    const focusSpy = jest.spyOn(button, 'focus').mockImplementation(() => undefined);
    try {
      scheduleFocusReturn({ doc: document, target: () => button });
      advance(10_000);

      expect(winFocus).not.toHaveBeenCalled();
      expect(focusSpy).toHaveBeenCalledTimes(10);
    } finally {
      winFocus.mockRestore();
      focusSpy.mockRestore();
      button.remove();
    }
  });
});
