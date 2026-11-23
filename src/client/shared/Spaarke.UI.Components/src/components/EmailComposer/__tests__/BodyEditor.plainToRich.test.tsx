/**
 * BodyEditor.plainToRich.test.tsx - task 197 fix round 1.
 *
 * Every composer host now opens in rich text, so a rich -> plain -> rich toggle is common. Switching Plain -> Rich used to pass
 * the textarea's text to the rich editor unconverted, and the editor parses its value as HTML: line breaks collapsed into one
 * paragraph and a typed `<b>x</b>` became bold. The switch now converts with the shared `plainTextToHtml`.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const fetchStub = jest.fn() as unknown as AuthenticatedFetchFn;

const editor = (): HTMLElement => {
  const el = document.querySelector('[contenteditable="true"]');
  if (!el) throw new Error('the rich-text editor is not shown');
  return el as HTMLElement;
};

describe('BodyEditor plain -> rich switch (task 197 fix round 1)', () => {
  it('keeps paragraphs and shows typed markup and ampersands literally', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={jest.fn()}
          authenticatedFetch={fetchStub}
          bffBaseUrl="https://bff.example.com"
          initialBody=""
          initialBodyFormat="PlainText"
        />
      </FluentProvider>
    );

    fireEvent.change(screen.getByRole('textbox', { name: 'Message body' }), {
      target: { value: 'a\n\nb <b>x</b> & c' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Switch to rich text' }));

    await waitFor(() => expect(editor()).toBeInTheDocument());
    await waitFor(() => expect(editor().textContent).toContain('b <b>x</b> & c'));
    const paragraphs = Array.from(editor().querySelectorAll('p')).map(p => p.textContent);
    expect(paragraphs).toEqual(['a', 'b <b>x</b> & c']);
    expect(editor().querySelector('b, strong')).toBeNull();
    expect(editor().textContent).not.toContain('&amp;');
  });
});
