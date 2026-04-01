/**
 * useInlineTodoCreate — hook for creating To Do items (sprk_event records)
 * directly from notification items in the Daily Briefing.
 *
 * Unlike FeedTodoSyncContext (which toggles existing sprk_event.sprk_todoflag),
 * this hook CREATES new sprk_event records from notification data. It tracks
 * per-notification creation state so the UI can show pending/created/error states.
 *
 * Usage:
 *   const { createTodo, isCreated, isPending, getError } = useInlineTodoCreate(webApi);
 *
 *   <Button onClick={() => createTodo(notificationItem)} disabled={isPending(item.id)}>
 *     {isCreated(item.id) ? "Created" : "Add To Do"}
 *   </Button>
 */

import { useState, useRef, useCallback } from "react";
import type { IWebApi, NotificationItem, NotificationPriority } from "../types/notifications";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type TodoCreateStatus = "pending" | "created" | "error";

export interface UseInlineTodoCreateResult {
  /** Create a new To Do (sprk_event) from a notification item. */
  createTodo: (item: NotificationItem) => Promise<void>;
  /** Returns true if a To Do was successfully created for this notification. */
  isCreated: (itemId: string) => boolean;
  /** Returns true if a To Do creation is in-flight for this notification. */
  isPending: (itemId: string) => boolean;
  /** Returns the error message for a failed creation, or undefined. */
  getError: (itemId: string) => string | undefined;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Map notification priority string to Dataverse sprk_priority option set value.
 * Values: 100000000=Low, 100000001=Normal, 100000002=High, 100000003=Urgent
 * (per CreateTodo/formTypes.ts) */
function mapPriorityToOptionSet(priority: NotificationPriority): number {
  switch (priority) {
    case "urgent":
      return 100000003;
    case "high":
      return 100000002;
    case "normal":
      return 100000001;
    case "low":
      return 100000000;
    default:
      return 100000001; // default to normal
  }
}

/** Map entity logical name to Dataverse entity set name (plural form for OData). */
function getEntitySetName(logicalName: string): string {
  const map: Record<string, string> = {
    sprk_matter: "sprk_matters",
    sprk_project: "sprk_projects",
    sprk_document: "sprk_documents",
    sprk_event: "sprk_events",
    sprk_invoice: "sprk_invoices",
    sprk_contact: "sprk_contacts",
  };
  return map[logicalName] ?? `${logicalName}s`;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Hook for inline To Do creation from notification items.
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider). Pass null if not yet available.
 * @returns Object with createTodo, isCreated, isPending, getError functions.
 */
export function useInlineTodoCreate(webApi: IWebApi | null): UseInlineTodoCreateResult {
  const [statusMap, setStatusMap] = useState<Map<string, TodoCreateStatus>>(
    () => new Map()
  );
  const errorsRef = useRef<Map<string, string>>(new Map());

  const createTodo = useCallback(
    async (item: NotificationItem): Promise<void> => {
      if (!webApi) {
        console.error("[useInlineTodoCreate] webApi not available");
        return;
      }

      // Optimistic: set to pending
      setStatusMap((prev) => {
        const next = new Map(prev);
        next.set(item.id, "pending");
        return next;
      });

      try {
        // Build the sprk_event record
        const record: Record<string, unknown> = {
          sprk_eventname: item.title,
          sprk_todoflag: true,
          sprk_todostatus: 0, // Open
          sprk_todosource: 0, // User
          sprk_priority: mapPriorityToOptionSet(item.priority),
        };

        if (item.body) {
          record["sprk_description"] = item.body;
        }

        // Add regarding lookup if available
        if (item.regardingEntityType && item.regardingId) {
          const entitySetName = getEntitySetName(item.regardingEntityType);
          record["sprk_Regarding@odata.bind"] =
            `/${entitySetName}(${item.regardingId})`;
        }

        await webApi.createRecord("sprk_event", record);

        // Success
        setStatusMap((prev) => {
          const next = new Map(prev);
          next.set(item.id, "created");
          return next;
        });
      } catch (e: unknown) {
        // Failure
        let message = "Failed to create To Do. Please try again.";
        if (e && typeof e === "object") {
          const err = e as Record<string, unknown>;
          if (typeof err["message"] === "string") {
            message = err["message"];
          }
        } else if (typeof e === "string") {
          message = e;
        }

        errorsRef.current.set(item.id, message);
        console.error("[useInlineTodoCreate] Failed to create To Do:", message, e);

        setStatusMap((prev) => {
          const next = new Map(prev);
          next.set(item.id, "error");
          return next;
        });
      }
    },
    [webApi]
  );

  const isCreated = useCallback(
    (itemId: string): boolean => statusMap.get(itemId) === "created",
    [statusMap]
  );

  const isPending = useCallback(
    (itemId: string): boolean => statusMap.get(itemId) === "pending",
    [statusMap]
  );

  const getError = useCallback(
    (itemId: string): string | undefined => errorsRef.current.get(itemId),
    // statusMap is included so the callback identity changes when errors are recorded
    // (error status is set in statusMap at the same time as errorsRef is updated)
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [statusMap]
  );

  return { createTodo, isCreated, isPending, getError };
}
