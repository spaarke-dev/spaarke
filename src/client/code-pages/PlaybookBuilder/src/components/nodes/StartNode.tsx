/**
 * StartNode — Entry point node for a playbook workflow.
 *
 * New in R5 (not present in R4 PCF).
 * Uses @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { Play20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Start node — the entry point of a playbook workflow.
 * Has no target handle (nothing connects into it) and one source handle.
 */
export const StartNode = React.memo(function StartNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<Play20Regular />}
            typeLabel="Start"
            targetHandleCount={0}
        >
            <Text
                size={100}
                style={{ color: tokens.colorNeutralForeground3 }}
            >
                Workflow entry point
            </Text>
        </BaseNode>
    );
});
