using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Transforms BFF API response data into Adaptive Card JSON for M365 Copilot rendering.
/// Uses the card templates from src/solutions/CopilotAgent/cards/ as the structural basis,
/// populating them with data via System.Text.Json.Nodes manipulation.
/// </summary>
/// <remarks>
/// Registered as a singleton (ADR-010: concrete type, no interface).
/// Templates are loaded once at construction and cached for the lifetime of the service.
/// </remarks>
public sealed class AdaptiveCardFormatterService
{
    private readonly Dictionary<string, string> _templates;

    /// <summary>
    /// Initializes the service by loading all card templates from the specified directory.
    /// </summary>
    /// <param name="cardTemplatesPath">
    /// Absolute path to the directory containing card template JSON files.
    /// Defaults to the CopilotAgent/cards/ folder relative to the content root.
    /// </param>
    public AdaptiveCardFormatterService(string cardTemplatesPath)
    {
        _templates = LoadTemplates(cardTemplatesPath);
    }

    /// <summary>
    /// Formats a list of documents into an Adaptive Card.
    /// </summary>
    public string FormatDocumentList(IReadOnlyList<DocumentCardItem> documents)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock($"📄 Documents Found ({documents.Count})", weight: "Bolder", size: "Medium"));

        foreach (var doc in documents)
        {
            body.Add(CreateDocumentContainer(doc));
        }

        return Serialize(card);
    }

    /// <summary>
    /// Formats a matter summary with recent activity into an Adaptive Card.
    /// </summary>
    public string FormatMatterSummary(MatterCardItem matter, IReadOnlyList<ActivityCardItem> recentActivity)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock(matter.MatterName, weight: "Bolder", size: "Medium"));

        body.Add(CreateFactSet(
            ("Status", matter.Status),
            ("Type", matter.MatterType),
            ("Open Tasks", matter.OpenTaskCount.ToString()),
            ("Documents", matter.DocumentCount.ToString())));

        body.Add(CreateTextBlock("Summary", weight: "Bolder", spacing: "Medium"));
        body.Add(CreateTextBlock(matter.Summary, wrap: true, size: "Small"));

        body.Add(CreateTextBlock("Recent Activity", weight: "Bolder", spacing: "Medium"));

        foreach (var activity in recentActivity)
        {
            body.Add(CreateTextBlock($"• {activity.Description} ({activity.Date})", wrap: true, size: "Small"));
        }

        card["actions"] = new JsonArray
        {
            CreateSubmitAction("View Documents", new { action = "list_matter_documents", matterId = matter.MatterId }),
            CreateSubmitAction("View Tasks", new { action = "list_matter_tasks", matterId = matter.MatterId })
        };

        return Serialize(card);
    }

    /// <summary>
    /// Formats a task list into an Adaptive Card.
    /// </summary>
    public string FormatTaskList(string title, IReadOnlyList<TaskCardItem> tasks)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock(title, weight: "Bolder", size: "Medium"));

        foreach (var task in tasks)
        {
            body.Add(CreateTaskContainer(task));
        }

        return Serialize(card);
    }

    /// <summary>
    /// Formats a playbook selection menu for a specific document.
    /// </summary>
    public string FormatPlaybookMenu(PlaybookMenuContext document, IReadOnlyList<PlaybookMenuItem> playbooks)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock($"📄 {document.DocumentName}", weight: "Bolder", size: "Medium"));

        body.Add(CreateFactSet(
            ("Matter", document.MatterName),
            ("Type", document.DocumentType),
            ("Pages", document.PageCount.ToString())));

        body.Add(CreateTextBlock("What would you like to do?", spacing: "Medium", weight: "Bolder"));

        var actions = new JsonArray();
        foreach (var pb in playbooks)
        {
            actions.Add(CreateSubmitAction(
                $"📋 {pb.Name}",
                new { action = "run_playbook", playbookId = pb.PlaybookId, documentId = document.DocumentId }));
        }

        // Standard actions always present
        actions.Add(CreateSubmitAction("⚠️ Risk Scan", new { action = "run_playbook", playbookId = document.RiskScanPlaybookId, documentId = document.DocumentId }));
        actions.Add(CreateSubmitAction("📝 Quick Summary", new { action = "summarize_document", documentId = document.DocumentId }));
        actions.Add(CreateSubmitAction("🔍 Full Analysis", new { action = "deep_analysis", documentId = document.DocumentId }));
        actions.Add(CreateSubmitAction("Open in Workspace", new { action = "handoff_workspace", documentId = document.DocumentId }));

        card["actions"] = actions;

        return Serialize(card);
    }

    /// <summary>
    /// Formats risk analysis findings into an Adaptive Card.
    /// </summary>
    public string FormatRiskFindings(RiskAnalysisCardItem analysis, IReadOnlyList<RiskFlagCardItem> findings)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock($"{analysis.PlaybookName} — {analysis.DocumentName}", weight: "Bolder", size: "Medium"));
        body.Add(CreateTextBlock($"Risk Flags ({findings.Count}):", weight: "Bolder", color: "Attention", spacing: "Medium"));

        foreach (var flag in findings)
        {
            body.Add(CreateTextBlock($"• {flag.Description}", wrap: true, color: "Attention", size: "Small"));
        }

        body.Add(CreateTextBlock($"Standard Clauses ({analysis.StandardClauseCount} confirmed) ✅", spacing: "Medium", color: "Good"));

        body.Add(CreateFactSet(
            spacing: "Medium",
            ("Source", analysis.PlaybookName),
            ("Confidence", analysis.Confidence)));

        card["actions"] = new JsonArray
        {
            CreateSubmitAction("📊 Full Analysis", new { action = "view_full_analysis", analysisId = analysis.AnalysisId }),
            CreateSubmitAction("📄 View Document", new { action = "view_document", documentId = analysis.DocumentId }),
            CreateSubmitAction("Open in Workspace", new { action = "handoff_workspace", analysisId = analysis.AnalysisId, documentId = analysis.DocumentId })
        };

        return Serialize(card);
    }

    /// <summary>
    /// Formats the playbook library organized by category.
    /// </summary>
    public string FormatPlaybookLibrary(IReadOnlyList<PlaybookCategoryCardItem> categories)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock("📚 Available Playbooks", weight: "Bolder", size: "Medium"));

        foreach (var category in categories)
        {
            body.Add(CreateTextBlock($"{category.CategoryName}:", weight: "Bolder", spacing: "Medium"));

            foreach (var pb in category.Playbooks)
            {
                body.Add(CreateTextBlock($"• 📋 {pb.Name} ({pb.DocumentTypes})", wrap: true, size: "Small"));
            }
        }

        body.Add(CreateTextBlock(
            "Tell me which one and I'll help you get started, or describe what you need and I'll recommend one.",
            wrap: true, spacing: "Medium", isSubtle: true, size: "Small"));

        return Serialize(card);
    }

    /// <summary>
    /// Formats a draft email preview into an Adaptive Card.
    /// </summary>
    public string FormatEmailPreview(EmailPreviewCardItem email)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock("📧 Draft Email", weight: "Bolder", size: "Medium"));

        body.Add(CreateFactSet(
            ("To", $"{email.RecipientEmail} ({email.RecipientRole})"),
            ("Subject", email.Subject)));

        body.Add(CreateTextBlock(email.Body, wrap: true, spacing: "Medium", size: "Small"));

        card["actions"] = new JsonArray
        {
            CreateSubmitAction("📤 Send", new { action = "send_email", communicationId = email.CommunicationId }),
            CreateSubmitAction("✏️ Edit Draft", new { action = "edit_email", communicationId = email.CommunicationId }),
            CreateSubmitAction("✕ Cancel", new { action = "cancel_email", communicationId = email.CommunicationId })
        };

        return Serialize(card);
    }

    /// <summary>
    /// Formats a handoff card directing the user to the Analysis Workspace.
    /// </summary>
    public string FormatHandoffCard(
        string analysisType,
        string analysisId,
        string sourceFileId,
        string playbookId,
        string deepLinkUrl)
    {
        var template = GetTemplate("handoff-card");
        var result = template
            .Replace("${analysisType}", analysisType)
            .Replace("${analysisId}", analysisId)
            .Replace("${sourceFileId}", sourceFileId)
            .Replace("${playbookId}", playbookId)
            .Replace("${deepLinkUrl}", deepLinkUrl)
            .Replace("${riskScanId}", string.Empty);

        return result;
    }

    /// <summary>
    /// Formats a progress indicator showing analysis steps.
    /// </summary>
    public string FormatProgressIndicator(
        string documentName,
        IReadOnlyList<ProgressStepCardItem> steps,
        string analysisId,
        string? documentId = null)
    {
        var card = CreateCardBase();
        var body = card["body"]!.AsArray();

        body.Add(CreateTextBlock($"⏳ Analyzing {documentName}...", weight: "Bolder", size: "Medium"));

        foreach (var step in steps)
        {
            body.Add(CreateTextBlock(
                $"{step.StatusIcon} Step {step.Order}/{steps.Count}: {step.StepName}",
                size: "Small"));
        }

        body.Add(CreateTextBlock(
            "This analysis may take a few minutes. You can continue chatting — I'll update you when it's done.",
            wrap: true, isSubtle: true, size: "Small", spacing: "Medium"));

        var actionData = new JsonObject
        {
            ["action"] = "handoff_workspace",
            ["analysisId"] = analysisId
        };
        if (documentId is not null)
        {
            actionData["documentId"] = documentId;
        }

        card["actions"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Action.Submit",
                ["title"] = "Open in Workspace",
                ["data"] = actionData
            }
        };

        return Serialize(card);
    }

    /// <summary>
    /// Formats an error card with retry capability.
    /// </summary>
    public string FormatErrorCard(string errorMessage, string correlationId, string originalAction)
    {
        var template = GetTemplate("error-card");
        var result = template
            .Replace("${errorMessage}", EscapeJsonString(errorMessage))
            .Replace("${correlationId}", EscapeJsonString(correlationId))
            .Replace("${originalAction}", EscapeJsonString(originalAction));

        return result;
    }

    // ──────────────────────────────────────────────
    // Card building helpers
    // ──────────────────────────────────────────────

    private static JsonObject CreateCardBase() => new()
    {
        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
        ["type"] = "AdaptiveCard",
        ["version"] = "1.5",
        ["body"] = new JsonArray()
    };

    private static JsonObject CreateTextBlock(
        string text,
        string? weight = null,
        string? size = null,
        string? spacing = null,
        string? color = null,
        bool wrap = false,
        bool isSubtle = false)
    {
        var block = new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = text
        };

        if (weight is not null) block["weight"] = weight;
        if (size is not null) block["size"] = size;
        if (spacing is not null) block["spacing"] = spacing;
        if (color is not null) block["color"] = color;
        if (wrap) block["wrap"] = true;
        if (isSubtle) block["isSubtle"] = true;

        return block;
    }

    private static JsonObject CreateFactSet(params (string Title, string Value)[] facts)
    {
        return CreateFactSet(spacing: null, facts);
    }

    private static JsonObject CreateFactSet(string? spacing, params (string Title, string Value)[] facts)
    {
        var factArray = new JsonArray();
        foreach (var (title, value) in facts)
        {
            factArray.Add(new JsonObject { ["title"] = title, ["value"] = value });
        }

        var factSet = new JsonObject
        {
            ["type"] = "FactSet",
            ["facts"] = factArray
        };

        if (spacing is not null) factSet["spacing"] = spacing;

        return factSet;
    }

    private static JsonObject CreateSubmitAction(string title, object data)
    {
        return new JsonObject
        {
            ["type"] = "Action.Submit",
            ["title"] = title,
            ["data"] = JsonSerializer.SerializeToNode(data)
        };
    }

    private JsonObject CreateDocumentContainer(DocumentCardItem doc)
    {
        return new JsonObject
        {
            ["type"] = "Container",
            ["items"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "ColumnSet",
                    ["columns"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "Column",
                            ["width"] = "stretch",
                            ["items"] = new JsonArray
                            {
                                CreateTextBlock(doc.Name, weight: "Bolder", wrap: true),
                                new JsonObject
                                {
                                    ["type"] = "TextBlock",
                                    ["text"] = $"Matter: {doc.MatterName} | Type: {doc.DocumentType}",
                                    ["spacing"] = "None",
                                    ["isSubtle"] = true,
                                    ["size"] = "Small"
                                },
                                new JsonObject
                                {
                                    ["type"] = "TextBlock",
                                    ["text"] = $"Uploaded: {doc.UploadedAt} | Pages: {doc.PageCount}",
                                    ["spacing"] = "None",
                                    ["isSubtle"] = true,
                                    ["size"] = "Small"
                                }
                            }
                        }
                    }
                },
                new JsonObject
                {
                    ["type"] = "ActionSet",
                    ["actions"] = new JsonArray
                    {
                        CreateSubmitAction("📋 Analyze", new { action = "analyze_document", documentId = doc.Id }),
                        CreateSubmitAction("📝 Summary", new { action = "summarize_document", documentId = doc.Id }),
                        CreateSubmitAction("View", new { action = "view_document", documentId = doc.Id })
                    }
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = "",
                    ["separator"] = true
                }
            }
        };
    }

    private static JsonObject CreateTaskContainer(TaskCardItem task)
    {
        return new JsonObject
        {
            ["type"] = "Container",
            ["items"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "ColumnSet",
                    ["columns"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "Column",
                            ["width"] = "auto",
                            ["items"] = new JsonArray
                            {
                                CreateTextBlock(task.StatusIcon, size: "Medium")
                            },
                            ["verticalContentAlignment"] = "Center"
                        },
                        new JsonObject
                        {
                            ["type"] = "Column",
                            ["width"] = "stretch",
                            ["items"] = new JsonArray
                            {
                                CreateTextBlock(task.TaskTitle, weight: "Bolder", wrap: true),
                                new JsonObject
                                {
                                    ["type"] = "TextBlock",
                                    ["text"] = $"Due: {task.DueDate} | Matter: {task.MatterName}",
                                    ["spacing"] = "None",
                                    ["isSubtle"] = true,
                                    ["size"] = "Small"
                                }
                            }
                        }
                    }
                },
                new JsonObject
                {
                    ["type"] = "ActionSet",
                    ["actions"] = new JsonArray
                    {
                        CreateSubmitAction("Open", new { action = "open_task", taskId = task.Id }),
                        CreateSubmitAction("Complete", new { action = "complete_task", taskId = task.Id })
                    }
                }
            }
        };
    }

    // ──────────────────────────────────────────────
    // Template loading and utilities
    // ──────────────────────────────────────────────

    private static Dictionary<string, string> LoadTemplates(string directoryPath)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directoryPath))
            return templates;

        foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            templates[name] = File.ReadAllText(file);
        }

        return templates;
    }

    private string GetTemplate(string name)
    {
        if (_templates.TryGetValue(name, out var template))
            return template;

        throw new InvalidOperationException($"Adaptive Card template '{name}' not found. Available: {string.Join(", ", _templates.Keys)}");
    }

    private static string Serialize(JsonObject card)
    {
        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Escapes a string value for safe insertion into a JSON template via string.Replace.
    /// Handles quotes, backslashes, and newlines.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

// ──────────────────────────────────────────────
// Card data models
// ──────────────────────────────────────────────

/// <summary>
/// Data model for a document item in the document-list card.
/// </summary>
public sealed record DocumentCardItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string MatterName { get; init; }
    public required string DocumentType { get; init; }
    public required string UploadedAt { get; init; }
    public required int PageCount { get; init; }
}

/// <summary>
/// Data model for the matter-summary card.
/// </summary>
public sealed record MatterCardItem
{
    public required string MatterId { get; init; }
    public required string MatterName { get; init; }
    public required string Status { get; init; }
    public required string MatterType { get; init; }
    public required int OpenTaskCount { get; init; }
    public required int DocumentCount { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// A single activity entry for the matter-summary card.
/// </summary>
public sealed record ActivityCardItem
{
    public required string Description { get; init; }
    public required string Date { get; init; }
}

/// <summary>
/// Data model for a task item in the task-list card.
/// </summary>
public sealed record TaskCardItem
{
    public required string Id { get; init; }
    public required string StatusIcon { get; init; }
    public required string TaskTitle { get; init; }
    public required string DueDate { get; init; }
    public required string MatterName { get; init; }
}

/// <summary>
/// Context for the playbook-menu card (document details).
/// </summary>
public sealed record PlaybookMenuContext
{
    public required string DocumentId { get; init; }
    public required string DocumentName { get; init; }
    public required string MatterName { get; init; }
    public required string DocumentType { get; init; }
    public required int PageCount { get; init; }
    /// <summary>
    /// Default risk scan playbook ID for the "Risk Scan" action.
    /// </summary>
    public required string RiskScanPlaybookId { get; init; }
}

/// <summary>
/// A playbook option in the playbook-menu card.
/// </summary>
public sealed record PlaybookMenuItem
{
    public required string PlaybookId { get; init; }
    public required string Name { get; init; }
}

/// <summary>
/// Analysis metadata for the risk-findings card.
/// </summary>
public sealed record RiskAnalysisCardItem
{
    public required string AnalysisId { get; init; }
    public required string DocumentId { get; init; }
    public required string PlaybookName { get; init; }
    public required string DocumentName { get; init; }
    public required int StandardClauseCount { get; init; }
    public required string Confidence { get; init; }
}

/// <summary>
/// A single risk flag for the risk-findings card.
/// </summary>
public sealed record RiskFlagCardItem
{
    public required string Description { get; init; }
}

/// <summary>
/// A category grouping for the playbook-library card.
/// </summary>
public sealed record PlaybookCategoryCardItem
{
    public required string CategoryName { get; init; }
    public required IReadOnlyList<PlaybookLibraryItem> Playbooks { get; init; }
}

/// <summary>
/// A single playbook in the playbook-library card.
/// </summary>
public sealed record PlaybookLibraryItem
{
    public required string Name { get; init; }
    public required string DocumentTypes { get; init; }
}

/// <summary>
/// Data model for the email-preview card.
/// </summary>
public sealed record EmailPreviewCardItem
{
    public required string CommunicationId { get; init; }
    public required string RecipientEmail { get; init; }
    public required string RecipientRole { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
}

/// <summary>
/// A progress step for the progress-indicator card.
/// </summary>
public sealed record ProgressStepCardItem
{
    public required string StatusIcon { get; init; }
    public required int Order { get; init; }
    public required string StepName { get; init; }
}
