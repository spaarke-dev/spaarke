namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Canvas layout data for the visual playbook builder.
/// Stores React Flow canvas state: viewport position and node positions.
/// </summary>
public record CanvasLayoutDto
{
    /// <summary>
    /// Canvas viewport state (pan/zoom).
    /// </summary>
    public ViewportDto? Viewport { get; init; }

    /// <summary>
    /// Nodes on the canvas with their positions and data.
    /// </summary>
    public CanvasNodeDto[] Nodes { get; init; } = [];

    /// <summary>
    /// Edges connecting nodes.
    /// </summary>
    public CanvasEdgeDto[] Edges { get; init; } = [];

    /// <summary>
    /// Schema version for forward compatibility.
    /// </summary>
    public int Version { get; init; } = 1;
}

/// <summary>
/// Viewport state for React Flow canvas.
/// </summary>
public record ViewportDto
{
    /// <summary>
    /// Horizontal pan position.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Vertical pan position.
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// Zoom level (1.0 = 100%).
    /// </summary>
    public double Zoom { get; init; } = 1.0;
}

/// <summary>
/// Node position and data for React Flow canvas.
/// </summary>
public record CanvasNodeDto
{
    /// <summary>
    /// Unique node identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Node type (e.g., aiAnalysis, condition, deliverOutput).
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// X position on canvas.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Y position on canvas.
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// Node data including label, configuration, etc.
    /// Stored as flexible dictionary to support different node types.
    /// </summary>
    public Dictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Edge connecting two nodes on the canvas.
/// </summary>
public record CanvasEdgeDto
{
    /// <summary>
    /// Unique edge identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Source node ID.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Target node ID.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Source handle ID (optional).
    /// </summary>
    public string? SourceHandle { get; init; }

    /// <summary>
    /// Target handle ID (optional).
    /// </summary>
    public string? TargetHandle { get; init; }

    /// <summary>
    /// Edge type (e.g., smoothstep, bezier).
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Whether edge is animated.
    /// </summary>
    public bool Animated { get; init; }
}

/// <summary>
/// Request to save canvas layout.
/// </summary>
public record SaveCanvasLayoutRequest
{
    /// <summary>
    /// Canvas layout data to save.
    /// </summary>
    public required CanvasLayoutDto Layout { get; init; }
}

/// <summary>
/// Response for canvas layout operations.
/// </summary>
public record CanvasLayoutResponse
{
    /// <summary>
    /// Playbook ID.
    /// </summary>
    public Guid PlaybookId { get; init; }

    /// <summary>
    /// Canvas layout data.
    /// </summary>
    public CanvasLayoutDto? Layout { get; init; }

    /// <summary>
    /// When the layout was last modified.
    /// </summary>
    public DateTime? ModifiedOn { get; init; }
}
