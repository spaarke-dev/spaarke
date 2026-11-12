/**
 * R4 task 004, fix round 1 (F4) - the Tasks & events section holds the record per list, not per section.
 *
 * `EventsCalendar` and `SmartTodo` each report a create in flight through `onBusyChange`, and the template keeps ONE busy
 * flag per section. If both lists passed it straight through, one list going idle (its create dialog unmounting, a list
 * remounting) would clear the flag while the other list's post was still in flight, releasing paging, Close and Refresh
 * early. The section therefore reports "busy" while EITHER list is busy.
 *
 * What runs for real: `TasksEventsSection`. Substituted: the two lists, by stand-ins that expose the `onBusyChange` prop the
 * section hands them (the lists' own reporting is covered in `RecordTemplateChildDialogs`).
 */
import * as React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { act, render } from '@testing-library/react';

const lists = vi.hoisted(() => ({
  events: undefined as undefined | ((busy: boolean) => void),
  todos: undefined as undefined | ((busy: boolean) => void),
}));

vi.mock('../src/components/EventsCalendar', () => ({
  EventsCalendar: (props: { onBusyChange?: (busy: boolean) => void }) => {
    lists.events = props.onBusyChange;
    return null;
  },
}));
vi.mock('../src/components/SmartTodo', () => ({
  SmartTodo: (props: { onBusyChange?: (busy: boolean) => void }) => {
    lists.todos = props.onBusyChange;
    return null;
  },
}));

import { TasksEventsSection } from '../src/components/record/sections/TasksEventsSection';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';

function renderSection(onBusyChange: (busy: boolean) => void): ReturnType<typeof render> {
  const context = {
    detail: { entity: 'sprk_matter' },
    recordId: '00000000-0000-4000-8000-000000000001',
    caller: null,
    refreshSignal: 0,
    onBusyChange,
    offerAction: () => undefined,
  } as unknown as RecordSectionContext;
  return render(<TasksEventsSection {...context} />);
}

describe('Tasks & events: one hold per list', () => {
  it("one list going idle does not release the other list's in-flight hold", () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);
    expect(lists.events).toBeTypeOf('function');
    expect(lists.todos).toBeTypeOf('function');

    act(() => lists.todos!(true));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);

    // The events list reports idle (its create dialog unmounting): the to-do post is still in flight.
    act(() => lists.events!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);
    expect(onBusyChange).not.toHaveBeenCalledWith(false);

    act(() => lists.todos!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('is symmetric: the to-do list going idle does not release an events post', () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);

    act(() => lists.events!(true));
    act(() => lists.todos!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);
    expect(onBusyChange).not.toHaveBeenCalledWith(false);

    act(() => lists.events!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('both lists busy: the hold lasts until the last one settles', () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);

    act(() => lists.events!(true));
    act(() => lists.todos!(true));
    act(() => lists.events!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(true);
    act(() => lists.todos!(false));
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('control: with no list busy the section reports idle, never busy', () => {
    const onBusyChange = vi.fn();
    renderSection(onBusyChange);
    act(() => lists.events!(false));
    act(() => lists.todos!(false));
    expect(onBusyChange).not.toHaveBeenCalledWith(true);
  });
});
