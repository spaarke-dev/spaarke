/**
 * R4 task 009 (owner 2026-11-12): external users work only with `sprk_event`. The SPA has no To Do (`sprk_todo`) list,
 * no create / update / delete call on a to-do route and no to-do wording. Task 011 added Tasks as an event type; task 012 (owner 2026-11-14: "we only want Events") folded them
 * into ONE Events list, so there is also no separate Tasks list, no "Add task" and no "Create Task".
 *
 * A source scan over `src/` (comments included: a to-do mention in a comment is how a to-do surface comes back), with
 * must-fire and must-not-fire controls for the matcher itself. Enforced here because no type can say "this entity is
 * not used": the BFF keeps its to-do routes for now (D-13 #2800), so a call to them would compile and work.
 */
import fs from 'fs';
import path from 'path';
import { describe, expect, it } from 'vitest';

const SRC = path.resolve(__dirname, '../src');

/**
 * What counts as a to-do surface: the entity, its routes, any `Todo` / `ToDo` identifier (SmartTodo, getRootTodos, ODataTodo,
 * addTodo, EventsTodosRoot, ...) and the user-facing words. Not the legacy event column `sprk_todoflag` (an event field)
 * and not an upper-case `TODO` work note.
 */
const TODO_SURFACE: readonly RegExp[] = [
  /\bsprk_todo(?!flag)/,
  /\/todos\b/,
  /Todo/,
  /ToDo/,
  /\btodos?\b/,
  /\bto-dos?\b/i,
  /\bTo Do\b/,
];

/** The patterns a line of source matches (empty = clean). */
function todoSurfaceIn(text: string): string[] {
  return TODO_SURFACE.filter(re => re.test(text)).map(re => re.source);
}

/**
 * Task 012: the separate-Tasks surface that must not come back. Tasks are events of type Task: one Events list with a Task
 * badge, one "Add event" with a Type choice. User-facing words only (not the `isTask` flag or the Task badge / radio
 * label), so a heading, an action or a dialog title named for a task fails the scan.
 */
const SEPARATE_TASKS_SURFACE: readonly RegExp[] = [
  /\bAdd task\b/i,
  /\bCreate (new )?task\b/i,
  /\bNo tasks\b/i,
  /\bTasks (&|&amp;|and) events\b/i,
  /events-group-tasks/,
];

/** A "Tasks" group heading: the word as a quoted label or an element's whole text, in the event list and the record template. */
const TASKS_HEADING: readonly RegExp[] = [/['"`]Tasks['"`]/, />\s*Tasks\s*</, /^\s*Tasks\s*$/];

function separateTasksIn(text: string, headingScope: boolean): string[] {
  const patterns = headingScope ? [...SEPARATE_TASKS_SURFACE, ...TASKS_HEADING] : SEPARATE_TASKS_SURFACE;
  return patterns.filter(re => re.test(text)).map(re => re.source);
}

/** The files whose "Tasks" is a list heading: the event list and everything of the record template. */
const isEventListFile = (rel: string): boolean =>
  rel === 'components/EventsCalendar.tsx' || rel.startsWith('components/record/');

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(full);
    return /\.(ts|tsx|css|json|html)$/.test(entry.name) ? [full] : [];
  });
}

describe('the to-do surface matcher (controls)', () => {
  it.each([
    ['a to-do read', 'const page = await getRootTodos(root, id);'],
    ['the entity', "entityName: 'sprk_todo',"],
    ['a to-do route', "await bffApiCall(`/api/v1/external/todos/${id}`, { method: 'DELETE' });"],
    ['the old list', '<SmartTodo projectId={id} />'],
    ['a create action id', "toolbar: ['refresh', 'addTodo', 'copyLink'],"],
    ['the old button text', 'Use "Add to-do" above to create the first task.'],
    ['the product words', 'To Do'],
    ['a camel-cased identifier', 'const items = await getProjectToDos(id);'],
    ['a lower-case mention', '// the to-dos of this record'],
  ])('fires on %s', (_name, line) => {
    expect(todoSurfaceIn(line)).not.toEqual([]);
  });

  it.each([
    ['the legacy event column', '$select: sprk_eventid,sprk_todoflag'],
    ['an upper-case work note', '// TODO(#123): tidy later'],
    ['plain English', 'There is nothing to do here.'],
    ['the section title', "tasksEvents: 'Tasks & events',"],
    ['a word that contains the letters', 'const todayDate = new Date();'],
  ])('does not fire on %s', (_name, line) => {
    expect(todoSurfaceIn(line)).toEqual([]);
  });
});

describe('src/ has no to-do surface (task 009)', () => {
  it('scans a real tree (the walk finds the SPA sources)', () => {
    const files = sourceFiles(SRC).map(f => path.relative(SRC, f).replace(/\\/g, '/'));
    expect(files).toContain('api/web-api-client.ts');
    expect(files).toContain('components/record/sections/TasksEventsSection.tsx');
  });

  it('no file in src/ names the to-do entity, its routes, a Todo identifier or the to-do wording', () => {
    const offenders: string[] = [];
    for (const file of sourceFiles(SRC)) {
      const rel = path.relative(SRC, file).replace(/\\/g, '/');
      fs.readFileSync(file, 'utf8')
        .split(/\r?\n/)
        .forEach((line, i) => {
          const hits = todoSurfaceIn(line);
          if (hits.length > 0) offenders.push(`${rel}:${i + 1} [${hits.join(', ')}] ${line.trim()}`);
        });
    }
    expect(offenders).toEqual([]);
  });

  it('the retired list component is gone', () => {
    expect(fs.existsSync(path.join(SRC, 'components/SmartTodo.tsx'))).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Task 012: Events only - no separate Tasks list, "Add task" or "Create Task"
// ---------------------------------------------------------------------------

describe('the separate-Tasks surface matcher (controls)', () => {
  it.each([
    ['the old header action', "addTask: { label: 'Add task', tooltip: 'Add task' },"],
    ['the old dialog title', "title: 'Create Task',"],
    ['the old dialog title, any case', 'submitLabel="Create task"'],
    ['the old empty state', '<Text>No tasks yet</Text>'],
    ['the old section title', "tasksEvents: 'Tasks & events',"],
    ['the old section title, escaped', '<h3>Tasks &amp; events</h3>'],
    ['the old group test id', 'data-testid={`events-group-tasks`}'],
  ])('fires on %s', (_name, line) => {
    expect(separateTasksIn(line, false)).not.toEqual([]);
  });

  it.each([
    ['a Tasks group heading as a label', "const label = isTaskGroup ? 'Tasks' : 'Events';"],
    ['a Tasks group heading as element text', '<Text size={400}>Tasks</Text>'],
    ['a Tasks heading on its own line', '            Tasks'],
  ])('fires on %s (in the event list and the record template)', (_name, line) => {
    expect(separateTasksIn(line, true)).not.toEqual([]);
  });

  it.each([
    ['the Task badge', '                Task'],
    ['the Task radio', '<Radio value="task" label="Task" />'],
    ['the isTask flag', 'const tasks = events.filter(e => e.isTask === true);'],
    ['the Events title', "tasksEvents: 'Events',"],
    ['the Add event action', "addEvent: { label: 'Add event', tooltip: 'Add event' },"],
    ['the one dialog title', "title: 'Create Event',"],
    ['the one empty state', '<Text>No events yet</Text>'],
    ['the eventType payload', "{ eventType: 'task' as const }"],
  ])('does not fire on %s', (_name, line) => {
    expect(separateTasksIn(line, true)).toEqual([]);
  });

  it('a "Tasks" word elsewhere is not a group heading outside the event list (the dashboard keeps its own Tasks button)', () => {
    expect(separateTasksIn('<Button aria-label="Tasks" />', false)).toEqual([]);
    expect(separateTasksIn('<Button aria-label="Tasks" />', true)).not.toEqual([]);
  });
});

describe('src/ has no separate Tasks surface (task 012)', () => {
  it('no file in src/ names "Add task", "Create Task", "No tasks", "Tasks & events" or the Tasks group test id; the event list and record template have no "Tasks" heading', () => {
    const offenders: string[] = [];
    for (const file of sourceFiles(SRC)) {
      const rel = path.relative(SRC, file).replace(/\\/g, '/');
      const headingScope = isEventListFile(rel);
      fs.readFileSync(file, 'utf8')
        .split(/\r?\n/)
        .forEach((line, i) => {
          const hits = separateTasksIn(line, headingScope);
          if (hits.length > 0) offenders.push(`${rel}:${i + 1} [${hits.join(', ')}] ${line.trim()}`);
        });
    }
    expect(offenders).toEqual([]);
  });

  it('the Add task action is gone from the record template: no addTask id in the action text, the type or any config', () => {
    for (const rel of [
      'components/record/recordTemplates.ts',
      'components/record/recordTemplate.types.ts',
      'components/record/recordAccess.ts',
      'components/record/sections/TasksEventsSection.tsx',
      'components/EventsCalendar.tsx',
    ]) {
      expect({ rel, mentionsAddTask: /addTask/.test(fs.readFileSync(path.join(SRC, rel), 'utf8')) }).toEqual({
        rel,
        mentionsAddTask: false,
      });
    }
  });
});
