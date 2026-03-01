/**
 * AiCompletionNode — Generates text using AI completion.
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
 * AI Completion node — generates text using AI completion.
 * Use for: drafting emails, creating summaries, generating content.
 */
export const AiCompletionNode = React.memo(function AiCompletionNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<BrainCircuit20Regular />}
            typeLabel="AI Completion"
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
