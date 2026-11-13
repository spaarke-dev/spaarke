/**
 * R4 task 004 fix round 1 (F4), revised by task 009 - the Tasks & events section holds the record while its list is busy.
 *
 * Task 009 (owner 2026-11-12): the section lists events only, so it has one list and hands the template's `onBusyChange`
 * to it unchanged: a create in flight holds paging, Close and Refresh until it settles. Task 011 adds the Task-typed
 * event list here and must hold per list again (one list going idle must not release the other's in-flight hold).
 *
 * What runs for real: `TasksEventsSection`. Substituted: `EventsCalendar`, by a stand-in that exposes the props the
 * section hands it (the list's own reporting is covered in `RecordTemplateChildDialogs`).
 */
import * as React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';

const lists = vi.hoisted(() => ({
  events: undefined as undefined | ((busy: boolean) => void),
  mounted: 0,
}));

vi.mock('../src/components/EventsCalendar', () => ({
  EventsCalendar: (props: { onBusyChange?: (busy: boolean) => void }) => {
    lists.events = props.onBusyChange;
    lists.mounted += 1;
    return <div data-testid="events-list-stand-in" />;
  },
}));

import { TasksEventsSection } from '../src/components/record/sections/TasksEventsSection';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';

function renderSection(onBusyChange: (busy: boolean) => void): ReturnType<typeof render> {
  const context = {
    detail: { entity: 'sprk_matter' },
    recordId: '00000000-0000-4000-8000-000000000001',
    caller: null,
    callerContext: null,
    refreshSignal: 0,
    onBusyChange,
    offerAction: () => undefined,
    headerActions: [],
  } as unknown as RecordSectionContext;
  return render(<TasksEventsSection {...context} />);
}

describe('Tasks & events: the events list holds the record', () => {
  it("the events list's create in flight holds the record until it settles", () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);
    expect(lists.events).toBeTypeOf('function');

    act(() => lists.events!(true));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);

    act(() => lists.events!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('renders the events list only, inside the Tasks & events card', () => {
    lists.mounted = 0;
    renderSection(vi.fn());
    const card = screen.getByTestId('record-section-tasksEvents');
    expect(card.querySelector('[data-testid="record-detail-events"]')).not.toBeNull();
    expect(screen.getAllByTestId('events-list-stand-in')).toHaveLength(1);
  });
});
