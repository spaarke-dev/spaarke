/**
 * R4 task 004 fix round 1 (F4), revised by tasks 009 and 011 - the Tasks & events section holds the record while a create
 * in its list is in flight.
 *
 * Task 011 (owner 2026-11-12): a Task is an event of type Task, so the section shows ONE list (`EventsCalendar`) in two
 * groups, with two create dialogs (Add task, Add event). The section hands the template's `onBusyChange` to that list
 * unchanged; the list reports "busy" while EITHER dialog is posting and never lets one going idle release the other's
 * hold. That combining rule is `createBusyReporters` (exported by `EventsCalendar`), and its overlap cases are tested
 * in `RecordTemplateAccess.test.tsx` ("createBusyReporters: the host is busy while EITHER dialog is posting"); one
 * dialog's hold and release through the real list is tested next to it. This file pins the seam: what the section hands
 * the list.
 *
 * What runs for real: `TasksEventsSection`. Substituted: `EventsCalendar`, by a stand-in that exposes the props the
 * section hands it.
 */
import * as React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';

const lists = vi.hoisted(() => ({
  events: undefined as undefined | ((busy: boolean) => void),
  props: undefined as undefined | Record<string, unknown>,
  mounted: 0,
}));

vi.mock('../src/components/EventsCalendar', () => ({
  EventsCalendar: (props: { onBusyChange?: (busy: boolean) => void } & Record<string, unknown>) => {
    lists.events = props.onBusyChange;
    lists.props = props;
    lists.mounted += 1;
    return <div data-testid="events-list-stand-in" />;
  },
}));

import { TasksEventsSection } from '../src/components/record/sections/TasksEventsSection';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';

function renderSection(
  onBusyChange: (busy: boolean) => void,
  offerAction: RecordSectionContext['offerAction'] = () => undefined
): ReturnType<typeof render> {
  const context = {
    detail: { entity: 'sprk_matter' },
    recordId: '00000000-0000-4000-8000-000000000001',
    caller: null,
    callerContext: null,
    refreshSignal: 0,
    onBusyChange,
    offerAction,
    headerActions: [],
  } as unknown as RecordSectionContext;
  return render(<TasksEventsSection {...context} />);
}

describe('Tasks & events: the list holds the record', () => {
  it("the list's create in flight holds the record until it settles", () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);
    expect(lists.events).toBeTypeOf('function');

    act(() => lists.events!(true));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);

    act(() => lists.events!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('renders one list, inside the Tasks & events card', () => {
    lists.mounted = 0;
    renderSection(vi.fn());
    const card = screen.getByTestId('record-section-tasksEvents');
    expect(card.querySelector('[data-testid="record-detail-events"]')).not.toBeNull();
    expect(screen.getAllByTestId('events-list-stand-in')).toHaveLength(1);
  });

  it('hands the list one reporter per create action: addEvent and addTask, each its own', () => {
    const reporters = new Map<string, () => void>();
    const offerAction: RecordSectionContext['offerAction'] = id => {
      const reporter = (): void => undefined;
      reporters.set(id, reporter);
      return reporter;
    };
    renderSection(vi.fn(), offerAction);

    expect([...reporters.keys()].sort()).toEqual(['addEvent', 'addTask']);
    expect(lists.props?.onCreateActionChange).toBe(reporters.get('addEvent'));
    expect(lists.props?.onCreateTaskActionChange).toBe(reporters.get('addTask'));
    expect(lists.props?.onCreateTaskActionChange).not.toBe(lists.props?.onCreateActionChange);
  });

  it('a record whose config lists no Add task gets no task reporter (the list then offers no task button of its own)', () => {
    renderSection(vi.fn(), id => (id === 'addEvent' ? () => undefined : undefined));

    expect(lists.props?.onCreateActionChange).toBeTypeOf('function');
    expect(lists.props?.onCreateTaskActionChange).toBeUndefined();
  });
});
