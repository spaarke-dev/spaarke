using OpenAI.Chat;

namespace Sprk.Bff.Api.Services.Ai.Builder;

/// <summary>
/// OpenAI function calling tool definitions for the AI Playbook Builder.
/// Converted from TL-BUILDER-* scope definitions.
///
/// Tool Categories:
/// - Canvas Operations: add_node, remove_node, create_edge, update_node_config, auto_layout, validate_canvas
/// - Scope Operations: link_scope, search_scopes, create_scope
/// </summary>
public static class BuilderToolDefinitions
{
    /// <summary>
    /// Get all builder tool definitions for OpenAI function calling.
    /// </summary>
    public static IReadOnlyList<ChatTool> GetAllTools()
    {
        return new List<ChatTool>
        {
            AddNodeTool,
            RemoveNodeTool,
            CreateEdgeTool,
            UpdateNodeConfigTool,
            LinkScopeTool,
            CreateScopeTool,
            SearchScopesTool,
            AutoLayoutTool,
            ValidateCanvasTool
        };
    }

    /// <summary>
    /// Get tool definitions by category.
    /// </summary>
    public static IReadOnlyList<ChatTool> GetToolsByCategory(ToolCategory category)
    {
        return category switch
        {
            ToolCategory.CanvasOperations => new List<ChatTool>
            {
                AddNodeTool, RemoveNodeTool, CreateEdgeTool, UpdateNodeConfigTool, AutoLayoutTool, ValidateCanvasTool
            },
            ToolCategory.ScopeOperations => new List<ChatTool>
            {
                LinkScopeTool, SearchScopesTool, CreateScopeTool
            },
            _ => GetAllTools()
        };
    }

    public enum ToolCategory
    {
        All,
        CanvasOperations,
        ScopeOperations
    }

    #region Canvas Operation Tools

    /// <summary>
    /// TL-BUILDER-001: Add a new node to the playbook canvas.
    /// </summary>
    public static ChatTool AddNodeTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.AddNode,
        functionDescription: "Add a new node to the playbook canvas. Creates a node of the specified type at the given position with initial configuration.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "nodeType": {
                        "type": "string",
                        "enum": ["aiAnalysis", "condition", "assemble", "deliver", "loop", "transform", "humanReview", "externalApi"],
                        "description": "Type of node to create"
                    },
                    "label": {
                        "type": "string",
                        "description": "Display label for the node"
                    },
                    "position": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" }
                        },
                        "description": "Canvas position (optional, auto-calculated if not provided)"
                    },
                    "config": {
                        "type": "object",
                        "description": "Node-specific configuration (scopeId, outputVariable, etc.)"
                    }
                },
                "required": ["nodeType", "label"]
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-002: Remove a node from the playbook canvas.
    /// </summary>
    public static ChatTool RemoveNodeTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.RemoveNode,
        functionDescription: "Remove a node from the playbook canvas. Also removes all edges connected to the node.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "nodeId": {
                        "type": "string",
                        "description": "ID of the node to remove"
                    },
                    "nodeLabel": {
                        "type": "string",
                        "description": "Label of the node to remove (alternative to nodeId)"
                    }
                },
                "description": "Provide either nodeId or nodeLabel"
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-003: Create an edge between two nodes.
    /// </summary>
    public static ChatTool CreateEdgeTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.CreateEdge,
        functionDescription: "Create a connection (edge) between two nodes on the canvas. Defines execution flow between nodes.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "sourceId": {
                        "type": "string",
                        "description": "ID of the source node"
                    },
                    "sourceLabel": {
                        "type": "string",
                        "description": "Label of the source node (alternative to sourceId)"
                    },
                    "targetId": {
                        "type": "string",
                        "description": "ID of the target node"
                    },
                    "targetLabel": {
                        "type": "string",
                        "description": "Label of the target node (alternative to targetId)"
                    },
                    "label": {
                        "type": "string",
                        "description": "Optional label for the edge (e.g., 'true', 'false' for conditions)"
                    },
                    "edgeType": {
                        "type": "string",
                        "enum": ["default", "success", "failure", "true", "false"],
                        "description": "Edge type for conditional routing"
                    }
                },
                "description": "Provide source and target by ID or label"
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-004: Update node configuration.
    /// </summary>
    public static ChatTool UpdateNodeConfigTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.UpdateNodeConfig,
        functionDescription: "Update the configuration of an existing node on the canvas. Can modify label, position, or type-specific settings.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "nodeId": {
                        "type": "string",
                        "description": "ID of the node to update"
                    },
                    "nodeLabel": {
                        "type": "string",
                        "description": "Label of the node to update (alternative to nodeId)"
                    },
                    "updates": {
                        "type": "object",
                        "properties": {
                            "label": {
                                "type": "string",
                                "description": "New display label"
                            },
                            "position": {
                                "type": "object",
                                "properties": {
                                    "x": { "type": "number" },
                                    "y": { "type": "number" }
                                }
                            },
                            "config": {
                                "type": "object",
                                "description": "Type-specific configuration updates (merged with existing)"
                            }
                        },
                        "description": "Fields to update"
                    }
                },
                "required": ["updates"]
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-008: Auto-layout canvas nodes.
    /// </summary>
    public static ChatTool AutoLayoutTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.AutoLayout,
        functionDescription: "Automatically arrange nodes on the canvas for visual clarity. Organizes nodes based on execution flow using dagre layout algorithm.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "direction": {
                        "type": "string",
                        "enum": ["TB", "LR", "BT", "RL"],
                        "description": "Layout direction: TB=top-bottom, LR=left-right, BT=bottom-top, RL=right-left"
                    },
                    "nodeSpacing": {
                        "type": "object",
                        "properties": {
                            "horizontal": { "type": "number", "description": "Horizontal spacing between nodes (default: 250)" },
                            "vertical": { "type": "number", "description": "Vertical spacing between nodes (default: 150)" }
                        }
                    },
                    "selectedNodesOnly": {
                        "type": "boolean",
                        "description": "If true, only rearrange selected nodes"
                    }
                }
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-009: Validate canvas structure.
    /// </summary>
    public static ChatTool ValidateCanvasTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.ValidateCanvas,
        functionDescription: "Validate the current playbook canvas for completeness and correctness. Checks for missing scopes, orphaned nodes, cycles, and configuration issues.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "strictMode": {
                        "type": "boolean",
                        "description": "If true, treat warnings as errors"
                    },
                    "validationRules": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Specific rules to check (default: all). Options: NO_START_NODE, NO_END_NODE, ORPHAN_NODE, CYCLE_DETECTED, MISSING_ACTION, CONDITION_INCOMPLETE"
                    }
                }
            }
            """)
    );

    #endregion

    #region Scope Operation Tools

    /// <summary>
    /// TL-BUILDER-005: Link a scope to a node.
    /// </summary>
    public static ChatTool LinkScopeTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.LinkScope,
        functionDescription: "Attach an existing scope (Action, Skill, Knowledge, or Tool) to a node on the canvas.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "nodeId": {
                        "type": "string",
                        "description": "ID of the node to link scope to"
                    },
                    "nodeLabel": {
                        "type": "string",
                        "description": "Label of the node (alternative to nodeId)"
                    },
                    "scopeType": {
                        "type": "string",
                        "enum": ["action", "skill", "knowledge", "tool"],
                        "description": "Type of scope to link"
                    },
                    "scopeId": {
                        "type": "string",
                        "description": "ID of the scope to link"
                    },
                    "scopeName": {
                        "type": "string",
                        "description": "Name of the scope (alternative to scopeId, e.g., 'SYS-ACT-001')"
                    },
                    "replaceExisting": {
                        "type": "boolean",
                        "description": "If true, replaces existing scope of same type. If false, adds to list (for skills/knowledge)."
                    }
                },
                "required": ["scopeType"]
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-006: Create a new scope.
    /// </summary>
    public static ChatTool CreateScopeTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.CreateScope,
        functionDescription: "Create a new scope record in Dataverse. Supports creating Actions, Skills, Knowledge, and Tool scopes with customer ownership.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "scopeType": {
                        "type": "string",
                        "enum": ["action", "skill", "knowledge", "tool"],
                        "description": "Type of scope to create"
                    },
                    "name": {
                        "type": "string",
                        "description": "Unique name for the scope (CUST- prefix will be added automatically)"
                    },
                    "displayName": {
                        "type": "string",
                        "description": "Human-readable display name"
                    },
                    "description": {
                        "type": "string",
                        "description": "Purpose and functionality description"
                    },
                    "content": {
                        "type": "object",
                        "description": "Type-specific content: systemPrompt (actions), promptFragment (skills), configuration (tools), or content (knowledge)"
                    },
                    "metadata": {
                        "type": "object",
                        "properties": {
                            "tags": { "type": "array", "items": { "type": "string" } },
                            "documentTypes": { "type": "array", "items": { "type": "string" } }
                        }
                    },
                    "basedOnId": {
                        "type": "string",
                        "description": "If this is a 'Save As' operation, ID of the source scope"
                    }
                },
                "required": ["scopeType", "name", "displayName", "content"]
            }
            """)
    );

    /// <summary>
    /// TL-BUILDER-007: Search for scopes.
    /// </summary>
    public static ChatTool SearchScopesTool => ChatTool.CreateFunctionTool(
        functionName: ToolNames.SearchScopes,
        functionDescription: "Search for existing scopes in Dataverse by type, name, tags, or semantic similarity to a query.",
        functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "scopeType": {
                        "type": "string",
                        "enum": ["action", "skill", "knowledge", "tool", "all"],
                        "description": "Type of scope to search for (default: all)"
                    },
                    "query": {
                        "type": "string",
                        "description": "Search query (matches name, description, tags)"
                    },
                    "semanticQuery": {
                        "type": "string",
                        "description": "Natural language description for semantic matching (e.g., 'extract rent amounts from lease documents')"
                    },
                    "filters": {
                        "type": "object",
                        "properties": {
                            "ownerType": {
                                "type": "string",
                                "enum": ["system", "customer", "all"],
                                "description": "Filter by scope ownership"
                            },
                            "documentTypes": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Filter by compatible document types"
                            },
                            "tags": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Filter by tags (AND logic)"
                            }
                        }
                    },
                    "limit": {
                        "type": "integer",
                        "description": "Maximum number of results to return (default: 10, max: 50)"
                    }
                }
            }
            """)
    );

    #endregion

    #region Tool Name Constants

    public static class ToolNames
    {
        public const string AddNode = "add_node";
        public const string RemoveNode = "remove_node";
        public const string CreateEdge = "create_edge";
        public const string UpdateNodeConfig = "update_node_config";
        public const string AutoLayout = "auto_layout";
        public const string ValidateCanvas = "validate_canvas";
        public const string LinkScope = "link_scope";
        public const string CreateScope = "create_scope";
        public const string SearchScopes = "search_scopes";
    }

    #endregion
}
