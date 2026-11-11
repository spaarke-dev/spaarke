/**
 * Tasks & events — the record's events and to-dos (R3 task 057 `EventsAndTasksSection`, moved from R3's record-detail modal;
 * task 005).
 *
 * The existing `EventsCalendar` / `SmartTodo` on the record's own routes (GET /api/v1/external/{projects|matters|
 * workassignments}/{id}/events and /todos; the server applies the record's rights and the every-root rule, the client
 * filters nothing). Read-only, as in R3: `AccessLevel.ViewOnly` hides create and toggle, and an event or to-do the caller
 * created shows Delete (task 054). Task 006 replaces the two ViewOnly props with the per-record right (FR-06). A 403 shows
 * the shared denied state and a failed read a retryable error, inside the section only. Refresh (or Try again) remounts
 * both lists, which re-read.
 *
 * ADD EVENT / ADD TO-DO (task 004, FR-03). Each list offers its toolbar action through `offerAction` (shown under the
 * list's own create rule, which it then does not draw a button for) and renders its create dialog (a `FormModal`) inside
 * this section, so the dialog is a descendant of the record modal; a create in flight holds the modal. While the two
 * ViewOnly props below stand, neither list's rule is met inside the template, so neither action is shown yet: task 006
 * changes the rule source, not this wiring.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { AccessLevel } from '../../../types';
import type { EventsTodosRoot } from '../../../api/web-api-client';
import { EventsCalendar } from '../../EventsCalendar';
import { SmartTodo } from '../../SmartTodo';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
});

/** The entities whose record shows events and tasks, and the `{root}` of their task 039 / 007 routes. */
const EVENTS_TODOS_ROOTS: Readonly<Record<string, EventsTodosRoot>> = {
  sprk_project: 'projects',
  sprk_matter: 'matters',
  sprk_workassignment: 'workassignments',
};

export const TasksEventsSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  caller,
  refreshSignal,
  onBusyChange,
  offerAction,
}) => {
  const styles = useStyles();
  // Both lists report one busy flag to the template, which keeps a single hold per section. Each list is its own holder
  // here: one list going idle (its create dialog unmounting, say) must not release the other's hold on an in-flight post.
  const holds = React.useRef({ events: false, todos: false });
  const reportEvents = React.useCallback(
    (busy: boolean) => {
      holds.current.events = busy;
      onBusyChange(holds.current.events || holds.current.todos);
    },
    [onBusyChange]
  );
  const reportTodos = React.useCallback(
    (busy: boolean) => {
      holds.current.todos = busy;
      onBusyChange(holds.current.events || holds.current.todos);
    },
    [onBusyChange]
  );
  // An entity with no events/to-do routes renders nothing (no config offers one; this keeps a wrong config inert).
  const root = Object.prototype.hasOwnProperty.call(EVENTS_TODOS_ROOTS, detail.entity)
    ? EVENTS_TODOS_ROOTS[detail.entity]
    : undefined;
  if (!root) return null;
  return (
    <RecordSectionCard id="tasksEvents">
      <div className={styles.body} data-testid="record-detail-events" key={refreshSignal}>
        <EventsCalendar
          projectId={recordId}
          root={root}
          accessLevel={AccessLevel.ViewOnly}
          caller={caller}
          onCreateActionChange={offerAction('addEvent')}
          onBusyChange={reportEvents}
        />
        <SmartTodo
          projectId={recordId}
          root={root}
          accessLevel={AccessLevel.ViewOnly}
          caller={caller}
          onCreateActionChange={offerAction('addTodo')}
          onBusyChange={reportTodos}
        />
      </div>
    </RecordSectionCard>
  );
};
