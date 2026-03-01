/**
 * DeliverOutputNode — Outputs data to Power Apps or other consumers.
 *
 * Terminal node (no output handle).
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { DocumentArrowRight20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Deliver Output node — outputs data to Power Apps or other consumers.
 * Terminal node (no source handle / output connection).
 */
export const DeliverOutputNode = React.memo(function DeliverOutputNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<DocumentArrowRight20Regular />}
            typeLabel="Deliver Output"
            sourceHandleCount={0}
        >
            {data.outputVariable && (
                <Text
                    size={100}
                    style={{ color: tokens.colorNeutralForeground3 }}
                >
                    Variable: {data.outputVariable}
                </Text>
            )}
        </BaseNode>
    );
});
