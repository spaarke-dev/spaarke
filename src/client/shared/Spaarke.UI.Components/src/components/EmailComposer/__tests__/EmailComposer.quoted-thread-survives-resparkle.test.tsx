/**
 * EmailComposer.quoted-thread-survives-resparkle.test.tsx
 * (spaarkeai-assistant-enhancements-r3 task 025 — D-5 fix, per notes/defer-issues.md)
 *
 * D-5: when a Reply/Forward card seeds the composer via `openComposer({bodyOverride})`
 * on the Layer-1 `initialBody` path (no `sourceRecord` passed to the engine), the
 * reducer's `quotedThread` state stayed empty. If the user then clicked the in-dialog
 * sparkle again, `runAiDraft` re-appended `state.quotedThread` (empty) — DROPPING the
 * already-seeded quoted thread baked into `initialBody`.
 *
 * The fix: `IEmailComposerProps.initialQuotedThread` seeds `state.quotedThread` on the
 * props-only path (`EmailComposer.reducer.ts` `initialState`), reaching parity with the
 * `sourceRecord` path (`deriveReplyState`/`deriveForwardState`).
 *
 * This suite proves the fix end-to-end at the component level (mirrors
 * `aiSparkle.test.tsx`'s harness): seed a card body + `initialQuotedThread` (exactly
 * what `useEmailComposeActions.openComposer({bodyOverride})` now does), click the
 * in-dialog sparkle, and assert the quoted thread SURVIVES in the resulting body.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps, IEmailComposerHandle, IEmailAiDraftResult } from '../EmailComposer.types';

const noopFetch = jest.fn();

const QUOTED_THREAD =
  '<p>On Jul 24, 2026 9:00 AM, sender@contoso.com wrote:</p><hr/><p>Original thread body — keep me.</p>';
const CARD_SEEDED_DRAFT = '<p>Thanks — I will review and revert by Friday.</p>';
const SEEDED_BODY = `${CARD_SEEDED_DRAFT}<p></p>${QUOTED_THREAD}`;
const RESPARKLE_RESULT: IEmailAiDraftResult = { text: '<p>Actually, let me get back to you tomorrow.</p>', isHtml: true };

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="reply"
      mount="dialog"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      {...overrides}
    />
  );
  return { ref, ...utils };
}

async function openQuickResponses() {
  fireEvent.click(screen.getByRole('button', { name: /draft with ai/i }));
  fireEvent.click(await screen.findByRole('button', { name: /quick responses/i }));
}

describe('D-5 — quoted thread survives an in-dialog re-sparkle after a card-seeded bodyOverride', () => {
  it('FIX: with initialQuotedThread seeded (the openComposer({bodyOverride}) path), the re-sparkle body contains BOTH the new draft AND the original quoted thread', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(RESPARKLE_RESULT);
    const { ref } = renderComposer({
      initialSubject: 'Re: Quarterly review',
      initialBody: SEEDED_BODY,
      initialBodyFormat: 'HTML',
      initialQuotedThread: QUOTED_THREAD,
      onDraftWithAi,
    });

    await openQuickResponses();
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Make it concise' }));

    await waitFor(() => {
      // The Lexical rich-text editor re-serializes the seeded HTML into its own normalized
      // markup on mount (editor-paragraph wrappers) — assert on the PLAIN TEXT content, not
      // the exact original HTML tags.
      expect(onDraftWithAi).toHaveBeenCalledWith(
        expect.objectContaining({
          intent: 'concise',
          currentBody: expect.stringContaining('Thanks — I will review and revert by Friday.'),
        })
      );
    });
    await waitFor(() => {
      const body = ref.current?.getState().body ?? '';
      // The NEW re-sparkle draft is present…
      expect(body).toContain('get back to you tomorrow');
      // …AND the ORIGINAL quoted thread survives below it (the D-5 invariant).
      expect(body).toContain('Original thread body — keep me.');
    });
  });

  it('REGRESSION GUARD: without initialQuotedThread (the pre-fix shape), the re-sparkle DROPS the quoted thread', async () => {
    const onDraftWithAi = jest.fn().mockResolvedValue(RESPARKLE_RESULT);
    const { ref } = renderComposer({
      initialSubject: 'Re: Quarterly review',
      initialBody: SEEDED_BODY,
      initialBodyFormat: 'HTML',
      // initialQuotedThread intentionally omitted — reproduces the pre-fix defect.
      onDraftWithAi,
    });

    await openQuickResponses();
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Make it concise' }));

    await waitFor(() => {
      const body = ref.current?.getState().body ?? '';
      expect(body).toContain('get back to you tomorrow');
    });
    // Demonstrates the defect this fix prevents: the thread is gone when quotedThread
    // was never seeded — this is exactly what `initialQuotedThread` (D-5 fix) prevents.
    const finalBody = ref.current?.getState().body ?? '';
    expect(finalBody).not.toContain('Original thread body — keep me.');
  });
});
