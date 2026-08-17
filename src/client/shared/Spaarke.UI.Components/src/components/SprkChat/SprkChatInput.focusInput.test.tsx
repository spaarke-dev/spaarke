/**
 * SprkChatInput.focusInput.test.tsx — FR-05 (spaarkeai-compose-r7 task 061, UC-6) coverage for the new
 * `focusInput()` imperative handle: it moves keyboard focus into the chat textarea WITHOUT mutating its
 * content (contrast `triggerSlashMode()`, which writes `/`). This is the handle SprkChat's
 * `focusInputSignal` host→focus seam calls when a cross-pane Ctrl+Shift+Space arrives.
 */

import * as React from 'react';
import { render, screen, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkChatInput } from './SprkChatInput';
import type { ISprkChatInputHandle } from './types';

function renderInput() {
  const ref = React.createRef<ISprkChatInputHandle>();
  render(
    <FluentProvider theme={webLightTheme}>
      <SprkChatInput ref={ref} onSend={jest.fn()} />
    </FluentProvider>
  );
  return ref;
}

describe('SprkChatInput — focusInput() imperative handle (FR-05)', () => {
  it('moves focus into the textarea', () => {
    const ref = renderInput();
    const textarea = screen.getByRole('textbox');
    expect(textarea).not.toHaveFocus();

    act(() => {
      ref.current?.focusInput();
    });

    expect(textarea).toHaveFocus();
  });

  it('does NOT change the textarea value (focus-only, unlike triggerSlashMode)', () => {
    const ref = renderInput();
    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement;

    act(() => {
      ref.current?.focusInput();
    });

    expect(textarea.value).toBe('');
  });
});
