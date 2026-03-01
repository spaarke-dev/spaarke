/**
 * Slash Command Registry - Available commands for the AI Assistant
 *
 * Provides a Claude Code-like experience for building playbooks.
 * Commands are organized by category and can be filtered by user input.
 *
 * Migrated from R4 PCF: replaced PlaybookNodeType import with local type.
 *
 * @version 2.0.0 (Code Page migration)
 */

import type { PlaybookNodeType } from "../../types/canvas";

// ============================================================================
// Types
// ============================================================================

export interface SlashCommand {
    /** Command name (without /) */
    name: string;
    /** Display label */
    label: string;
    /** Description shown in palette */
    description: string;
    /** Category for grouping */
    category: "nodes" | "canvas" | "scopes" | "help" | "test";
    /** Keyboard shortcut (optional) */
    shortcut?: string;
    /** Arguments hint (optional) */
    argsHint?: string;
    /** Execute the command - returns message to send to AI or null for local action */
    execute: (args: string) => string | null;
}

// ============================================================================
// Node Type Metadata
// ============================================================================

export const NODE_TYPE_INFO: Record<
    PlaybookNodeType,
    { label: string; description: string }
> = {
    start: {
        label: "Start",
        description: "Entry point for the playbook",
    },
    aiAnalysis: {
        label: "AI Analysis",
        description: "Analyze documents or data using AI",
    },
    aiCompletion: {
        label: "AI Completion",
        description: "Generate text using AI models",
    },
    condition: {
        label: "Condition",
        description: "Branch workflow based on conditions",
    },
    deliverOutput: {
        label: "Deliver Output",
        description: "Send results to destination",
    },
    createTask: {
        label: "Create Task",
        description: "Create a task or work item",
    },
    sendEmail: {
        label: "Send Email",
        description: "Send an email notification",
    },
    wait: {
        label: "Wait",
        description: "Pause workflow for a duration",
    },
};

// ============================================================================
// Command Registry
// ============================================================================

export const COMMANDS: SlashCommand[] = [
    // Node Commands
    {
        name: "add",
        label: "Add Node",
        description: "Add a new node to the canvas",
        category: "nodes",
        argsHint: "<node-type>",
        execute: (args) => {
            const nodeType = args.trim().toLowerCase();
            if (nodeType) {
                return `Add a ${nodeType} node to my playbook`;
            }
            return "What type of node would you like to add? Options: AI Analysis, AI Completion, Condition, Deliver Output, Create Task, Send Email, Wait";
        },
    },
    {
        name: "add-analysis",
        label: "Add AI Analysis",
        description: "Add an AI Analysis node",
        category: "nodes",
        execute: () => "Add an AI Analysis node to my playbook",
    },
    {
        name: "add-completion",
        label: "Add AI Completion",
        description: "Add an AI Completion node",
        category: "nodes",
        execute: () => "Add an AI Completion node to my playbook",
    },
    {
        name: "add-condition",
        label: "Add Condition",
        description: "Add a Condition (branch) node",
        category: "nodes",
        execute: () => "Add a Condition node to branch my workflow",
    },
    {
        name: "add-output",
        label: "Add Deliver Output",
        description: "Add a Deliver Output node",
        category: "nodes",
        execute: () => "Add a Deliver Output node to send results",
    },
    {
        name: "connect",
        label: "Connect Nodes",
        description: "Connect two nodes together",
        category: "nodes",
        argsHint: "<from> to <to>",
        execute: (args) => {
            if (args.trim()) {
                return `Connect ${args}`;
            }
            return "Which nodes would you like to connect? Describe the source and target nodes.";
        },
    },
    {
        name: "configure",
        label: "Configure Node",
        description: "Configure the selected node",
        category: "nodes",
        argsHint: "<setting>",
        execute: (args) => {
            if (args.trim()) {
                return `Configure the selected node: ${args}`;
            }
            return "What would you like to configure on the selected node?";
        },
    },
    {
        name: "remove",
        label: "Remove Node",
        description: "Remove a node from the canvas",
        category: "nodes",
        argsHint: "<node>",
        execute: (args) => {
            if (args.trim()) {
                return `Remove the ${args} node`;
            }
            return "Which node would you like to remove?";
        },
    },

    // Canvas Commands
    {
        name: "clear",
        label: "Clear Canvas",
        description: "Remove all nodes and start fresh",
        category: "canvas",
        execute: () => "Clear the canvas and start a new playbook",
    },
    {
        name: "layout",
        label: "Auto Layout",
        description: "Automatically arrange nodes",
        category: "canvas",
        execute: () => "Arrange the nodes in a clean layout",
    },
    {
        name: "undo",
        label: "Undo",
        description: "Undo the last action",
        category: "canvas",
        shortcut: "Ctrl+Z",
        execute: () => "Undo the last change",
    },
    {
        name: "redo",
        label: "Redo",
        description: "Redo the last undone action",
        category: "canvas",
        shortcut: "Ctrl+Y",
        execute: () => "Redo the last undone change",
    },

    // Scope Commands
    {
        name: "skills",
        label: "List Skills",
        description: "Show available skills",
        category: "scopes",
        execute: () => "What skills are available for this playbook?",
    },
    {
        name: "knowledge",
        label: "List Knowledge",
        description: "Show available knowledge sources",
        category: "scopes",
        execute: () => "What knowledge sources are available?",
    },
    {
        name: "tools",
        label: "List Tools",
        description: "Show available tools",
        category: "scopes",
        execute: () => "What tools can I use in this playbook?",
    },
    {
        name: "add-skill",
        label: "Add Skill",
        description: "Add a skill to the selected node",
        category: "scopes",
        argsHint: "<skill-name>",
        execute: (args) => {
            if (args.trim()) {
                return `Add the ${args} skill to the selected node`;
            }
            return "Which skill would you like to add to the selected node?";
        },
    },
    {
        name: "add-knowledge",
        label: "Add Knowledge",
        description: "Add knowledge to the selected node",
        category: "scopes",
        argsHint: "<knowledge-source>",
        execute: (args) => {
            if (args.trim()) {
                return `Add ${args} as a knowledge source to the selected node`;
            }
            return "Which knowledge source would you like to add?";
        },
    },

    // Test Commands
    {
        name: "test",
        label: "Test Playbook",
        description: "Run a test execution of the playbook",
        category: "test",
        execute: () => "Run a test of this playbook",
    },
    {
        name: "validate",
        label: "Validate Playbook",
        description: "Check if the playbook is valid",
        category: "test",
        execute: () => "Validate this playbook and check for any issues",
    },

    // Help Commands
    {
        name: "help",
        label: "Help",
        description: "Show available commands and how to use them",
        category: "help",
        execute: () =>
            "What can you help me with? Show me the available commands and how to build a playbook.",
    },
    {
        name: "explain",
        label: "Explain",
        description: "Explain how the playbook works",
        category: "help",
        execute: () => "Explain how this playbook works step by step",
    },
    {
        name: "suggest",
        label: "Suggest Next Step",
        description: "Get suggestions for what to do next",
        category: "help",
        execute: () =>
            "Based on my current playbook, what should I do next? Suggest improvements or next steps.",
    },
    {
        name: "examples",
        label: "Show Examples",
        description: "Show example playbook patterns",
        category: "help",
        execute: () =>
            "Show me some example playbook patterns I can use as templates",
    },
];

// ============================================================================
// Helper Functions
// ============================================================================

export function filterCommands(query: string): SlashCommand[] {
    const lowerQuery = query.toLowerCase().trim();
    if (!lowerQuery) return COMMANDS;

    return COMMANDS.filter(
        (cmd) =>
            cmd.name.toLowerCase().includes(lowerQuery) ||
            cmd.label.toLowerCase().includes(lowerQuery) ||
            cmd.description.toLowerCase().includes(lowerQuery)
    );
}

export function getCommandsByCategory(
    category: SlashCommand["category"]
): SlashCommand[] {
    return COMMANDS.filter((cmd) => cmd.category === category);
}

export function findCommand(name: string): SlashCommand | undefined {
    return COMMANDS.find((cmd) => cmd.name.toLowerCase() === name.toLowerCase());
}

export function parseSlashCommand(
    input: string
): { command: string; args: string } | null {
    const trimmed = input.trim();
    if (!trimmed.startsWith("/")) return null;

    const withoutSlash = trimmed.slice(1);
    const spaceIndex = withoutSlash.indexOf(" ");

    if (spaceIndex === -1) {
        return { command: withoutSlash, args: "" };
    }

    return {
        command: withoutSlash.slice(0, spaceIndex),
        args: withoutSlash.slice(spaceIndex + 1).trim(),
    };
}

export const CATEGORY_LABELS: Record<SlashCommand["category"], string> = {
    nodes: "Nodes",
    canvas: "Canvas",
    scopes: "Scopes",
    test: "Testing",
    help: "Help",
};

export const CATEGORY_ORDER: SlashCommand["category"][] = [
    "nodes",
    "canvas",
    "scopes",
    "test",
    "help",
];

export default COMMANDS;
