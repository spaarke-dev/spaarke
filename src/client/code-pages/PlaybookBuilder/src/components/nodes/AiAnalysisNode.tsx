/**
 * AiAnalysisNode — Processes data with AI and produces structured output.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { BrainCircuit20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * AI Analysis node — processes data with AI and produces structured output.
 * Use for: document analysis, entity extraction, classification.
 */
export const AiAnalysisNode = React.memo(function AiAnalysisNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<BrainCircuit20Regular />}
            typeLabel="AI Analysis"
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
