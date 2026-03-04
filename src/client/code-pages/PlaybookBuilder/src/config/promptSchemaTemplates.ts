/**
 * Prompt Schema Template Library — pre-built JPS schema templates for common
 * playbook patterns.
 *
 * Each template is a complete, valid PromptSchema that can be applied to a
 * playbook node as a starting point. The PlaybookBuilder UI surfaces these
 * in a template picker when configuring analysis nodes.
 */

import type { PromptSchema } from "../types/promptSchema";

// ---------------------------------------------------------------------------
// Template Wrapper Interface
// ---------------------------------------------------------------------------

/**
 * A named, tagged schema template that wraps a complete PromptSchema.
 *
 * Templates are displayed in the PlaybookBuilder template picker and can be
 * applied to any analysis node to pre-populate its prompt configuration.
 */
export interface PromptSchemaTemplate {
    /** Unique identifier for the template (kebab-case). */
    id: string;
    /** Human-readable display name. */
    name: string;
    /** Short description of what the template does. */
    description: string;
    /** Classification tags for filtering and search. */
    tags: string[];
    /** The complete PromptSchema to apply. */
    schema: PromptSchema;
}

// ---------------------------------------------------------------------------
// Template Definitions
// ---------------------------------------------------------------------------

/**
 * Pre-built JPS schema templates for common document analysis patterns.
 *
 * All templates share:
 * - `$schema: "https://spaarke.com/schemas/prompt/v1"`
 * - `$version: 1`
 * - `metadata.author: "template"` with `authorLevel: 0`
 * - `output.structuredOutput: true`
 */
export const PROMPT_SCHEMA_TEMPLATES: PromptSchemaTemplate[] = [
    // -----------------------------------------------------------------
    // 1. Document Classification
    // -----------------------------------------------------------------
    {
        id: "document-classification",
        name: "Document Classification",
        description:
            "Classify document type with confidence score. Returns a single classification label and numeric confidence.",
        tags: ["classification", "document", "quick"],
        schema: {
            $schema: "https://spaarke.com/schemas/prompt/v1",
            $version: 1,
            instruction: {
                role: "Document classification specialist",
                task: "Classify the document type and provide a confidence score",
                constraints: [
                    "Only use information present in the document",
                    "Return exactly one classification",
                ],
            },
            input: {
                document: {
                    required: true,
                    placeholder: "{{document.extractedText}}",
                },
            },
            output: {
                fields: [
                    {
                        name: "documentType",
                        type: "string",
                        description: "The classified document type",
                        enum: [
                            "contract",
                            "invoice",
                            "letter",
                            "memo",
                            "report",
                            "other",
                        ],
                    },
                    {
                        name: "confidence",
                        type: "number",
                        description:
                            "Classification confidence score between 0 and 1",
                        minimum: 0,
                        maximum: 1,
                    },
                    {
                        name: "reasoning",
                        type: "string",
                        description:
                            "Brief explanation of why this classification was chosen",
                    },
                ],
                structuredOutput: true,
            },
            examples: [
                {
                    input: "This Master Services Agreement is entered into between Acme Corp and Beta LLC...",
                    output: {
                        documentType: "contract",
                        confidence: 0.95,
                        reasoning:
                            "The document contains agreement language, defined parties, and contractual terms typical of a services agreement.",
                    },
                },
            ],
            metadata: {
                author: "template",
                authorLevel: 0,
                description:
                    "Classify document type with confidence score",
                tags: ["classification", "document"],
            },
        },
    },

    // -----------------------------------------------------------------
    // 2. Entity Extraction
    // -----------------------------------------------------------------
    {
        id: "entity-extraction",
        name: "Entity Extraction",
        description:
            "Extract entities, dates, and parties from documents. Returns structured arrays of parties, key dates, and a summary.",
        tags: ["extraction", "entities", "dates", "parties"],
        schema: {
            $schema: "https://spaarke.com/schemas/prompt/v1",
            $version: 1,
            instruction: {
                role: "Document analysis assistant",
                task: "Extract all key entities, dates, and named parties from the document",
                constraints: [
                    "Only extract information explicitly stated",
                    "Use ISO 8601 for dates",
                    "Return null for fields that cannot be determined",
                ],
            },
            input: {
                document: {
                    required: true,
                    placeholder: "{{document.extractedText}}",
                },
            },
            output: {
                fields: [
                    {
                        name: "parties",
                        type: "array",
                        items: { type: "string" },
                        description:
                            "Named parties, organizations, or individuals mentioned in the document",
                    },
                    {
                        name: "keyDates",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                date: {
                                    type: "string",
                                    description: "Date in ISO 8601 format",
                                },
                                description: {
                                    type: "string",
                                    description:
                                        "What this date represents",
                                },
                            },
                        },
                        description:
                            "Important dates and deadlines found in the document",
                    },
                    {
                        name: "summary",
                        type: "string",
                        description:
                            "Brief summary of the document content",
                        maxLength: 500,
                    },
                    {
                        name: "confidence",
                        type: "number",
                        description:
                            "Overall confidence in the extraction quality",
                        minimum: 0,
                        maximum: 1,
                    },
                ],
                structuredOutput: true,
            },
            examples: [
                {
                    input: "Agreement between Acme Corp and Beta Inc effective January 15, 2026, with renewal on January 15, 2027...",
                    output: {
                        parties: ["Acme Corp", "Beta Inc"],
                        keyDates: [
                            {
                                date: "2026-01-15",
                                description: "Agreement effective date",
                            },
                            {
                                date: "2027-01-15",
                                description: "Renewal date",
                            },
                        ],
                        summary:
                            "Agreement between Acme Corp and Beta Inc with a one-year term starting January 2026.",
                        confidence: 0.91,
                    },
                },
            ],
            metadata: {
                author: "template",
                authorLevel: 0,
                description:
                    "Extract entities, dates, and parties from documents",
                tags: ["extraction", "entities", "dates"],
            },
        },
    },

    // -----------------------------------------------------------------
    // 3. Risk Assessment
    // -----------------------------------------------------------------
    {
        id: "risk-assessment",
        name: "Risk Assessment",
        description:
            "Assess risk level and flag concerns. Returns a risk level, individual risk factors with evidence, recommendations, and an overall score.",
        tags: ["risk", "assessment", "compliance"],
        schema: {
            $schema: "https://spaarke.com/schemas/prompt/v1",
            $version: 1,
            instruction: {
                role: "Risk analysis specialist",
                task: "Assess the overall risk level and identify specific risk factors in the document",
                constraints: [
                    "Provide evidence for each risk factor",
                    "Rate each factor individually",
                ],
            },
            input: {
                document: {
                    required: true,
                    placeholder: "{{document.extractedText}}",
                },
            },
            output: {
                fields: [
                    {
                        name: "riskLevel",
                        type: "string",
                        description: "Overall risk assessment level",
                        enum: ["low", "medium", "high", "critical"],
                    },
                    {
                        name: "riskFactors",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                factor: {
                                    type: "string",
                                    description:
                                        "Name or short description of the risk factor",
                                },
                                severity: {
                                    type: "string",
                                    description:
                                        "Severity level: low, medium, high, or critical",
                                },
                                evidence: {
                                    type: "string",
                                    description:
                                        "Excerpt or reference from the document supporting this risk",
                                },
                            },
                        },
                        description:
                            "Individual risk factors identified in the document",
                    },
                    {
                        name: "recommendations",
                        type: "array",
                        items: { type: "string" },
                        description:
                            "Recommended actions to mitigate identified risks",
                    },
                    {
                        name: "overallScore",
                        type: "number",
                        description:
                            "Numeric risk score from 0 (no risk) to 100 (critical risk)",
                        minimum: 0,
                        maximum: 100,
                    },
                ],
                structuredOutput: true,
            },
            examples: [
                {
                    input: "The supplier agrees to unlimited liability for consequential damages...",
                    output: {
                        riskLevel: "high",
                        riskFactors: [
                            {
                                factor: "Unlimited liability clause",
                                severity: "high",
                                evidence:
                                    "\"unlimited liability for consequential damages\" — exposes significant financial risk.",
                            },
                        ],
                        recommendations: [
                            "Negotiate a liability cap proportional to contract value",
                            "Add mutual limitation of liability clause",
                        ],
                        overallScore: 75,
                    },
                },
            ],
            metadata: {
                author: "template",
                authorLevel: 0,
                description:
                    "Assess risk level and flag concerns in documents",
                tags: ["risk", "assessment", "compliance"],
            },
        },
    },

    // -----------------------------------------------------------------
    // 4. Summarization
    // -----------------------------------------------------------------
    {
        id: "summarization",
        name: "Summarization",
        description:
            "Generate executive summary with key points and action items. Returns a concise summary, key points, prioritized action items, and a document length indicator.",
        tags: ["summary", "executive", "action-items"],
        schema: {
            $schema: "https://spaarke.com/schemas/prompt/v1",
            $version: 1,
            instruction: {
                role: "Document summarization specialist",
                task: "Generate a concise executive summary and extract key action items",
                constraints: [
                    "Keep summary under 500 characters",
                    "Action items must be specific and actionable",
                ],
            },
            input: {
                document: {
                    required: true,
                    placeholder: "{{document.extractedText}}",
                },
            },
            output: {
                fields: [
                    {
                        name: "summary",
                        type: "string",
                        description:
                            "Concise executive summary of the document",
                        maxLength: 500,
                    },
                    {
                        name: "keyPoints",
                        type: "array",
                        items: { type: "string" },
                        description:
                            "Most important points from the document",
                    },
                    {
                        name: "actionItems",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                item: {
                                    type: "string",
                                    description:
                                        "Description of the action item",
                                },
                                priority: {
                                    type: "string",
                                    description:
                                        "Priority level: low, medium, or high",
                                },
                                assignee: {
                                    type: "string",
                                    description:
                                        "Suggested assignee if identifiable from the document",
                                },
                            },
                        },
                        description:
                            "Specific, actionable items extracted from the document",
                    },
                    {
                        name: "documentLength",
                        type: "string",
                        description:
                            "Relative length classification of the original document",
                        enum: ["short", "medium", "long"],
                    },
                ],
                structuredOutput: true,
            },
            examples: [
                {
                    input: "Board meeting minutes from March 1, 2026. Topics: Q1 budget approval, new hire plan, office relocation timeline...",
                    output: {
                        summary:
                            "Board meeting covered Q1 budget approval, hiring plan for 5 engineers, and office relocation scheduled for Q3 2026.",
                        keyPoints: [
                            "Q1 budget of $2.4M approved unanimously",
                            "5 new engineering hires planned for Q2",
                            "Office relocation to downtown campus in Q3",
                        ],
                        actionItems: [
                            {
                                item: "Finalize engineering job descriptions",
                                priority: "high",
                                assignee: "VP Engineering",
                            },
                            {
                                item: "Get relocation cost estimates from three vendors",
                                priority: "medium",
                                assignee: "Facilities Manager",
                            },
                        ],
                        documentLength: "medium",
                    },
                },
            ],
            metadata: {
                author: "template",
                authorLevel: 0,
                description:
                    "Generate executive summary with key points and action items",
                tags: ["summary", "executive", "action-items"],
            },
        },
    },

    // -----------------------------------------------------------------
    // 5. Contract Review
    // -----------------------------------------------------------------
    {
        id: "contract-review",
        name: "Contract Review",
        description:
            "Legal contract analysis. Extracts parties, dates, key terms, obligations, concerns, and confidentiality status with risk assessment.",
        tags: ["contract", "legal", "review", "compliance"],
        schema: {
            $schema: "https://spaarke.com/schemas/prompt/v1",
            $version: 1,
            instruction: {
                role: "Legal contract analysis specialist",
                task: "Review the contract and extract key terms, obligations, and potential issues",
                constraints: [
                    "Focus on actionable findings",
                    "Flag any unusual or one-sided clauses",
                    "Note missing standard clauses",
                ],
            },
            input: {
                document: {
                    required: true,
                    maxLength: 50000,
                    placeholder: "{{document.extractedText}}",
                },
            },
            output: {
                fields: [
                    {
                        name: "parties",
                        type: "array",
                        items: { type: "string" },
                        description:
                            "Named parties to the contract",
                    },
                    {
                        name: "effectiveDate",
                        type: "string",
                        description:
                            "Contract effective date in ISO 8601 format",
                    },
                    {
                        name: "expirationDate",
                        type: "string",
                        description:
                            "Contract expiration or termination date in ISO 8601 format",
                    },
                    {
                        name: "keyTerms",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                term: {
                                    type: "string",
                                    description:
                                        "Name of the key contractual term",
                                },
                                description: {
                                    type: "string",
                                    description:
                                        "Summary of the term's provisions",
                                },
                            },
                        },
                        description:
                            "Key contractual terms and their descriptions",
                    },
                    {
                        name: "obligations",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                party: {
                                    type: "string",
                                    description:
                                        "The party responsible for the obligation",
                                },
                                obligation: {
                                    type: "string",
                                    description:
                                        "Description of the obligation",
                                },
                            },
                        },
                        description:
                            "Specific obligations for each party",
                    },
                    {
                        name: "concerns",
                        type: "array",
                        items: {
                            type: "object",
                            properties: {
                                issue: {
                                    type: "string",
                                    description:
                                        "Description of the concern or issue",
                                },
                                severity: {
                                    type: "string",
                                    description:
                                        "Severity level: low, medium, high, or critical",
                                },
                                clause: {
                                    type: "string",
                                    description:
                                        "Reference to the specific clause, if applicable",
                                },
                            },
                        },
                        description:
                            "Potential issues, unusual clauses, or missing standard provisions",
                    },
                    {
                        name: "isConfidential",
                        type: "boolean",
                        description:
                            "Whether the contract contains confidentiality or NDA provisions",
                    },
                    {
                        name: "riskLevel",
                        type: "string",
                        description: "Overall contract risk assessment",
                        enum: ["low", "medium", "high", "critical"],
                    },
                ],
                structuredOutput: true,
            },
            examples: [
                {
                    input: "CONSULTING AGREEMENT between Acme Corp (\"Client\") and Legal Partners LLC (\"Consultant\") effective March 1, 2026, expiring February 28, 2027...",
                    output: {
                        parties: ["Acme Corp", "Legal Partners LLC"],
                        effectiveDate: "2026-03-01",
                        expirationDate: "2027-02-28",
                        keyTerms: [
                            {
                                term: "Scope of Services",
                                description:
                                    "Consultant to provide legal advisory services as defined in Exhibit A.",
                            },
                            {
                                term: "Payment Terms",
                                description:
                                    "Monthly retainer of $15,000 due within 30 days of invoice.",
                            },
                        ],
                        obligations: [
                            {
                                party: "Acme Corp",
                                obligation:
                                    "Provide timely access to relevant documents and personnel.",
                            },
                            {
                                party: "Legal Partners LLC",
                                obligation:
                                    "Deliver monthly status reports by the 5th of each month.",
                            },
                        ],
                        concerns: [
                            {
                                issue: "No limitation of liability clause",
                                severity: "high",
                                clause: "Missing",
                            },
                            {
                                issue: "Unilateral termination favors Client only",
                                severity: "medium",
                                clause: "Section 8.2",
                            },
                        ],
                        isConfidential: true,
                        riskLevel: "medium",
                    },
                },
            ],
            metadata: {
                author: "template",
                authorLevel: 0,
                description:
                    "Legal contract analysis with key terms, obligations, and concerns",
                tags: ["contract", "legal", "review"],
            },
        },
    },
];

// ---------------------------------------------------------------------------
// Lookup Helpers
// ---------------------------------------------------------------------------

/**
 * Look up a template by its unique ID.
 *
 * @param id - The template ID (e.g., "document-classification").
 * @returns The matching template, or `undefined` if not found.
 */
export function getTemplateById(
    id: string,
): PromptSchemaTemplate | undefined {
    return PROMPT_SCHEMA_TEMPLATES.find((t) => t.id === id);
}

/**
 * Filter templates by tag.
 *
 * @param tag - A single tag to filter by.
 * @returns All templates that include the specified tag.
 */
export function getTemplatesByTag(tag: string): PromptSchemaTemplate[] {
    return PROMPT_SCHEMA_TEMPLATES.filter((t) => t.tags.includes(tag));
}
