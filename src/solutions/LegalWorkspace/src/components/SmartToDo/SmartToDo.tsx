/**
 * SmartToDo — LegalWorkspace thin shim over the hoisted rich Kanban board.
 *
 * R5 FR-01 / task 003 (thin-shim conversion; closes the 13-file hoist task 002
 * started). ALL LW-specific coupling (Dataverse data hooks, mutation
 * callbacks, FeedTodoSyncContext, Xrm.Navigation) is brokered by
 * `useSmartToDoBridge`; the Kanban board rendering itself is 100% owned by
 * the host-agnostic `@spaarke/smart-todo-components` `SmartToDo` component —
 * ZERO duplicated component implementation remains in this file.
 *
 * See `projects/smart-todo-r5/notes/task-002-hoist.md` "Contract handed to
 * task 003" for the full injected-props contract this shim fulfils.
 *
 * Standards: ADR-012 (shared component peer package), ADR-021 (Fluent v9).
 */

import * as React from "react";
import { SmartToDo as PackageSmartToDo } from "@spaarke/smart-todo-components";
import { useSmartToDoBridge } from "../../hooks/useSmartToDoBridge";
import type { ITodo } from "../../types/entities";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Props — preserves the pre-hoist public surface so existing call sites
// (`workspaceConfig.tsx`, `App.tsx`) require no changes.
// ---------------------------------------------------------------------------

export interface ISmartToDoProps {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Optional mock items for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockItems?: ITodo[];
  /**
   * When true, hides the card wrapper (border, fixed height) and header
   * so the component can be embedded inside a tabbed container.
   */
  embedded?: boolean;
  /** Report the active item count to the parent (for tab badge display). */
  onCountChange?: (count: number) => void;
  /** Expose the refetch function to the parent (for refresh button in tab header). */
  onRefetchReady?: (refetch: () => void) => void;
  /** Called when "Show more" is clicked. */
  onShowMore?: () => void;
  /** When true, disables card click behavior (used for workspace glance mode). */
  disableSidePane?: boolean;
  /** Record scope. */
  scope?: "my" | "all";
  /** Business unit ID. */
  businessUnitId?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartToDo: React.FC<ISmartToDoProps> = ({
  webApi,
  userId,
  mockItems,
  embedded = false,
  onCountChange,
  onRefetchReady,
  onShowMore,
  disableSidePane = false,
  scope,
  businessUnitId,
}) => {
  const bridge = useSmartToDoBridge({ webApi, userId, mockItems, scope, businessUnitId });

  return (
    <PackageSmartToDo
      {...bridge}
      embedded={embedded}
      onCountChange={onCountChange}
      onRefetchReady={onRefetchReady}
      onShowMore={onShowMore}
      disableSidePane={disableSidePane}
    />
  );
};

SmartToDo.displayName = "SmartToDo";

export default SmartToDo;
