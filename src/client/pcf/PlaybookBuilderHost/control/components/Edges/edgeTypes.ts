/**
 * Edge Type Registry for React Flow
 *
 * Maps edge type strings to their React components.
 * Used for custom edge rendering (condition branches, etc.).
 */

import type { EdgeTypes } from 'react-flow-renderer';
import { TrueBranchEdge, FalseBranchEdge } from './ConditionEdge';

/**
 * Edge type registry for React Flow.
 * Maps edge type strings to their React components.
 */
export const edgeTypes: EdgeTypes = {
  trueBranch: TrueBranchEdge,
  falseBranch: FalseBranchEdge,
};

/**
 * Edge type constants for type-safe usage.
 */
export const EDGE_TYPES = {
  TRUE_BRANCH: 'trueBranch',
  FALSE_BRANCH: 'falseBranch',
  DEFAULT: 'smoothstep',
} as const;
