/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * The shared To Do score (spaarke-ontology-platform-r1 task 067 / D-29, FR-63): tier points, weights,
 * calendar-day counting, and a forcing function that no second copy of the urgency / composite math exists.
 */
import * as fs from 'fs';
import * as path from 'path';
import { computeTodoScoreBreakdown, todoUrgencyRaw } from '../dateLocal';

const NOW = new Date(2026, 9, 9, 8, 0); // Fri 9 Oct 2026 08:00 local

describe('todoUrgencyRaw', () => {
  it.each([
    [-1, 100],
    [0, 80],
    [3, 80],
    [4, 50],
    [7, 50],
    [8, 25],
    [10, 25],
    [11, 0],
  ])('%i calendar days out -> %i points, whatever the hour', (days, points) => {
    for (const hour of [0, 1, 12, 23]) {
      expect(todoUrgencyRaw(new Date(2026, 9, 9 + days, hour, 30), NOW)).toBe(points);
    }
  });

  it('no due date -> 0', () => {
    expect(todoUrgencyRaw(null, NOW)).toBe(0);
    expect(todoUrgencyRaw(undefined, NOW)).toBe(0);
  });

  it('an invalid date -> 0 (not overdue)', () => {
    expect(todoUrgencyRaw(new Date('nope'), NOW)).toBe(0);
  });
});

describe('computeTodoScoreBreakdown', () => {
  it('weights priority 0.50, inverted effort 0.20, urgency 0.30; clamps and rounds', () => {
    const r = computeTodoScoreBreakdown({ sprk_priorityscore: 75, sprk_effortscore: 25, sprk_duedate: '2026-10-08' }, NOW);
    expect(r.priorityComponent).toBeCloseTo(37.5);
    expect(r.effortComponent).toBeCloseTo(15);
    expect(r.urgencyComponent).toBeCloseTo(30);
    expect(r.todoScore).toBe(83); // 82.5 rounds up
    expect(computeTodoScoreBreakdown({ sprk_priorityscore: 100, sprk_effortscore: 0, sprk_duedate: '2026-10-01' }, NOW).todoScore).toBe(100);
  });

  it('defaults priority and effort to 50 and tolerates null', () => {
    expect(computeTodoScoreBreakdown({}, NOW).todoScore).toBe(35); // 25 + 10, no due date
    expect(computeTodoScoreBreakdown({ sprk_priorityscore: null, sprk_effortscore: null, sprk_duedate: null }, NOW).todoScore).toBe(35);
  });
});

describe('no second copy of the urgency / composite math (FR-63)', () => {
  const repoRoot = path.resolve(__dirname, '../../../../../../..');
  const roots = [
    'src/client/shared/Spaarke.SmartTodo.Components/src',
    'src/client/shared/Spaarke.UI.Components/src/components/TodoDetail',
    'src/solutions/LegalWorkspace/src',
    'src/solutions/SmartTodo/src',
  ];

  function sources(dir: string): string[] {
    const out: string[] = [];
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (e.name === 'node_modules' || e.name === '__tests__') continue;
      const full = path.join(dir, e.name);
      if (e.isDirectory()) out.push(...sources(full));
      else if (/\.tsx?$/.test(e.name)) out.push(full);
    }
    return out;
  }

  it('only dateLocal.ts holds the urgency points and the composite weights', () => {
    const offenders: string[] = [];
    for (const root of roots) {
      const abs = path.join(repoRoot, root);
      if (!fs.existsSync(abs)) continue;
      for (const file of sources(abs)) {
        const text = fs.readFileSync(file, 'utf8');
        // The old copies: private weights, a private urgency function, or elapsed-ms day counting for the score.
        if (/\bW_URGENCY\b|\bcomputeDueDateUrgencyRaw\b|\bW_PRIORITY\b/.test(text)) offenders.push(file);
        if (/Math\.ceil\([^)]*1000 \* 60 \* 60 \* 24\)/.test(text)) offenders.push(file);
      }
    }
    expect(offenders).toEqual([]);
  });
});
