/**
 * Condition Edge - Custom edge for condition node branches.
 *
 * Shows different colors for true (green) and false (red) branches.
 * Uses smooth step path for clean routing.
 *
 * Note: Uses EdgeText from react-flow-renderer v10 for labels.
 */

import * as React from 'react';
import {
  getSmoothStepPath,
  EdgeText,
} from 'react-flow-renderer';
import type { EdgeProps } from 'react-flow-renderer';

// Green color for true branch
const TRUE_BRANCH_COLOR = '#107C10';
// Red color for false branch
const FALSE_BRANCH_COLOR = '#D13438';

/**
 * Custom edge for true branch connections (green).
 */
export const TrueBranchEdge: React.FC<EdgeProps> = ({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
}) => {
  const [edgePath, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  });

  // Calculate center point for label
  const centerX = Number(labelX) || (sourceX + targetX) / 2;
  const centerY = Number(labelY) || (sourceY + targetY) / 2;

  return (
    <>
      <path
        id={id}
        style={{
          ...style,
          stroke: TRUE_BRANCH_COLOR,
          strokeWidth: 2,
        }}
        className="react-flow__edge-path"
        d={edgePath}
        markerEnd={markerEnd}
      />
      <EdgeText
        x={centerX}
        y={centerY}
        label="True"
        labelStyle={{
          fill: TRUE_BRANCH_COLOR,
          fontWeight: 600,
          fontSize: 10,
        }}
        labelBgStyle={{
          fill: '#ffffff',
          fillOpacity: 0.9,
        }}
        labelBgPadding={[4, 6] as [number, number]}
        labelBgBorderRadius={4}
      />
    </>
  );
};

/**
 * Custom edge for false branch connections (red).
 */
export const FalseBranchEdge: React.FC<EdgeProps> = ({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
}) => {
  const [edgePath, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  });

  // Calculate center point for label
  const centerX = Number(labelX) || (sourceX + targetX) / 2;
  const centerY = Number(labelY) || (sourceY + targetY) / 2;

  return (
    <>
      <path
        id={id}
        style={{
          ...style,
          stroke: FALSE_BRANCH_COLOR,
          strokeWidth: 2,
        }}
        className="react-flow__edge-path"
        d={edgePath}
        markerEnd={markerEnd}
      />
      <EdgeText
        x={centerX}
        y={centerY}
        label="False"
        labelStyle={{
          fill: FALSE_BRANCH_COLOR,
          fontWeight: 600,
          fontSize: 10,
        }}
        labelBgStyle={{
          fill: '#ffffff',
          fillOpacity: 0.9,
        }}
        labelBgPadding={[4, 6] as [number, number]}
        labelBgBorderRadius={4}
      />
    </>
  );
};
