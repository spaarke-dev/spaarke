/**
 * SendEmailNode — Sends an email via Dataverse.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from "react";
import type { Node, NodeProps } from "@xyflow/react";
import { tokens, Text } from "@fluentui/react-components";
import { Mail20Regular } from "@fluentui/react-icons";
import { BaseNode } from "./BaseNode";
import type { PlaybookNodeData } from "../../types/canvas";

/**
 * Send Email node — sends an email via Dataverse.
 * Use for: notifications, reports, automated communications.
 */
export const SendEmailNode = React.memo(function SendEmailNode({
    data,
    selected,
}: NodeProps<Node<PlaybookNodeData>>) {
    return (
        <BaseNode
            data={data}
            selected={selected}
            icon={<Mail20Regular />}
            typeLabel="Send Email"
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
