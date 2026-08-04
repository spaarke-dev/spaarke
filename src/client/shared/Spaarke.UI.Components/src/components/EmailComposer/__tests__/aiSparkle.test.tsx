/**
 * aiSparkle.test.tsx — Wave E compose AI "sparkle" draft (redesigned owner UAT 2026-08-03 R5 item 5).
 *
 * The compose toolbar exposes a sparkle button ONLY when the host wires `onDraftWithAi`. Clicking it
 * opens an inline Popover with (a) a free-text prompt (type + Generate → the `'custom'` intent) and
 * (b) a "+" quick-responses menu. Whole-draft quick actions (Draft a reply / Summarize the thread /
 * Fix grammar) call the host with a stable intent + the CURRENT body/subject and REPLACE the body.
 * Selection-scoped quick actions (Make it concise / Formal tone / Friendly tone) appear ONLY when the
 * author has a live text selection — with no selection (the jsdom default) they are hidden.
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

/** Open the sparkle Popover, then open the "+" quick-responses menu. */
async function openQuickResponses() {
  fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
  fireEvent.click(await screen.findByRole('button', { name: /quick responses/i }));
}

describe('EmailComposer — compose AI sparkle (Wave E / R5 redesign)', () => {
  it('is hidden unless onDraftWithAi is supplied', () => {
    renderComposer({});
    expect(screen.queryByRole('button', { name: /draft with ai/i })).not.toBeInTheDocument();
  });

  it('a whole-draft quick action calls onDraftWithAi with the intent + current body/subject, then replaces the body', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(DRAFTED);
    const { ref } = renderComposer({
      initialSubject: 'Re: Filing',
      initialBody: '<p>original</p>',
      initialBodyFormat: 'HTML',
      onDraftWithAi,
    });

    await openQuickResponses();
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Summarize the thread' }));

    await waitFor(() => {
      expect(onDraftWithAi).toHaveBeenCalledWith(
        expect.objectContaining({
          intent: 'summarize',
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

  it('the inline prompt runs the custom intent with the typed instruction', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(DRAFTED);
    renderComposer({ onDraftWithAi });

    // The prompt textarea is inline in the popover — no "Enter prompt" hop.
    fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
    const textarea = await screen.findByRole('textbox', { name: /ai prompt/i });
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

  it('selection-scoped quick actions are hidden when nothing is selected', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(DRAFTED);
    renderComposer({ initialBody: '<p>original</p>', initialBodyFormat: 'HTML', onDraftWithAi });

    await openQuickResponses();

    // Whole-draft actions are present; selection-only ones are not (no live selection in jsdom).
    expect(await screen.findByRole('menuitem', { name: 'Summarize the thread' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Make it concise' })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Formal tone' })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Friendly tone' })).not.toBeInTheDocument();
  });

  it('surfaces a message when the draft comes back empty', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue({ text: '' });
    renderComposer({ onDraftWithAi });

    await openQuickResponses();
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Draft a reply' }));

    await screen.findByText(/no draft was produced/i);
  });
});
