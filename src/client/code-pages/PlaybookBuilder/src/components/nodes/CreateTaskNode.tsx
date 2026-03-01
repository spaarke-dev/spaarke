/**
 * CreateTaskNode — Creates a Dataverse task record.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { TaskListSquareLtr20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Create Task node — creates a Dataverse task record.
 * Use for: follow-up actions, workflow assignments.
 */
export const CreateTaskNode = React.memo(function CreateTaskNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<TaskListSquareLtr20Regular />}
            typeLabel="Create Task"
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
