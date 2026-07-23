/**
 * useDocQaCitationBridge.test.ts — FR-35 Document Q&A over the session-mounted
 * document (spaarkeai-compose-r2 task 072, stretch).
 *
 * Proves the grounded-output invariant at the bridge boundary:
 *   - a CITED answer (non-empty ICitation[] with excerpt) dispatches BOTH the
 *     `compose_qa_highlight` (workspace) and `compose_assistant_insight`
 *     (context, kind 'citation') events, exactly once per distinct citation;
 *   - an UNCITED answer (empty array, or a citation with no excerpt) does NOT
 *     dispatch anything — no highlight, no Context-pane insight;
 *   - a re-fire with the SAME citation (React effect double-invoke) does not
 *     re-dispatch (dedupe guard).
 */
import { renderHook } from '@testing-library/react';
import type { ICitation } from '@spaarke/ui-components';
import { useDocQaCitationBridge } from '../useDocQaCitationBridge';

function setup(sessionId: string | null = 'sess-1') {
  const dispatch = jest.fn();
  const getSessionId = jest.fn(() => sessionId);
  const { result } = renderHook(() => useDocQaCitationBridge({ dispatch, getSessionId }));
  return { dispatch, getSessionId, result };
}

const CITATION: ICitation = {
  id: 1,
  source: 'Section 7.3',
  excerpt: 'the indemnification cap is five hundred thousand dollars',
};

describe('useDocQaCitationBridge — grounded-output invariant (task 072)', () => {
  it('cited answer: dispatches compose_qa_highlight (workspace) + compose_assistant_insight (context, citation)', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([CITATION]);

    expect(dispatch).toHaveBeenCalledTimes(2);

    const workspaceCall = dispatch.mock.calls.find(([channel]) => channel === 'workspace');
    expect(workspaceCall).toBeDefined();
    expect(workspaceCall![1]).toMatchObject({
      type: 'compose_qa_highlight',
      qaSourceText: CITATION.excerpt,
      qaSectionLabel: 'Section 7.3',
      sessionId: 'sess-1',
    });

    const contextCall = dispatch.mock.calls.find(([channel]) => channel === 'context');
    expect(contextCall).toBeDefined();
    expect(contextCall![1]).toMatchObject({
      type: 'compose_assistant_insight',
      insightKind: 'citation',
      insightText: 'Cited from Section 7.3',
      sessionId: 'sess-1',
    });
  });

  it('uncited answer (empty citations array): dispatches NOTHING', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([]);

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('a citation with no excerpt text: dispatches NOTHING (nothing to locate/cite)', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([{ id: 2, source: 'Doc', excerpt: '' }]);

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('dedupe: re-firing with the SAME citation does not re-dispatch', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([CITATION]);
    result.current.onCitations([CITATION]);

    expect(dispatch).toHaveBeenCalledTimes(2); // one workspace + one context call, not four
  });

  it('a DIFFERENT subsequent citation dispatches again (dedupe key changes)', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([CITATION]);
    result.current.onCitations([{ id: 5, source: 'Section 9.1', excerpt: 'a different clause entirely' }]);

    expect(dispatch).toHaveBeenCalledTimes(4);
  });

  it('falls back to a generic Context insight label when the citation has no source name', () => {
    const { dispatch, result } = setup();

    result.current.onCitations([{ id: 3, source: '', excerpt: 'some cited text' }]);

    const contextCall = dispatch.mock.calls.find(([channel]) => channel === 'context');
    expect(contextCall![1]).toMatchObject({ insightText: 'Cited from the open document' });
  });
});
