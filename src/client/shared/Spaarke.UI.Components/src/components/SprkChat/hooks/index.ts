/**
 * SprkChat Hooks - Barrel exports
 *
 * Re-exports all hooks from the SprkChat hooks folder for convenient
 * single-import access.
 *
 * @see ADR-012 - Shared Component Library
 */

export { useSseStream, parseSseEvent } from "./useSseStream";

export { useChatSession } from "./useChatSession";

export { useChatPlaybooks } from "./useChatPlaybooks";
export type { IUseChatPlaybooksResult } from "./useChatPlaybooks";

export { useActionMenuData } from "./useActionMenuData";
export type { UseActionMenuDataOptions, IUseActionMenuDataResult } from "./useActionMenuData";

export { useActionHandlers } from "./useActionHandlers";
export type {
    UseActionHandlersOptions,
    IUseActionHandlersResult,
    ActionHandlerContext,
    ActionHandler,
    ActionHandlerMap,
    WriteMode,
} from "./useActionHandlers";

export { useSelectionListener } from "./useSelectionListener";
export type {
    UseSelectionListenerOptions,
    IUseSelectionListenerResult,
} from "./useSelectionListener";
