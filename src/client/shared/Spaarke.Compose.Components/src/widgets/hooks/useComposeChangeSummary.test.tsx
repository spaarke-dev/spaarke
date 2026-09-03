/**
 * useComposeChangeSummary.test.tsx — the on-demand change-summary flow (spaarkeai-compose-r8, UAT item 8).
 *
 * The assertions that matter are the ones about what does NOT happen: no network call on a dirty editor,
 * and no dispatch when there is nothing to summarise. Both are cases where the cheap bug is to proceed.
 */

import { act, renderHook } from '@testing-library/react';

import type { PullAnnotationsResult } from '../useComposeWordShuttle';
import { useComposeChangeSummary } from './useComposeChangeSummary';

const TARGET = { documentSpeId: 'spe-1', driveId: 'drive-1', tenantId: 'tenant-1' };

function pulled(overrides: Partial<PullAnnotationsResult> = {}): PullAnnotationsResult {
  return {
    documentSpeId: 'spe-1',
    driveId: 'drive-1',
    comments: [],
    revisions: [],
    correlationId: 'corr-1',
    ...overrides,
  };
}

function revision(overrides: Record<string, unknown> = {}) {
  return {
    kind: 'insertion' as const,
    id: 'r1',
    author: 'A. Reviewer',
    date: '2026-09-01T10:00:00Z',
    text: 'and its affiliates',
    anchorText: 'The Company shall indemnify the Client.',
    paragraphHint: 11,
    ...overrides,
  };
}

function setup(opts: {
  dirty?: boolean;
  pullResult?: PullAnnotationsResult;
  pullError?: Error;
  dispatchError?: Error;
}) {
  const pull = jest.fn(async () => {
    if (opts.pullError) throw opts.pullError;
    return opts.pullResult ?? pulled();
  });
  const dispatch = jest.fn(async () => {
    if (opts.dispatchError) throw opts.dispatchError;
  });

  const hook = renderHook(() =>
    useComposeChangeSummary({
      isEditorDirty: () => opts.dirty ?? false,
      pull,
      dispatch,
    })
  );

  return { hook, pull, dispatch };
}

describe('useComposeChangeSummary — the save gate', () => {
  it('refuses on a dirty editor WITHOUT calling the server', async () => {
    // pull-annotations reads STORED bytes. Summarising over a dirty editor silently omits the user's
    // unsaved edits — and the network call to discover that would be wasted anyway.
    const { hook, pull, dispatch } = setup({ dirty: true });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(outcome).toEqual({ kind: 'needs-save' });
    expect(pull).not.toHaveBeenCalled();
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('never saves on the user behalf — a save is a user action', () => {
    // Pinned as an absence: the hook is given no save function at all, so it cannot acquire one by
    // accident. If a future change adds auto-save, this test is where the argument has to be made.
    const { hook } = setup({ dirty: true });

    expect(Object.keys(hook.result.current)).toEqual(['running', 'requestSummary']);
  });
});

describe('useComposeChangeSummary — the refusal', () => {
  it('returns no-changes and does NOT dispatch when the document has no tracked changes', async () => {
    const { hook, pull, dispatch } = setup({ pullResult: pulled() });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(pull).toHaveBeenCalledTimes(1);
    expect(outcome).toEqual({ kind: 'no-changes' });
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('returns no-changes when annotations exist but carry no substance', async () => {
    // The producer owns this definition; the hook must not have a second, looser one.
    const { hook, dispatch } = setup({
      pullResult: pulled({ revisions: [revision({ text: '' })] }),
    });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(outcome).toEqual({ kind: 'no-changes' });
    expect(dispatch).not.toHaveBeenCalled();
  });
});

describe('useComposeChangeSummary — the happy path', () => {
  it('dispatches the produced operand and reports how many changes it described', async () => {
    const { hook, dispatch } = setup({
      pullResult: pulled({
        revisions: [revision(), revision({ id: 'r2', kind: 'deletion', text: 'sole and exclusive' })],
        comments: [
          {
            id: 'c1',
            author: 'B. Counsel',
            date: '2026-09-02T09:30:00Z',
            commentText: 'Should this be mutual?',
            anchorText: 'The Company shall indemnify the Client.',
            paragraphHint: 11,
          },
        ],
      }),
    });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(outcome).toEqual({ kind: 'dispatched', changeCount: 3 });
    expect(dispatch).toHaveBeenCalledTimes(1);

    const operand = dispatch.mock.calls[0][0] as unknown as string;
    expect(operand).toContain('and its affiliates');
    expect(operand).toContain('sole and exclusive');
    expect(operand).toContain('Should this be mutual?');
  });
});

describe('useComposeChangeSummary — failure', () => {
  it('returns a user-safe failure when the pull fails, carrying no server detail', async () => {
    const { hook, dispatch } = setup({ pullError: new Error('500 from /pull-annotations at 10.0.0.4') });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(outcome).toEqual({
      kind: 'failed',
      message: 'The change summary could not be generated. Please try again.',
    });
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('returns a failure — not a false success — when the dispatch itself fails', async () => {
    const { hook } = setup({
      pullResult: pulled({ revisions: [revision()] }),
      dispatchError: new Error('binding rejected'),
    });

    let outcome;
    await act(async () => {
      outcome = await hook.result.current.requestSummary(TARGET);
    });

    expect(outcome).toMatchObject({ kind: 'failed' });
  });

  it('clears the running flag after a failure so the trigger is not left disabled', async () => {
    const { hook } = setup({ pullError: new Error('boom') });

    await act(async () => {
      await hook.result.current.requestSummary(TARGET);
    });

    expect(hook.result.current.running).toBe(false);
  });
});
