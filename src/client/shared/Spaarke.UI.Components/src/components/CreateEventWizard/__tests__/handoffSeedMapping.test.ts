/**
 * handoffSeedMapping.test.ts — CreateEventWizard hand-off pre-seed mapper
 * (spaarkeai-assistant-enhancements-r1 task 013 part 2).
 */

import { mapEventHandoffSeed } from '../handoffSeedMapping';
import type { HandoffSeed } from '../../../services/surfaceHandoff';

function seed(partial: Partial<HandoffSeed>): HandoffSeed {
  return {
    draftValues: partial.draftValues ?? {},
    resolvedLookups: partial.resolvedLookups ?? {},
    fileIds: partial.fileIds ?? [],
  };
}

describe('mapEventHandoffSeed', () => {
  it('returns undefined for a null / empty seed', () => {
    expect(mapEventHandoffSeed(null)).toBeUndefined();
    expect(mapEventHandoffSeed(seed({}))).toBeUndefined();
  });

  it('maps drafted event name + description onto eventName / description', () => {
    expect(
      mapEventHandoffSeed(seed({ draftValues: { event_name: 'Kickoff', task_description: 'Prep docs.' } }))
    ).toEqual({ eventName: 'Kickoff', description: 'Prep docs.' });
    expect(mapEventHandoffSeed(seed({ draftValues: { eventName: 'A', description: 'B' } }))).toEqual({
      eventName: 'A',
      description: 'B',
    });
  });

  it('maps the create-task preset event-type GUID + companion name (D-013-03)', () => {
    // The registry preset injects the raw subtype GUID plus a companion display name so the
    // event-type lookup renders "Task" instead of a blank label.
    const result = mapEventHandoffSeed(
      seed({
        draftValues: {
          event_name: 'Named',
          sprk_eventtype_ref: '124f5fc9-98ff-f011-8406-7c1e525abd8b',
          sprk_eventtype_ref_name: 'Task',
        },
      })
    );
    expect(result).toEqual({
      eventName: 'Named',
      eventTypeId: '124f5fc9-98ff-f011-8406-7c1e525abd8b',
      eventTypeName: 'Task',
    });
  });

  it('still skips a bare event-type GUID with NO companion name (never a blank label)', () => {
    const result = mapEventHandoffSeed(
      seed({ draftValues: { event_name: 'Named', sprk_eventtype_ref: '124f5fc9-98ff-f011-8406-7c1e525abd8b' } })
    );
    expect(result).toEqual({ eventName: 'Named' });
  });

  it('pre-selects a HIGH-confidence resolved event-type', () => {
    const result = mapEventHandoffSeed(
      seed({
        draftValues: { event_name: 'Named' },
        resolvedLookups: {
          eventType: { confidence: 'high', recordId: 'et-1', candidates: [{ recordId: 'et-1', label: 'Deadline' }] },
        },
      })
    );
    expect(result).toMatchObject({ eventName: 'Named', eventTypeId: 'et-1', eventTypeName: 'Deadline' });
  });
});
