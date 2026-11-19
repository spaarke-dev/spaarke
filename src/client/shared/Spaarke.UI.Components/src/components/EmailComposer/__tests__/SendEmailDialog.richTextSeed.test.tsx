/**
 * SendEmailDialog.richTextSeed.test.tsx - task 197 (owner dev recheck 2026-11-18).
 *
 * Every composer host opens the body in rich text. A host that has a plain-text pre-fill (the Console's "Send as email" draft,
 * the file-preview and upload-wizard email bodies, a briefing item's draft) converts it with the one shared
 * `plainTextToHtml` and passes `initialBodyFormat="HTML"`. This pins what the composer then shows: the rich-text editor, the
 * same visible text and line breaks, and characters like `<` and `&` shown literally (escaped once, never parsed as markup).
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import { plainTextToHtml } from '../composeOnlyText';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const fetchStub = jest.fn() as unknown as AuthenticatedFetchFn;

function renderDialog(props: Partial<React.ComponentProps<typeof SendEmailDialog>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SendEmailDialog
        open
        onClose={jest.fn()}
        authenticatedFetch={fetchStub}
        bffBaseUrl="https://bff.example.com"
        {...props}
      />
    </FluentProvider>
  );
}

const editor = (): HTMLElement => {
  const el = document.querySelector('[contenteditable="true"]');
  if (!el) throw new Error('the rich-text editor is not shown');
  return el as HTMLElement;
};

describe('SendEmailDialog opens in rich text (task 197)', () => {
  it('a dialog opened blank shows the rich-text editor: toolbar present, toggle offers plain text, no plain-text box', async () => {
    renderDialog();

    await waitFor(() => expect(editor()).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Switch to plain text' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Message body' })).toBeNull();
    expect(document.querySelector('[role="toolbar"]')).not.toBeNull();
  });

  it('a multi-line plain-text seed opens in rich text with the same visible text and line breaks', async () => {
    const seed = 'Dear Alex,\n\nFirst line\nSecond line\n\nKind regards';
    renderDialog({ initialBody: plainTextToHtml(seed), initialBodyFormat: 'HTML' });

    await waitFor(() => expect(editor().textContent).toContain('Kind regards'));
    expect(screen.getByRole('button', { name: 'Switch to plain text' })).toBeInTheDocument();
    const paragraphs = Array.from(editor().querySelectorAll('p')).map(p => p.textContent);
    expect(paragraphs).toEqual(['Dear Alex,', 'First lineSecond line', 'Kind regards']);
    // The single line break inside the middle block survives as a break.
    expect(editor().querySelectorAll('br').length).toBeGreaterThanOrEqual(1);
  });

  it('characters like < & and quotes are shown literally: no double escaping, no markup injected', async () => {
    const seed = 'Budget < $5,000 & "approved" <b>now</b>\nA &amp; B';
    renderDialog({ initialBody: plainTextToHtml(seed), initialBodyFormat: 'HTML' });

    await waitFor(() => expect(editor().textContent).toContain('approved'));
    expect(editor().textContent).toContain('Budget < $5,000 & "approved" <b>now</b>');
    expect(editor().textContent).toContain('A &amp; B'); // typed as the text "&amp;": shown as typed, not as "&"
    expect(editor().querySelector('b')).toBeNull();
  });

  it('must-not-fire: a host that still asks for plain text keeps the plain-text box', () => {
    renderDialog({ initialBody: 'Plain body', initialBodyFormat: 'PlainText' });

    expect(screen.getByRole('textbox', { name: 'Message body' })).toHaveValue('Plain body');
    expect(screen.getByRole('button', { name: 'Switch to rich text' })).toBeInTheDocument();
  });
});
