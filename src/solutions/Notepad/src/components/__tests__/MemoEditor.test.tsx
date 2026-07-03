/**
 * Unit tests for MemoEditor (spec FR-17).
 *
 * Approach
 * ────────
 * Notepad's devDeps do NOT include `@testing-library/react`. We use the same
 * react-dom + `act` harness as `useSprkMemoRepository.test.ts`, and dispatch
 * DOM events directly on the underlying <textarea> element (via querySelector).
 *
 * FluentProvider wraps the component under test so Griffel styles + tokens
 * resolve; the component consumes semantic tokens via `makeStyles`.
 *
 * Coverage: Ctrl+Enter, Cmd+Enter, plain Enter, blur, keystroke, composition
 * gate, disabled, ref forwarding, controlled updates, and a smoke check that
 * no hex / rgb color literals appear in the source.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// React 18 concurrent-mode test environment flag — must be set BEFORE React
// is imported so its internal `isConcurrentActEnvironment` check picks it up.
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
// React 18.3+: `act` moved from `react-dom/test-utils` to the `react` package.
import { act } from 'react';
import * as fs from 'fs';
import * as path from 'path';

import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { MemoEditor, type IMemoEditorProps } from '../MemoEditor';

// ─── Harness ─────────────────────────────────────────────────────────────────

interface Harness {
  container: HTMLDivElement;
  textarea: HTMLTextAreaElement;
  ref: React.RefObject<HTMLTextAreaElement>;
  rerender: (patch: Partial<IMemoEditorProps>) => void;
  unmount: () => void;
}

/**
 * Render `<MemoEditor>` inside `<FluentProvider>` and return handles to the
 * underlying <textarea>, the ref, and a rerender/unmount pair.
 */
async function renderEditor(initialProps: IMemoEditorProps): Promise<Harness> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root: Root = createRoot(container);

  const ref = React.createRef<HTMLTextAreaElement>();

  const propsRef: {
    current: IMemoEditorProps;
    setProps?: (patch: Partial<IMemoEditorProps>) => void;
  } = { current: initialProps };

  function Wrapper(): React.ReactElement {
    const [p, setP] = React.useState<IMemoEditorProps>(initialProps);
    propsRef.current = p;
    propsRef.setProps = (patch) => setP((prev) => ({ ...prev, ...patch }));
    return React.createElement(
      FluentProvider,
      { theme: webLightTheme },
      React.createElement(MemoEditor, { ...p, ref })
    );
  }

  await act(async () => {
    root.render(React.createElement(Wrapper));
  });
  await act(async () => {
    await Promise.resolve();
  });

  const textarea = container.querySelector('textarea') as HTMLTextAreaElement | null;
  if (!textarea) {
    throw new Error('MemoEditor harness: <textarea> did not render');
  }

  return {
    container,
    textarea,
    ref,
    rerender: (patch) => {
      act(() => {
        propsRef.setProps?.(patch);
      });
    },
    unmount: () => {
      act(() => {
        root.unmount();
      });
      document.body.removeChild(container);
    },
  };
}

/** Dispatch a keydown event with the given options on the target. */
async function dispatchKeyDown(
  target: HTMLTextAreaElement,
  key: string,
  opts: { ctrlKey?: boolean; metaKey?: boolean; shiftKey?: boolean } = {}
): Promise<KeyboardEvent> {
  const event = new KeyboardEvent('keydown', {
    key,
    ctrlKey: opts.ctrlKey ?? false,
    metaKey: opts.metaKey ?? false,
    shiftKey: opts.shiftKey ?? false,
    bubbles: true,
    cancelable: true,
  });
  await act(async () => {
    target.dispatchEvent(event);
  });
  return event;
}

/**
 * Dispatch an `input` event with the given value on the textarea.
 *
 * Fluent v9 Textarea's onChange is invoked via React's synthetic event system
 * when the underlying <textarea> `input` event fires. We set `.value` then
 * dispatch a native `input` event (which React normalizes to the change).
 */
async function typeInto(target: HTMLTextAreaElement, value: string): Promise<void> {
  await act(async () => {
    // The native setter is required — React tracks the input value via a
    // property descriptor override; direct `.value =` bypasses that on input.
    const proto = window.HTMLTextAreaElement.prototype;
    const desc = Object.getOwnPropertyDescriptor(proto, 'value');
    desc?.set?.call(target, value);
    target.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

/** Dispatch a native focusout event to trigger onBlur. */
async function blur(target: HTMLTextAreaElement): Promise<void> {
  await act(async () => {
    target.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
  });
}

/** Dispatch composition events. */
async function compositionStart(target: HTMLTextAreaElement): Promise<void> {
  await act(async () => {
    target.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
  });
}
async function compositionEnd(target: HTMLTextAreaElement): Promise<void> {
  await act(async () => {
    target.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }));
  });
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('MemoEditor', () => {
  let onChange: jest.Mock;

  beforeEach(() => {
    onChange = jest.fn();
  });

  it('renders a textarea with the initial value', async () => {
    const h = await renderEditor({ value: 'Hello world', onChange });
    expect(h.textarea.value).toBe('Hello world');
    h.unmount();
  });

  it('shows the default placeholder when value is empty', async () => {
    const h = await renderEditor({ value: '', onChange });
    expect(h.textarea.getAttribute('placeholder')).toBe('Start typing your memo...');
    h.unmount();
  });

  it('honors a custom placeholder', async () => {
    const h = await renderEditor({
      value: '',
      onChange,
      placeholder: 'Custom placeholder',
    });
    expect(h.textarea.getAttribute('placeholder')).toBe('Custom placeholder');
    h.unmount();
  });

  it('Ctrl+Enter → onChange called with immediate=true and preventDefault fires', async () => {
    const h = await renderEditor({ value: 'draft body', onChange });
    const event = await dispatchKeyDown(h.textarea, 'Enter', { ctrlKey: true });
    expect(event.defaultPrevented).toBe(true);
    // Immediate save called with current value + immediate flag.
    const immediateCalls = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCalls).toHaveLength(1);
    expect(immediateCalls[0][0]).toBe('draft body');
    h.unmount();
  });

  it('Cmd+Enter (metaKey) → onChange called with immediate=true (macOS parity)', async () => {
    const h = await renderEditor({ value: 'macos draft', onChange });
    const event = await dispatchKeyDown(h.textarea, 'Enter', { metaKey: true });
    expect(event.defaultPrevented).toBe(true);
    const immediateCalls = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCalls).toHaveLength(1);
    expect(immediateCalls[0][0]).toBe('macos draft');
    h.unmount();
  });

  it('plain Enter → onChange NOT called with immediate; default newline preserved', async () => {
    const h = await renderEditor({ value: 'line1', onChange });
    const event = await dispatchKeyDown(h.textarea, 'Enter');
    // preventDefault MUST NOT have been called — Enter → newline is the browser's job.
    expect(event.defaultPrevented).toBe(false);
    const immediateCalls = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCalls).toHaveLength(0);
    h.unmount();
  });

  it('onBlur → onChange called with immediate=true using current value', async () => {
    const h = await renderEditor({ value: 'blur body', onChange });
    await blur(h.textarea);
    const immediateCalls = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCalls).toHaveLength(1);
    expect(immediateCalls[0][0]).toBe('blur body');
    h.unmount();
  });

  it('typing → onChange called with new value WITHOUT immediate flag', async () => {
    const h = await renderEditor({ value: '', onChange });
    await typeInto(h.textarea, 'hello');
    const nonImmediate = onChange.mock.calls.filter(
      ([, opts]) => !opts || opts.immediate !== true
    );
    expect(nonImmediate.length).toBeGreaterThan(0);
    // Last non-immediate call carries the new value.
    expect(nonImmediate[nonImmediate.length - 1][0]).toBe('hello');
    h.unmount();
  });

  it('Ctrl+Enter during IME composition → does NOT fire immediate save', async () => {
    const h = await renderEditor({ value: 'composing', onChange });
    await compositionStart(h.textarea);
    const event = await dispatchKeyDown(h.textarea, 'Enter', { ctrlKey: true });
    // Composition-gated: preventDefault NOT called, and no immediate save fired.
    expect(event.defaultPrevented).toBe(false);
    const immediateCalls = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCalls).toHaveLength(0);

    // After composition ends, Ctrl+Enter works normally again.
    await compositionEnd(h.textarea);
    const event2 = await dispatchKeyDown(h.textarea, 'Enter', { ctrlKey: true });
    expect(event2.defaultPrevented).toBe(true);
    const immediateCallsAfter = onChange.mock.calls.filter(
      ([, opts]) => opts && opts.immediate === true
    );
    expect(immediateCallsAfter).toHaveLength(1);
    h.unmount();
  });

  it('disabled=true → textarea has the disabled attribute', async () => {
    const h = await renderEditor({ value: 'x', onChange, disabled: true });
    expect(h.textarea.disabled).toBe(true);
    h.unmount();
  });

  it('ref forwarding: parent can call ref.current.focus()', async () => {
    const h = await renderEditor({ value: 'ref test', onChange });
    expect(h.ref.current).toBe(h.textarea);
    // The ref should be a real HTMLTextAreaElement with focus().
    expect(typeof h.ref.current?.focus).toBe('function');
    h.unmount();
  });

  it('value updates when the parent rerenders with a new prop', async () => {
    const h = await renderEditor({ value: 'initial', onChange });
    expect(h.textarea.value).toBe('initial');
    h.rerender({ value: 'updated' });
    // Fluent Textarea is controlled — the internal <textarea> reflects the prop.
    expect(h.textarea.value).toBe('updated');
    h.unmount();
  });

  // ─── Source-quality invariant (spec NFR-03 + ADR-021) ──────────────────
  it('MemoEditor source contains zero hex/rgb color literals', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'MemoEditor.tsx'),
      'utf8'
    );
    // Strip comments so JSDoc mentions of "hex" or unrelated URL fragments
    // do not trip the check.
    const codeOnly = src
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
    // Hex color literals like #fff or #123abc.
    expect(codeOnly).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    // rgb(), rgba(), hsl(), hsla() color functions.
    expect(codeOnly).not.toMatch(/\brgba?\s*\(/);
    expect(codeOnly).not.toMatch(/\bhsla?\s*\(/);
  });
});
