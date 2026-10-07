/**
 * todoScoreMappings.test.ts
 *
 * Unit tests for the single-source-of-truth Priority/Effort choice→score
 * mapping (spec FR-02/FR-03, smart-todo-r5 task 011).
 *
 * Covers:
 *   - Each priority value -> exact score.
 *   - Each effort value -> exact score (Option B, quick-wins-first).
 *   - Null-default reproduction (Medium priority -> 50, None effort -> 50).
 *   - Negative: unrecognized/out-of-range values fall back, never throw.
 *   - Parity: CreateTodoWizard (same-package relative import) and the
 *     SmartTodo Code Page quick-add flow (`@spaarke/ui-components` barrel
 *     import) resolve through the SAME exported table — verified via import
 *     graph (source inspection), not value comparison alone, per the task's
 *     acceptance criteria.
 *   - The locked composite formula in `todoScoring.ts` is untouched by this
 *     task (byte-identical hash check against a known-good pre-task hash).
 *
 * @see ../todoScoreMappings.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import * as crypto from 'crypto';
import {
  PRIORITY_TO_SCORE,
  EFFORT_TO_SCORE,
  DEFAULT_PRIORITY_CHOICE,
  DEFAULT_EFFORT_CHOICE,
  NULL_DEFAULT_PRIORITY_SCORE,
  NULL_DEFAULT_EFFORT_SCORE,
  priorityChoiceToScore,
  effortChoiceToScore,
  scoreToPriorityChoice,
  scoreToEffortChoice,
  TODO_PRIORITY_CHOICES,
  TODO_EFFORT_CHOICES,
} from '../todoScoreMappings';

// ---------------------------------------------------------------------------
// FR-02: Priority -> score (exact)
// ---------------------------------------------------------------------------

describe('PRIORITY_TO_SCORE (FR-02)', () => {
  it.each([
    ['Urgent', 100],
    ['High', 75],
    ['Medium', 50],
    ['Low', 25],
  ] as const)('%s -> %d', (choice, expected) => {
    expect(PRIORITY_TO_SCORE[choice]).toBe(expected);
    expect(priorityChoiceToScore(choice)).toBe(expected);
  });

  it('exposes exactly the 4 spec-defined options (no additions)', () => {
    expect(Object.keys(PRIORITY_TO_SCORE).sort()).toEqual(['High', 'Low', 'Medium', 'Urgent'].sort());
    expect(TODO_PRIORITY_CHOICES).toEqual(['Urgent', 'High', 'Medium', 'Low']);
  });
});

// ---------------------------------------------------------------------------
// FR-03: Effort -> score (Option B, quick-wins-first — exact)
// ---------------------------------------------------------------------------

describe('EFFORT_TO_SCORE (FR-03, Option B quick-wins-first)', () => {
  it.each([
    ['Low', 25],
    ['Medium', 50],
    ['High', 75],
    ['Very High', 100],
    ['None', 50],
  ] as const)('%s -> %d', (choice, expected) => {
    expect(EFFORT_TO_SCORE[choice]).toBe(expected);
    expect(effortChoiceToScore(choice)).toBe(expected);
  });

  it('exposes exactly the 5 spec-defined options (no additions)', () => {
    expect(Object.keys(EFFORT_TO_SCORE).sort()).toEqual(['High', 'Low', 'Medium', 'None', 'Very High'].sort());
    expect(TODO_EFFORT_CHOICES).toEqual(['Low', 'Medium', 'High', 'Very High', 'None']);
  });
});

// ---------------------------------------------------------------------------
// Null-default reproduction (no regression vs. today's behavior)
// ---------------------------------------------------------------------------

describe('null-defaults', () => {
  it("DEFAULT_PRIORITY_CHOICE is Medium and resolves to 50 (today's default)", () => {
    expect(DEFAULT_PRIORITY_CHOICE).toBe('Medium');
    expect(NULL_DEFAULT_PRIORITY_SCORE).toBe(50);
    expect(priorityChoiceToScore(undefined)).toBe(50);
    expect(priorityChoiceToScore(null)).toBe(50);
  });

  it('DEFAULT_EFFORT_CHOICE is None and resolves to 50 (Option B null-default)', () => {
    expect(DEFAULT_EFFORT_CHOICE).toBe('None');
    expect(NULL_DEFAULT_EFFORT_SCORE).toBe(50);
    expect(effortChoiceToScore(undefined)).toBe(50);
    expect(effortChoiceToScore(null)).toBe(50);
  });
});

// ---------------------------------------------------------------------------
// Negative: unrecognized/out-of-range values fall back, never throw
// ---------------------------------------------------------------------------

describe('defensive fallback (negative cases)', () => {
  it('priorityChoiceToScore never throws on garbage input and falls back to 50', () => {
    expect(() => priorityChoiceToScore('not-a-real-choice')).not.toThrow();
    expect(priorityChoiceToScore('not-a-real-choice')).toBe(50);
    expect(priorityChoiceToScore('')).toBe(50);
    // @ts-expect-error — intentionally passing a non-string to prove the guard holds at runtime
    expect(priorityChoiceToScore(12345)).toBe(50);
  });

  it('effortChoiceToScore never throws on garbage input and falls back to 50', () => {
    expect(() => effortChoiceToScore('not-a-real-choice')).not.toThrow();
    expect(effortChoiceToScore('not-a-real-choice')).toBe(50);
    expect(effortChoiceToScore('')).toBe(50);
    // @ts-expect-error — intentionally passing a non-string to prove the guard holds at runtime
    expect(effortChoiceToScore(12345)).toBe(50);
  });

  it('reverse lookups fall back to the documented default choice on no exact match', () => {
    expect(scoreToPriorityChoice(999)).toBe(DEFAULT_PRIORITY_CHOICE);
    expect(scoreToPriorityChoice(undefined)).toBe(DEFAULT_PRIORITY_CHOICE);
    expect(scoreToEffortChoice(999)).toBe(DEFAULT_EFFORT_CHOICE);
    expect(scoreToEffortChoice(undefined)).toBe(DEFAULT_EFFORT_CHOICE);
  });
});

// ---------------------------------------------------------------------------
// Parity: wizard + quick-add resolve through the SAME exported table
// (verified by import graph / source inspection, not value comparison alone)
// ---------------------------------------------------------------------------

describe('cross-surface parity (import graph)', () => {
  const repoRoot = path.resolve(__dirname, '../../../../../../..');

  const createTodoStepSrc = fs.readFileSync(
    path.join(repoRoot, 'src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/CreateTodoStep.tsx'),
    'utf8'
  );

  const smartToDoSrc = fs.readFileSync(
    path.join(repoRoot, 'src/solutions/SmartTodo/src/components/SmartToDo.tsx'),
    'utf8'
  );

  it('CreateTodoStep.tsx (CreateTodoWizard) imports the canonical mapping module directly', () => {
    expect(createTodoStepSrc).toMatch(/from ['"]\.\.\/\.\.\/utils\/todoScoreMappings['"]/);
    // Sanity: it consumes the real exported names, not a re-declared local table.
    expect(createTodoStepSrc).toMatch(/priorityChoiceToScore/);
    expect(createTodoStepSrc).toMatch(/effortChoiceToScore/);
  });

  it('SmartToDo.tsx (quick-add) resolves the SAME module via the @spaarke/ui-components barrel', () => {
    expect(smartToDoSrc).toMatch(/from ["']@spaarke\/ui-components["']/);
    expect(smartToDoSrc).toMatch(/NULL_DEFAULT_PRIORITY_SCORE/);
    expect(smartToDoSrc).toMatch(/NULL_DEFAULT_EFFORT_SCORE/);
  });

  it('quick-add no longer hardcodes an undocumented literal score (the old sprk_effortscore: 10)', () => {
    expect(smartToDoSrc).not.toMatch(/sprk_effortscore:\s*10\b/);
  });

  it('the utils barrel re-exports todoScoreMappings (so the bare @spaarke/ui-components import resolves)', () => {
    const barrelSrc = fs.readFileSync(path.join(__dirname, '../index.ts'), 'utf8');
    expect(barrelSrc).toMatch(/from ['"]\.\/todoScoreMappings['"]/);
  });
});

// ---------------------------------------------------------------------------
// Locked formula guard: todoScoring.ts must remain byte-identical
// ---------------------------------------------------------------------------

describe('todoScoring.ts remains untouched (locked composite formula)', () => {
  it('matches the pre-task-011 sha256 hash', () => {
    const repoRoot = path.resolve(__dirname, '../../../../../../..');
    const lockedFilePath = path.join(
      repoRoot,
      'src/client/shared/Spaarke.SmartTodo.Components/src/utils/todoScoring.ts'
    );
    // Line endings normalised to LF (task 081 round 3): the pinned hash used to
    // be of the CRLF bytes a Windows `core.autocrlf=true` checkout produces, so
    // the guard failed on any LF checkout (e.g. Linux CI) with no code change.
    const contents = fs.readFileSync(lockedFilePath, 'utf8').replace(/\r\n/g, '\n');
    const hash = crypto.createHash('sha256').update(contents).digest('hex');
    // Captured via `sha256sum` immediately before task 011 made any edits.
    //
    // Re-pinned 2026-10-03 (spaarke-ontology-platform-r1 task 080 / C-10) — this guard's INTENT is
    // "the composite-score FORMULA/WEIGHTS below don't drift", not "this file's bytes never change
    // for any reason". Task 080 converged the three independently-drifted copies of `parseDueDate`
    // (a LIVE BUG: Kanban bucketing disagreed with the composite score by a day in negative-UTC-
    // offset zones) onto a single implementation in `@spaarke/ui-components`, so this file no longer
    // defines its own `parseDueDate` — it imports + re-exports the canonical one. The weights
    // (priority 0.50 / effort 0.20 inverted / urgency 0.30) and every scoring/label function below
    // are byte-for-byte unchanged; only the date-parsing primitive's SOURCE moved. New hash computed
    // via `sha256sum` immediately after that edit, reviewed in the same change.
    //
    // Re-pinned 2026-10-05 (spaarke-ontology-platform-r1 task 081 / C-17, same intent as above):
    // `computeDueLabel`'s TIER now comes from the shared `dueUrgencyForDays`
    // (`@spaarke/ui-components` `utils/dateLocal.ts`, the one 3/7/10 tier function every due-date
    // surface calls) over the shared `daysBetweenLocalMidnight`, and `DueUrgency` is re-exported
    // from there. The tier boundaries are identical (pinned by `todoScoring.dueTiers.test.ts` and
    // `dueUrgency.crossSurface.test.ts`); the composite-score weights and `computeTodoScore` /
    // `computeDueDateUrgencyRaw` are byte-for-byte unchanged. Hash of the LF-normalised file.
    expect(hash).toBe('47f94fe286179fe3945294555a927453e40332459f7d68448e4555bd646e4c00');
  });
});
