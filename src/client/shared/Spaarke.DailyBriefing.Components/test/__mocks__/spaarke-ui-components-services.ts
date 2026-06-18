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
export interface INavPropEntry { [key: string]: any }
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface IPolymorphicWebApi { [key: string]: any }

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const TODO_REGARDING_CATALOG: any[] = [];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function applyResolverFields(..._args: any[]): any {
  return {};
}
