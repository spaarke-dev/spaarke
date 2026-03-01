/**
 * Edge Components — Barrel Export + edgeTypes Registry
 *
 * Exports all custom edge components and the edgeTypes map for @xyflow/react v12.
 * The edgeTypes map must be defined outside the component to avoid re-renders.
 */

import type { EdgeTypes } from "@xyflow/react";
import { TrueBranchEdge, FalseBranchEdge } from "./ConditionEdge";

// Re-export edge components
export { TrueBranchEdge, FalseBranchEdge } from "./ConditionEdge";

/**
 * Edge type registry for @xyflow/react v12.
 * Maps edge type strings to their React components.
 *
 * IMPORTANT: Defined at module scope (outside components) to prevent
 * unnecessary re-renders per React Flow documentation.
 */
export const edgeTypes: EdgeTypes = {
    trueBranch: TrueBranchEdge,
    falseBranch: FalseBranchEdge,
};

/**
 * Edge type constants for type-safe usage.
 */
export const EDGE_TYPES = {
    TRUE_BRANCH: "trueBranch",
    FALSE_BRANCH: "falseBranch",
    DEFAULT: "smoothstep",
} as const;
