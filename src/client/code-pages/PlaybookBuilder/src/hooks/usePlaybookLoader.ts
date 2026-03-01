/**
 * usePlaybookLoader — Loads playbook canvas data from Dataverse on mount
 *
 * Retrieves the sprk_canvaslayoutjson field from sprk_analysisplaybooks,
 * parses the JSON, and initializes the canvasStore with nodes and edges.
 */

import { useEffect, useState, useCallback } from "react";
import { retrieveRecord } from "../services/dataverseClient";
import { useCanvasStore } from "../stores/canvasStore";

const LOG_PREFIX = "[PlaybookBuilder:usePlaybookLoader]";

/** Entity set name for playbooks in Dataverse Web API */
const PLAYBOOK_ENTITY_SET = "sprk_analysisplaybooks";

/** OData $select for the canvas layout field */
const CANVAS_SELECT = "$select=sprk_canvaslayoutjson,sprk_name,sprk_analysisplaybookid";

export interface UsePlaybookLoaderResult {
    /** True while the initial load is in progress */
    isLoading: boolean;
    /** Error message if the load failed */
    error: string | null;
    /** Playbook display name from Dataverse */
    playbookName: string | null;
    /** Reload the playbook data from Dataverse */
    reload: () => Promise<void>;
}

/**
 * Hook that loads a playbook's canvas JSON from Dataverse and hydrates the canvas store.
 *
 * @param playbookId - The GUID of the sprk_analysisplaybook record (may include braces).
 */
export function usePlaybookLoader(playbookId: string): UsePlaybookLoaderResult {
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [playbookName, setPlaybookName] = useState<string | null>(null);

    const loadFromCanvasJson = useCanvasStore((s) => s.loadFromCanvasJson);
    const reset = useCanvasStore((s) => s.reset);

    const load = useCallback(async () => {
        if (!playbookId) {
            setIsLoading(false);
            setError("No playbook ID provided");
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            const record = await retrieveRecord(
                PLAYBOOK_ENTITY_SET,
                playbookId,
                CANVAS_SELECT
            );

            const name = (record.sprk_name as string) ?? null;
            setPlaybookName(name);

            const canvasJson = record.sprk_canvaslayoutjson as string | null | undefined;

            if (canvasJson && canvasJson.trim().length > 0) {
                loadFromCanvasJson(canvasJson);
                console.info(`${LOG_PREFIX} Loaded playbook "${name}" canvas data`);
            } else {
                // No saved canvas yet — reset to empty state
                reset();
                console.info(`${LOG_PREFIX} Playbook "${name}" has no saved canvas; starting fresh`);
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            console.error(`${LOG_PREFIX} Failed to load playbook:`, message);
            setError(message);
            reset();
        } finally {
            setIsLoading(false);
        }
    }, [playbookId, loadFromCanvasJson, reset]);

    useEffect(() => {
        void load();
    }, [load]);

    return { isLoading, error, playbookName, reload: load };
}
