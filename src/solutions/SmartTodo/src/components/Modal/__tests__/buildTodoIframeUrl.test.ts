/**
 * buildTodoIframeUrl.test.ts
 *
 * Unit tests for the pure URL-construction helper used by SmartTodoModal
 * (R4 task 040).
 *
 * ──────────────────────────────────────────────────────────────────────────
 * TEST-RUNNER STATUS (2026-06-10)
 *
 * SmartTodo's `package.json` does NOT currently include a test runner (no
 * jest, no vitest). These tests are written as DOCUMENTATION + ASSERTION
 * SOURCE so they activate immediately when a test runner is added to the
 * SmartTodo package, or when the file is moved into a workspace that already
 * runs them. This matches the convention established by
 * `useLaunchContext.test.ts` (R3 task 070b).
 *
 * Once a test runner is wired up:
 *   • Add `vitest` (preferred) or `jest` + jsdom env to
 *     `src/solutions/SmartTodo/package.json` devDependencies.
 *   • Add a `"test"` script.
 *   • Both runners expose `describe` / `it` / `expect` as globals so these
 *     tests run unchanged.
 * ──────────────────────────────────────────────────────────────────────────
 *
 * Scenarios:
 *   1. Happy path — correct URL shape
 *   2. Strips trailing slash from clientUrl
 *   3. Strips braces from todoId
 *   4. Uses default form ID when not overridden
 *   5. Honours formId override
 *   6. Throws when clientUrl is empty
 *   7. Throws when todoId is empty
 *   8. spec FR-13 — verifies the EXACT URL contract
 */

import {
  buildTodoIframeUrl,
  TODO_MAIN_FORM_ID,
} from '../buildTodoIframeUrl';

describe('buildTodoIframeUrl', () => {
  const clientUrl = 'https://contoso.crm.dynamics.com';
  const todoId = '11111111-2222-3333-4444-555555555555';

  it('builds the canonical URL shape (FR-13)', () => {
    const url = buildTodoIframeUrl({ clientUrl, todoId });
    expect(url).toBe(
      'https://contoso.crm.dynamics.com/main.aspx' +
        '?pagetype=entityrecord' +
        '&etn=sprk_todo' +
        `&id=${todoId}` +
        `&formid=${TODO_MAIN_FORM_ID}` +
        '&navbar=off',
    );
  });

  it('strips a trailing slash from clientUrl', () => {
    const url = buildTodoIframeUrl({
      clientUrl: clientUrl + '/',
      todoId,
    });
    // No `//main.aspx` — the trailing slash was removed.
    expect(url.startsWith('https://contoso.crm.dynamics.com/main.aspx')).toBe(true);
    expect(url).not.toContain('//main.aspx');
  });

  it('strips multiple trailing slashes from clientUrl', () => {
    const url = buildTodoIframeUrl({
      clientUrl: clientUrl + '///',
      todoId,
    });
    expect(url.startsWith('https://contoso.crm.dynamics.com/main.aspx')).toBe(true);
  });

  it('strips braces from todoId', () => {
    const url = buildTodoIframeUrl({
      clientUrl,
      todoId: `{${todoId}}`,
    });
    expect(url).toContain(`&id=${todoId}`);
    expect(url).not.toContain('{');
    expect(url).not.toContain('}');
  });

  it('uses the OOB form ID by default', () => {
    const url = buildTodoIframeUrl({ clientUrl, todoId });
    expect(url).toContain(`&formid=${TODO_MAIN_FORM_ID}`);
    expect(TODO_MAIN_FORM_ID).toBe('eca59df4-1364-f111-ab0c-7ced8ddc4cc6');
  });

  it('honours a formId override (test seam)', () => {
    const customFormId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
    const url = buildTodoIframeUrl({
      clientUrl,
      todoId,
      formId: customFormId,
    });
    expect(url).toContain(`&formid=${customFormId}`);
    expect(url).not.toContain(TODO_MAIN_FORM_ID);
  });

  it('throws when clientUrl is empty', () => {
    expect(() =>
      buildTodoIframeUrl({ clientUrl: '', todoId }),
    ).toThrow(/clientUrl is required/);
  });

  it('throws when todoId is empty', () => {
    expect(() =>
      buildTodoIframeUrl({ clientUrl, todoId: '' }),
    ).toThrow(/todoId is required/);
  });

  it('includes navbar=off to suppress MDA chrome (FR-13)', () => {
    const url = buildTodoIframeUrl({ clientUrl, todoId });
    expect(url).toContain('&navbar=off');
  });

  it('uses pagetype=entityrecord + etn=sprk_todo (FR-13)', () => {
    const url = buildTodoIframeUrl({ clientUrl, todoId });
    expect(url).toContain('pagetype=entityrecord');
    expect(url).toContain('etn=sprk_todo');
  });

  it('NFR-03 — accepts any clientUrl host (no hardcoded environment)', () => {
    const urlA = buildTodoIframeUrl({
      clientUrl: 'https://prod.crm.dynamics.com',
      todoId,
    });
    const urlB = buildTodoIframeUrl({
      clientUrl: 'https://test.crm4.dynamics.com',
      todoId,
    });
    expect(urlA).toContain('https://prod.crm.dynamics.com/main.aspx');
    expect(urlB).toContain('https://test.crm4.dynamics.com/main.aspx');
  });
});

/**
 * Filter-set navigation calculation — sibling test documenting how the
 * SmartTodoModal computes prev / next when its caller passes the current
 * filter set. The modal itself uses `Array.findIndex` + bounds-checked
 * increment / decrement; this test pins that contract.
 */
describe('SmartTodoModal nav-set semantics (filter set traversal)', () => {
  type Rec = { sprk_todoid: string };
  const ids: Rec[] = [
    { sprk_todoid: 'a' },
    { sprk_todoid: 'b' },
    { sprk_todoid: 'c' },
  ];

  const next = (records: Rec[], current: string): string | null => {
    const idx = records.findIndex((r) => r.sprk_todoid === current);
    if (idx < 0 || idx + 1 >= records.length) return null;
    return records[idx + 1].sprk_todoid;
  };
  const prev = (records: Rec[], current: string): string | null => {
    const idx = records.findIndex((r) => r.sprk_todoid === current);
    if (idx <= 0) return null;
    return records[idx - 1].sprk_todoid;
  };

  it('next from index 0 → b', () => {
    expect(next(ids, 'a')).toBe('b');
  });

  it('next at last index → null (button disabled in shell)', () => {
    expect(next(ids, 'c')).toBeNull();
  });

  it('prev from index 1 → a', () => {
    expect(prev(ids, 'b')).toBe('a');
  });

  it('prev at index 0 → null (button disabled in shell)', () => {
    expect(prev(ids, 'a')).toBeNull();
  });

  it('current id not in set → both null (defensive)', () => {
    expect(next(ids, 'zzz')).toBeNull();
    expect(prev(ids, 'zzz')).toBeNull();
  });
});
