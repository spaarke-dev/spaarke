/**
 * Test-local mock for `@spaarke/ui-components/services`.
 *
 * R2 task 019 / NFR-05:
 *   `useInlineTodoCreate` imports several ADR-024 polymorphic-resolver symbols
 *   (`TODO_REGARDING_CATALOG`, `applyResolverFields`, `INavPropEntry`,
 *   `IPolymorphicWebApi`) from `@spaarke/ui-components/services`. The smoke
 *   test does NOT exercise the inline-todo path; this stub keeps every imported
 *   symbol resolvable so the hook compiles in test context. Permissive types
 *   are intentional — the smoke test never invokes these.
 */

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface INavPropEntry {
  [key: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface IPolymorphicWebApi {
  [key: string]: any;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const TODO_REGARDING_CATALOG: any[] = [];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function applyResolverFields(..._args: any[]): any {
  return {};
}

// Pattern D (#2803 / #2804): useInlineTodoCreate calls the shared nav-prop discovery instead of a private copy. The
// stub catalog is empty, so the hook never reaches it here; `useInlineTodoCreate.sharedNavProps.test.ts` overrides this
// module with the real discovery to prove the hook uses the shared cache.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export async function discoverNavProps(..._args: any[]): Promise<any[]> {
  return [];
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function _resetNavPropCacheForTests(..._args: any[]): void {}

// ---------------------------------------------------------------------------
// r5 email-share (2026-07-09): DailyBriefingApp imports extractEmailKey from
// `@spaarke/ui-components/services`. Mirror the real implementation so tests
// that exercise the briefing-share recipient resolution behave faithfully.
// ---------------------------------------------------------------------------
export function extractEmailKey(name: string): string | null {
  const match = name.match(/\(([^)]+)\)\s*$/);
  if (!match) return null;
  const candidate = match[1].trim().toLowerCase();
  return candidate.includes('@') ? candidate : null;
}

// UAC-r2 task 147 r1: useInlineTodoCreate's DEFAULT creator posts the To Do to the BFF through this function.
export const createChildRecordViaBff: jest.Mock = jest.fn(() => Promise.resolve('bff-todo-1'));
