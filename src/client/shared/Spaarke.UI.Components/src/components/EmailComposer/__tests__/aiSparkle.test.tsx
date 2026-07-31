/**
 * aiSparkle.test.tsx — Wave E (owner UAT 2026-07-30): compose AI "sparkle" draft.
 *
 * The compose toolbar exposes a sparkle button ONLY when the host wires `onDraftWithAi`. Preset
 * quick-actions (Draft a reply / Make it concise / …) call the host with a stable intent + the CURRENT
 * body/subject; the "Enter prompt" action collects a free-text instruction and runs the `'custom'`
 * intent. The returned text REPLACES the message body.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps, IEmailComposerHandle, IEmailAiDraftResult } from '../EmailComposer.types';

const noopFetch = jest.fn();

const DRAFTED: IEmailAiDraftResult = { text: '<p>Dear Acme, thank you for your note.</p>', isHtml: true };

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="page"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      {...overrides}
    />
  );
  return { ref, ...utils };
}

describe('EmailComposer — compose AI sparkle (Wave E)', () => {
  it('is hidden unless onDraftWithAi is supplied', () => {
    renderComposer({});
    expect(screen.queryByRole('button', { name: /draft with ai/i })).not.toBeInTheDocument();
  });

  it('a preset action calls onDraftWithAi with the intent + current body/subject, then replaces the body', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(DRAFTED);
    const { ref } = renderComposer({
      initialSubject: 'Re: Filing',
      initialBody: '<p>original</p>',
      initialBodyFormat: 'HTML',
      onDraftWithAi,
    });

    fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Make it concise' }));

    await waitFor(() => {
      expect(onDraftWithAi).toHaveBeenCalledWith(
        expect.objectContaining({
          intent: 'concise',
          currentBody: expect.stringContaining('original'),
          isHtml: true,
          subject: 'Re: Filing',
        })
      );
    });
    await waitFor(() => {
      expect(ref.current?.getState().body).toContain('thank you');
    });
  });

  it('the "Enter prompt" action runs the custom intent with the typed instruction', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(DRAFTED);
    renderComposer({ onDraftWithAi });

    fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /enter prompt/i }));

    // The free-text dialog opens.
    await screen.findByText('Draft with AI');
    const textarea = screen.getByRole('textbox', { name: /ai prompt/i });
    fireEvent.change(textarea, { target: { value: 'Ask for the signed contract by Friday.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate' }));

    await waitFor(() => {
      expect(onDraftWithAi).toHaveBeenCalledWith(
        expect.objectContaining({
          intent: 'custom',
          userInstruction: 'Ask for the signed contract by Friday.',
        })
      );
    });
  });

  it('surfaces a message when the draft comes back empty', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue({ text: '' });
    renderComposer({ onDraftWithAi });

    fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Draft a reply' }));

    await screen.findByText(/no draft was produced/i);
  });
});
