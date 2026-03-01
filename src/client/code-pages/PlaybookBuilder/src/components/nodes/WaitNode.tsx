/**
 * WaitNode — Pauses execution for human approval or time delay.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { Clock20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Wait node — pauses execution for human approval or time delay.
 * Use for: approval gates, human review steps, timed waits.
 */
export const WaitNode = React.memo(function WaitNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<Clock20Regular />}
            typeLabel="Wait"
        >
            {data.outputVariable && (
                <Text
                    size={100}
                    style={{ color: tokens.colorNeutralForeground3 }}
                >
                    Output: {data.outputVariable}
                </Text>
            )}
        </BaseNode>
    );
});
