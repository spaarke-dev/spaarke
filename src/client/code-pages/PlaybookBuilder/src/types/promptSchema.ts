/**
 * Prompt Schema Types — Type definitions for JSON Prompt Schema (JPS).
 *
 * Mirrors the C# PromptSchema model records on the server side.
 * All property names use camelCase to match JSON serialization.
 *
 * Used by the PlaybookBuilder UI for:
 * - Node configuration forms (form-based and JSON editor authoring)
 * - Canvas-time validation
 * - Builder Agent tool integration
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** All valid output field types as a const tuple. */
export const OUTPUT_FIELD_TYPES = [
    "string",
    "number",
    "boolean",
    "array",
    "object",
] as const;

/** Union type derived from OUTPUT_FIELD_TYPES. */
export type OutputFieldType = (typeof OUTPUT_FIELD_TYPES)[number];

// ---------------------------------------------------------------------------
// Core Interfaces
// ---------------------------------------------------------------------------

/**
 * Root JSON Prompt Schema (JPS) interface.
 *
 * Represents the structured prompt format stored in `sprk_systemprompt`.
 * Detected by the renderer when the field content starts with `{` and
 * contains `"$schema"`.
 */
export interface PromptSchema {
    /** Schema identifier URI for JPS format detection. */
    $schema?: string;
    /** Schema version number. Defaults to 1. */
    $version?: number;
    /** Core AI instruction — role, task, constraints, context. */
    instruction: InstructionSection;
    /** Input configuration — document, prior outputs, parameters. */
    input?: InputSection;
    /** Output field definitions and structured output settings. */
    output?: OutputSection;
    /** Explicit scope references — skills and knowledge. */
    scopes?: ScopesSection;
    /** Few-shot learning examples. */
    examples?: ExampleEntry[];
    /** Provenance and classification metadata. */
    metadata?: MetadataSection;
}

// ---------------------------------------------------------------------------
// Instruction
// ---------------------------------------------------------------------------

/**
 * The core AI instruction section.
 *
 * `task` is the only required field in the entire schema — it defines
 * the specific work the AI must perform.
 */
export interface InstructionSection {
    /** System-level identity (e.g., "You are a contract analysis specialist"). */
    role?: string;
    /** The specific work to perform — the most important field. */
    task: string;
    /** Behavioral constraints rendered as a numbered list. */
    constraints?: string[];
    /** Additional context; supports Handlebars template variables. */
    context?: string;
}

// ---------------------------------------------------------------------------
// Input
// ---------------------------------------------------------------------------

/**
 * Input configuration — what the AI receives for processing.
 */
export interface InputSection {
    /** Document text configuration. */
    document?: DocumentInput;
    /** Upstream node output dependencies (declarative, for validation). */
    priorOutputs?: PriorOutputReference[];
    /** Additional parameters; supports Handlebars template variables. */
    parameters?: Record<string, unknown>;
}

/**
 * Document input configuration within the input section.
 */
export interface DocumentInput {
    /** Whether document text is required for this prompt. */
    required?: boolean;
    /** Maximum character length for the document text. */
    maxLength?: number;
    /** Placeholder text shown when no document is provided. */
    placeholder?: string;
}

/**
 * Declares a dependency on an upstream node's output.
 *
 * Used for validation and documentation — the actual data flows
 * through Handlebars templates (e.g., `{{output_classify.output.documentType}}`).
 */
export interface PriorOutputReference {
    /** Output variable name of the upstream node. */
    variable: string;
    /** Specific field names referenced from the upstream output. */
    fields: string[];
    /** Human-readable description of why this dependency exists. */
    description?: string;
}

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------

/**
 * Output configuration — defines what the AI must produce.
 */
export interface OutputSection {
    /** Output field definitions with types, constraints, and descriptions. */
    fields: OutputFieldDefinition[];
    /** Use Azure OpenAI JSON Schema constrained decoding. Defaults to false. */
    structuredOutput?: boolean;
}

/**
 * Definition for a single output field.
 *
 * Mirrors the server-side OutputFieldDefinition record. Field types
 * are constrained to the OUTPUT_FIELD_TYPES union.
 */
export interface OutputFieldDefinition {
    /** Field name in the JSON output. */
    name: string;
    /** Data type of the field value. */
    type: OutputFieldType;
    /** What this field represents (becomes part of the prompt). */
    description?: string;
    /** Fixed set of valid values (rendered inline in prompt). */
    enum?: string[];
    /**
     * Auto-inject values from downstream node.
     * Format: `"downstream:nodeVar.fieldName"`
     */
    $choices?: string;
    /** Array item schema (when type is "array"). */
    items?: Record<string, unknown>;
    /** Maximum string length. */
    maxLength?: number;
    /** Minimum numeric value. */
    minimum?: number;
    /** Maximum numeric value. */
    maximum?: number;
}

// ---------------------------------------------------------------------------
// Scopes
// ---------------------------------------------------------------------------

/**
 * Explicit scope references that supplement N:N scope relationships.
 */
export interface ScopesSection {
    /** Skill references — inline string or named reference object. */
    $skills?: Array<string | { $ref: string }>;
    /** Knowledge references — named reference or inline content. */
    $knowledge?: KnowledgeReference[];
}

/**
 * A knowledge source reference within the scopes section.
 *
 * Either a named reference (`$ref`) pointing to a Dataverse
 * `sprk_analysisknowledge` record, or inline content.
 */
export interface KnowledgeReference {
    /** Named reference to a knowledge record (e.g., "knowledge:standard-contract-clauses"). */
    $ref?: string;
    /** Contextual label controlling the section heading when rendered. */
    as?: string;
    /** Inline content used as-is, no resolution needed. */
    inline?: string;
}

// ---------------------------------------------------------------------------
// Examples
// ---------------------------------------------------------------------------

/**
 * A single few-shot learning example.
 *
 * Rendered as a "## Examples" section in the final prompt to teach
 * the AI the expected output structure.
 */
export interface ExampleEntry {
    /** Example input text. */
    input: string;
    /** Expected output matching the output.fields schema. */
    output: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// Metadata
// ---------------------------------------------------------------------------

/**
 * Provenance and classification metadata for the prompt schema.
 */
export interface MetadataSection {
    /** Who created this schema (username or "builder-agent"). */
    author?: string;
    /**
     * Authoring level:
     * - 0: Migration (auto-converted from flat text)
     * - 1: Form-based authoring
     * - 2: JSON editor authoring
     * - 3: AI Builder Agent authoring
     */
    authorLevel?: number;
    /** ISO 8601 timestamp of when the schema was created. */
    createdAt?: string;
    /** Human-readable description of this prompt's purpose. */
    description?: string;
    /** Classification tags for search and organization. */
    tags?: string[];
}

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

/**
 * Result of validating a PromptSchema object.
 */
export interface PromptSchemaValidationResult {
    /** Whether the schema passed all validation checks. */
    valid: boolean;
    /** List of validation error messages (empty when valid). */
    errors: string[];
}

/**
 * Validates that an unknown value conforms to the PromptSchema structure.
 *
 * Checks:
 * - Value is a non-null object
 * - `instruction` is present and is an object
 * - `instruction.task` is a non-empty string
 * - `output.fields` entries have valid types (if present)
 *
 * @param schema - The value to validate.
 * @returns Validation result with `valid` flag and `errors` array.
 */
export function validatePromptSchema(
    schema: unknown,
): PromptSchemaValidationResult {
    const errors: string[] = [];

    // Check schema is an object
    if (schema === null || typeof schema !== "object" || Array.isArray(schema)) {
        return { valid: false, errors: ["Schema must be a non-null object."] };
    }

    const obj = schema as Record<string, unknown>;

    // Check instruction is present and is an object
    if (
        obj.instruction === null ||
        obj.instruction === undefined ||
        typeof obj.instruction !== "object" ||
        Array.isArray(obj.instruction)
    ) {
        errors.push("'instruction' is required and must be an object.");
    } else {
        const instruction = obj.instruction as Record<string, unknown>;

        // Check instruction.task is a non-empty string
        if (typeof instruction.task !== "string" || instruction.task.trim() === "") {
            errors.push(
                "'instruction.task' is required and must be a non-empty string.",
            );
        }
    }

    // Check output.fields have valid types if present
    if (obj.output !== undefined && obj.output !== null) {
        if (typeof obj.output !== "object" || Array.isArray(obj.output)) {
            errors.push("'output' must be an object when present.");
        } else {
            const output = obj.output as Record<string, unknown>;

            if (output.fields !== undefined && output.fields !== null) {
                if (!Array.isArray(output.fields)) {
                    errors.push("'output.fields' must be an array when present.");
                } else {
                    const validTypes: readonly string[] = OUTPUT_FIELD_TYPES;

                    for (let i = 0; i < output.fields.length; i++) {
                        const field = output.fields[i] as Record<string, unknown>;

                        if (
                            field === null ||
                            typeof field !== "object" ||
                            Array.isArray(field)
                        ) {
                            errors.push(
                                `output.fields[${i}] must be an object.`,
                            );
                            continue;
                        }

                        if (
                            typeof field.name !== "string" ||
                            field.name.trim() === ""
                        ) {
                            errors.push(
                                `output.fields[${i}].name is required and must be a non-empty string.`,
                            );
                        }

                        if (
                            typeof field.type !== "string" ||
                            !validTypes.includes(field.type)
                        ) {
                            errors.push(
                                `output.fields[${i}].type must be one of: ${validTypes.join(", ")}. Got: "${String(field.type)}".`,
                            );
                        }
                    }
                }
            }
        }
    }

    return { valid: errors.length === 0, errors };
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Creates a minimal valid PromptSchema with an empty task.
 *
 * Useful as a starting point for new prompt configurations in the
 * form-based editor or when the Builder Agent initializes a new node.
 *
 * @returns A PromptSchema with required fields set to defaults.
 */
export function createDefaultPromptSchema(): PromptSchema {
    return {
        $schema: "https://spaarke.com/schemas/jps/v1",
        $version: 1,
        instruction: {
            task: "",
        },
    };
}
