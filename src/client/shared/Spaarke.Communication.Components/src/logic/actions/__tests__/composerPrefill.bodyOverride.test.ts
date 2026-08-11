/**
 * composerPrefill.bodyOverride.test.ts
 * (spaarkeai-assistant-enhancements-r3 task 024, FR-10 — BINDING invariant)
 *
 * The thread-preservation invariant at the pure-logic seam: an AI `bodyOverride`
 * on `deriveComposerFields` seeds ONLY the authored-message region — the derived
 * quoted previous thread is STILL appended BELOW it, so the composed body is
 * `[AI draft] + [separator] + [quoted thread]`, reaching parity with the
 * in-dialog sparkle re-append (`EmailComposer.runAiDraft`). A `bodyOverride`
 * that replaces the WHOLE body (dropping the quoted thread) is a DEFECT this
 * suite asserts against.
 *
 * Pure (no React / no Xrm) — this is the reducer/logic-level guard behind the
 * hook (`useEmailComposeActions`), which both mounts (SpaarkeAi widget +
 * standalone `sprk_emailpage`) share (NFR-05 dual-mount parity).
 */
import {
  deriveComposerFields,
  buildQuotedThread,
  DRAFT_QUOTE_SEPARATOR_HTML,
  type RecordPrefill,
} from '../composerPrefill';

const RECORD: RecordPrefill = {
  from: 'sender@contoso.com',
  to: 'a@x.com; b@y.com',
  cc: 'c@z.com',
  subject: 'Quarterly review',
  body: '<p>Original thread body — keep me.</p>',
  sent: 'Jul 24, 2026 9:00 AM',
};

const AI_DRAFT = '<p>Thanks — I will review and revert by Friday.</p>';

/** Distinctive substrings that MUST survive in the composed body if the quoted thread is preserved. */
const THREAD_MARKERS = ['Original thread body — keep me.', '<blockquote>', 'From:'] as const;

function expectThreadPreserved(initialBody: string | undefined) {
  expect(initialBody).toBeDefined();
  const body = initialBody as string;
  for (const marker of THREAD_MARKERS) {
    expect(body).toContain(marker);
  }
}

describe('deriveComposerFields — FR-10 bodyOverride thread preservation', () => {
  it('reply: composes [AI draft] + [separator] + [quoted thread] — draft ABOVE, thread STILL below', () => {
    const f = deriveComposerFields('reply', RECORD, { bodyOverride: AI_DRAFT });
    const quoted = buildQuotedThread(RECORD);

    // Draft seeds the authored region (top); the historical empty lead is gone.
    expect(f.initialBody?.startsWith(AI_DRAFT)).toBe(true);
    expect(f.initialBody?.startsWith('<p></p><p></p>')).toBe(false);
    // The quoted thread is STILL present below the draft, separated by the sparkle-parity separator.
    expectThreadPreserved(f.initialBody);
    expect(f.initialBody).toBe(`${AI_DRAFT}${DRAFT_QUOTE_SEPARATOR_HTML}${quoted}`);
    // Recipients + subject unchanged by the override.
    expect(f.initialTo).toEqual(['sender@contoso.com']);
    expect(f.initialSubject).toBe('Re: Quarterly review');
  });

  it('replyAll: preserves its quoted thread (deriveReplyState family) with the draft above + recipients intact', () => {
    const f = deriveComposerFields('replyAll', RECORD, { bodyOverride: AI_DRAFT });
    const quoted = buildQuotedThread(RECORD);

    expect(f.initialBody?.startsWith(AI_DRAFT)).toBe(true);
    expectThreadPreserved(f.initialBody);
    expect(f.initialBody).toBe(`${AI_DRAFT}${DRAFT_QUOTE_SEPARATOR_HTML}${quoted}`);
    expect(f.initialTo).toEqual(['sender@contoso.com']);
    expect(f.initialCc).toEqual(expect.arrayContaining(['a@x.com', 'b@y.com', 'c@z.com']));
  });

  it('forward: preserves its quoted thread (deriveForwardState) with the draft above + Fwd: subject', () => {
    const f = deriveComposerFields('forward', RECORD, { bodyOverride: AI_DRAFT });
    const quoted = buildQuotedThread(RECORD);

    expect(f.initialBody?.startsWith(AI_DRAFT)).toBe(true);
    expectThreadPreserved(f.initialBody);
    expect(f.initialBody).toBe(`${AI_DRAFT}${DRAFT_QUOTE_SEPARATOR_HTML}${quoted}`);
    expect(f.initialSubject).toBe('Fwd: Quarterly review');
  });

  it('NEGATIVE (defect guard): a bodyOverride must NOT whole-body-replace — the composed body is never just the draft, and the thread survives', () => {
    for (const mode of ['reply', 'replyAll', 'forward'] as const) {
      const f = deriveComposerFields(mode, RECORD, { bodyOverride: AI_DRAFT });
      // A whole-body-replace would make initialBody === AI_DRAFT (thread dropped) — that is the DEFECT.
      expect(f.initialBody).not.toBe(AI_DRAFT);
      // And the thread must still be there (this assertion FAILS if a future change drops the quote).
      expectThreadPreserved(f.initialBody);
      // The composed body is strictly longer than the draft alone (draft + separator + thread).
      expect((f.initialBody as string).length).toBeGreaterThan(AI_DRAFT.length);
    }
  });

  it('REGRESSION: with no bodyOverride the historical prefill is byte-for-byte unchanged (dual-mount parity)', () => {
    const quoted = buildQuotedThread(RECORD);

    const reply = deriveComposerFields('reply', RECORD);
    expect(reply.initialBody).toBe(`<p></p><p></p>${quoted}`);

    const replyAll = deriveComposerFields('replyAll', RECORD);
    expect(replyAll.initialBody).toBe(`<p></p><p></p>${quoted}`);

    const forward = deriveComposerFields('forward', RECORD);
    expect(forward.initialBody).toBe(quoted);

    // Identical to passing an explicit-undefined override (options object present, no draft).
    expect(deriveComposerFields('reply', RECORD, { bodyOverride: undefined }).initialBody).toBe(reply.initialBody);
  });

  it('a blank/whitespace bodyOverride is ignored — falls back to the historical lead (thread still preserved)', () => {
    const quoted = buildQuotedThread(RECORD);
    const f = deriveComposerFields('reply', RECORD, { bodyOverride: '   ' });
    expect(f.initialBody).toBe(`<p></p><p></p>${quoted}`);
    expectThreadPreserved(f.initialBody);
  });

  it('compose (New): an AI bodyOverride seeds the body directly — no thread to preserve', () => {
    const f = deriveComposerFields('compose', null, { bodyOverride: AI_DRAFT });
    expect(f.initialBody).toBe(AI_DRAFT);
    // And with no override, compose stays fully empty (unchanged).
    expect(deriveComposerFields('compose', null).initialBody).toBeUndefined();
  });
});
