/**
 * useWorkspaceLayouts — fetches workspace layouts from the BFF API and manages
 * the active layout state.
 *
 * On mount, fetches the user's default layout and full layout list in parallel.
 * Falls back to SYSTEM_DEFAULT_LAYOUT_JSON if the API is unavailable or returns
 * no layouts — ensuring the workspace always renders.
 *
 * Standards: ADR-012 (shared components), ADR-008 (endpoint filters)
 */

import { useState, useEffect, useCallback, useRef } from "react";
import { authenticatedFetch } from "../services/bffAuthProvider";
import { getBffBaseUrl } from "../config/runtimeConfig";
import type { LayoutJson } from "../workspace/buildDynamicWorkspaceConfig";
import { SYSTEM_DEFAULT_LAYOUT_JSON } from "../workspace/buildDynamicWorkspaceConfig";

// ---------------------------------------------------------------------------
// Types (mirror BFF WorkspaceLayoutDto shape)
// ---------------------------------------------------------------------------

/** Client-side representation of a workspace layout from the BFF. */
export interface WorkspaceLayoutDto {
  id: string;
  name: string;
  layoutTemplateId: string;
  sectionsJson: string;
  isDefault: boolean;
  sortOrder: number | null;
  isSystem: boolean;
}

export interface UseWorkspaceLayoutsResult {
  /** All available layouts (system + user). Empty array while loading. */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. Null only during initial load. */
  activeLayout: WorkspaceLayoutDto | null;
  /** Parsed LayoutJson from the active layout's sectionsJson. Falls back to system default. */
  activeLayoutJson: LayoutJson;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
  /** Error message if layout fetch failed (workspace still renders with fallback). */
  error: string | null;
  /** Switch to a different layout by ID. Fetches layout details if needed. */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF. */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// System default layout stub (used when API is unavailable)
// ---------------------------------------------------------------------------

const SYSTEM_DEFAULT_LAYOUT: WorkspaceLayoutDto = {
  id: "00000000-0000-0000-0000-000000000001",
  name: "Corporate Workspace",
  layoutTemplateId: "3-row-mixed",
  sectionsJson: JSON.stringify(SYSTEM_DEFAULT_LAYOUT_JSON),
  isDefault: true,
  sortOrder: 0,
  isSystem: true,
};

// ---------------------------------------------------------------------------
// JSON parse helper
// ---------------------------------------------------------------------------

function parseLayoutJson(sectionsJson: string): LayoutJson {
  try {
    const parsed = JSON.parse(sectionsJson) as LayoutJson;
    if (parsed && typeof parsed.schemaVersion === "number" && Array.isArray(parsed.rows)) {
      return parsed;
    }
  } catch (err) {
    console.warn("[useWorkspaceLayouts] Failed to parse sectionsJson:", err);
  }
  return SYSTEM_DEFAULT_LAYOUT_JSON;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useWorkspaceLayouts(): UseWorkspaceLayoutsResult {
  const [layouts, setLayouts] = useState<WorkspaceLayoutDto[]>([]);
  const [activeLayout, setActiveLayout] = useState<WorkspaceLayoutDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState(0);

  // Track mount state to avoid state updates after unmount
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  // -------------------------------------------------------------------------
  // Fetch layouts on mount (and on refetch)
  // -------------------------------------------------------------------------

  useEffect(() => {
    let cancelled = false;

    async function fetchLayouts() {
      setIsLoading(true);
      setError(null);

      let bffBaseUrl: string;
      try {
        bffBaseUrl = getBffBaseUrl();
      } catch {
        // Runtime config not initialized — use fallback
        console.warn("[useWorkspaceLayouts] BFF base URL not available, using fallback layout");
        if (!cancelled && mountedRef.current) {
          setLayouts([SYSTEM_DEFAULT_LAYOUT]);
          setActiveLayout(SYSTEM_DEFAULT_LAYOUT);
          setIsLoading(false);
        }
        return;
      }

      try {
        // Fetch default layout and all layouts in parallel
        const [defaultRes, listRes] = await Promise.all([
          authenticatedFetch(`${bffBaseUrl.replace(/\/$/, '')}/api/workspace/layouts/default`),
          authenticatedFetch(`${bffBaseUrl.replace(/\/$/, '')}/api/workspace/layouts`),
        ]);

        if (cancelled || !mountedRef.current) return;

        // Parse layouts list
        let allLayouts: WorkspaceLayoutDto[] = [];
        if (listRes.ok) {
          allLayouts = await listRes.json();
        } else {
          console.warn(
            `[useWorkspaceLayouts] Failed to fetch layouts list: ${listRes.status}`,
          );
        }

        // Parse default layout
        let defaultLayout: WorkspaceLayoutDto | null = null;
        if (defaultRes.ok) {
          defaultLayout = await defaultRes.json();
        } else {
          console.warn(
            `[useWorkspaceLayouts] Failed to fetch default layout: ${defaultRes.status}`,
          );
        }

        if (cancelled || !mountedRef.current) return;

        // Apply results with fallback
        if (allLayouts.length > 0) {
          setLayouts(allLayouts);
        } else {
          setLayouts([SYSTEM_DEFAULT_LAYOUT]);
        }

        if (defaultLayout) {
          setActiveLayout(defaultLayout);
        } else if (allLayouts.length > 0) {
          // Pick the first default layout, or the first system layout
          const fallback =
            allLayouts.find((l) => l.isDefault) ??
            allLayouts.find((l) => l.isSystem) ??
            allLayouts[0];
          setActiveLayout(fallback);
        } else {
          setActiveLayout(SYSTEM_DEFAULT_LAYOUT);
        }

        setIsLoading(false);
      } catch (err) {
        if (cancelled || !mountedRef.current) return;

        const message = err instanceof Error ? err.message : "Unknown error";
        console.warn("[useWorkspaceLayouts] Layout fetch failed, using fallback:", message);

        setError(message);
        setLayouts([SYSTEM_DEFAULT_LAYOUT]);
        setActiveLayout(SYSTEM_DEFAULT_LAYOUT);
        setIsLoading(false);
      }
    }

    fetchLayouts();

    return () => { cancelled = true; };
  }, [fetchKey]);

  // -------------------------------------------------------------------------
  // Switch active layout
  // -------------------------------------------------------------------------

  const setActiveLayoutById = useCallback(
    (layoutId: string) => {
      // Look up in the current layouts list first
      const found = layouts.find((l) => l.id === layoutId);
      if (found) {
        setActiveLayout(found);
        return;
      }

      // If not found locally, fetch by ID from the BFF
      (async () => {
        try {
          const bffBaseUrl = getBffBaseUrl();
          const res = await authenticatedFetch(
            `${bffBaseUrl.replace(/\/$/, '')}/api/workspace/layouts/${layoutId}`,
          );
          if (res.ok) {
            const layout: WorkspaceLayoutDto = await res.json();
            if (mountedRef.current) {
              setActiveLayout(layout);
            }
          } else {
            console.warn(
              `[useWorkspaceLayouts] Failed to fetch layout ${layoutId}: ${res.status}`,
            );
          }
        } catch (err) {
          console.warn("[useWorkspaceLayouts] Failed to fetch layout by ID:", err);
        }
      })();
    },
    [layouts],
  );

  // -------------------------------------------------------------------------
  // Refetch
  // -------------------------------------------------------------------------

  const refetch = useCallback(() => {
    setFetchKey((k) => k + 1);
  }, []);

  // -------------------------------------------------------------------------
  // Derived: parse active layout JSON
  // -------------------------------------------------------------------------

  const activeLayoutJson: LayoutJson = activeLayout
    ? parseLayoutJson(activeLayout.sectionsJson)
    : SYSTEM_DEFAULT_LAYOUT_JSON;

  return {
    layouts,
    activeLayout,
    activeLayoutJson,
    isLoading,
    error,
    setActiveLayoutById,
    refetch,
  };
}
