/**
 * DeliverToIndexNode — Queues document for RAG semantic indexing.
 *
 * Terminal node (no output handle).
 *
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { DatabaseSearch20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Deliver to Index node — enqueues RAG indexing job for semantic search.
 * Terminal node (no source handle / output connection).
 */
export const DeliverToIndexNode = React.memo(function DeliverToIndexNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<DatabaseSearch20Regular />}
            typeLabel="Deliver to Index"
            sourceHandleCount={0}
        >
            {data.indexName && (
                <Text
                    size={100}
                    style={{ color: tokens.colorNeutralForeground3 }}
                >
                    Index: {data.indexName as string}
                </Text>
            )}
        </BaseNode>
    );
});
