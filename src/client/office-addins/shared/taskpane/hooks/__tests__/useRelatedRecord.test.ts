/**
 * Unit tests for useRelatedRecord (spaarkeai-word-add-in-r1 task 026, FR-09).
 *
 * A pure derivation over the already-resolved `DocumentIdentityState` — no network call, so these tests
 * assert on inputs/outputs only (renderHook, no fetch mocking needed).
 */

import { renderHook } from '@testing-library/react';
import { useRelatedRecord } from '../useRelatedRecord';
import type { DocumentIdentityState } from '../../services/documentIdentityService';

describe('useRelatedRecord', () => {
  it('is absent when documentIdentity is undefined', () => {
    const { result } = renderHook(() => useRelatedRecord(undefined));
    expect(result.current).toEqual({ kind: 'absent' });
  });

  it('is absent while checking', () => {
    const { result } = renderHook(() => useRelatedRecord('checking'));
    expect(result.current).toEqual({ kind: 'absent' });
  });

  it.each<DocumentIdentityState>([
    { kind: 'new', reason: 'not_spaarke_document' },
    { kind: 'conflict' },
    { kind: 'indeterminate', reason: 'system_failure' },
    { kind: 'denied' },
    { kind: 'error', message: 'boom' },
  ])('is absent for a non-resolved outcome %j', identity => {
    const { result } = renderHook(() => useRelatedRecord(identity));
    expect(result.current).toEqual({ kind: 'absent' });
  });

  it('is unassociated when resolved with no related record', () => {
    const { result } = renderHook(() =>
      useRelatedRecord({
        kind: 'resolved',
        documentId: 'a',
        documentName: 'doc',
        fileName: 'doc.docx',
        relatedRecord: null,
      })
    );
    expect(result.current).toEqual({ kind: 'unassociated' });
  });

  it('maps a resolved matter to a friendly-labeled associated view, defaulting missing fields to null', () => {
    const { result } = renderHook(() =>
      useRelatedRecord({
        kind: 'resolved',
        documentId: 'a',
        documentName: 'doc',
        fileName: 'doc.docx',
        relatedRecord: {
          entityType: 'sprk_matter',
          id: 'ABCDEF12-3456-7890-ABCD-EF1234567890',
          name: 'PAT-191111',
          // displayName / number omitted — pre-task-026 shape, or a server that has not deployed yet
        },
      })
    );
    expect(result.current).toEqual({
      kind: 'associated',
      type: 'Matter',
      entityType: 'sprk_matter',
      id: 'ABCDEF12-3456-7890-ABCD-EF1234567890',
      displayName: null,
      number: null,
    });
  });

  it('maps every direct-slot logical name to its friendly label', () => {
    const cases: Array<[string, string]> = [
      ['sprk_matter', 'Matter'],
      ['sprk_project', 'Project'],
      ['sprk_invoice', 'Invoice'],
      ['sprk_workassignment', 'Work Assignment'],
    ];
    for (const [entityType, label] of cases) {
      const { result } = renderHook(() =>
        useRelatedRecord({
          kind: 'resolved',
          documentId: 'a',
          documentName: 'doc',
          fileName: 'doc.docx',
          relatedRecord: { entityType, id: 'x', name: null, displayName: 'X', number: null },
        })
      );
      expect(result.current).toMatchObject({ kind: 'associated', type: label });
    }
  });
});
