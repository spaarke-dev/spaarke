/**
 * Shared form types for Playbook Builder node configuration forms.
 *
 * All node configuration forms share the same contract:
 * - Parse configJson on mount
 * - Serialize and emit updated configJson on any field change
 *
 * Template variable syntax: {{nodeName.output.fieldName}}
 */

/**
 * Standard props for all node configuration forms.
 * Each form receives a nodeId, a JSON configuration string,
 * and a callback to emit updated configuration.
 */
export interface NodeFormProps {
    /** Unique identifier of the node being configured. */
    nodeId: string;
    /** JSON string containing the node's current configuration. */
    configJson: string;
    /** Callback invoked with updated JSON string on any field change. */
    onConfigChange: (json: string) => void;
}

/**
 * Node reference used by VariableReferencePanel to enumerate upstream outputs.
 */
export interface NodeReference {
    id: string;
    data: {
        label: string;
        type: string;
        outputVariable?: string;
    };
}

/**
 * Single variable entry for the VariableReferencePanel.
 */
export interface VariableEntry {
    /** Full template expression, e.g. {{nodeName.output.fieldName}} */
    expression: string;
    /** Human-readable display label */
    label: string;
    /** Type hint for the variable value */
    typeHint: "string" | "number" | "boolean" | "object" | "array";
    /** Source node label */
    sourceNodeLabel: string;
}

/**
 * Validation result for a node's configuration.
 */
export interface NodeValidationResult {
    /** Error messages for required fields that are missing or invalid. */
    errors: string[];
    /** Warning messages for optional fields that are missing. */
    warnings: string[];
}
