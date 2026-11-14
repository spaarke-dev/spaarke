/**
 * R4 task 009 (owner 2026-11-12): external users work only with `sprk_event`. The SPA has no To Do (`sprk_todo`) list,
 * no create / update / delete call on a to-do route and no to-do wording; task 011 adds Tasks as an event type.
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
