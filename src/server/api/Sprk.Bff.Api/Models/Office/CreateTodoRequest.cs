using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for the add-in inline "Create To Do" (email-communication-intelligence-r2, #3).
/// Corresponds to <c>POST /api/office/todo</c>.
/// </summary>
/// <remarks>
/// <para>
/// Creates a first-class <c>sprk_todo</c> record — NOT a <c>sprk_event</c> (owner 2026-09-02:
/// "we are not using the sprk-event type 'to do' anymore"). Mirrors the <b>To Do Details</b> step of the
/// <c>CreateTodoWizard</c> (<c>@spaarke/ui-components</c>): Name, Description, Assigned To (a <b>contact</b>
/// lookup — the wizard migrated <c>sprk_assignedto</c> from systemuser to contact on 2026-06-21), Due Date,
/// Priority, and Effort. Priority/Effort arrive as the already-resolved 0-100 scores (the client owns the
/// choice→score table, mirroring the wizard's client-side resolution) so the server write stays a plain
/// integer set.
/// </para>
/// <para>
/// The To Do's regarding is the record the email was filed to (owner 2026-09-02: "the To Do should be created
/// Related to the record that the email has been Related to"). Only Matter/Project/Invoice are offered as
/// regarding targets in the add-in today (the "Related to" chips), so those three friendly type names are the
/// accepted values; the service writes the entity-specific lookup + the ADR-024 resolver fields.
/// </para>
/// </remarks>
public record CreateTodoRequest
{
    /// <summary>To Do title (<c>sprk_name</c>) — required.</summary>
    [Required]
    [MaxLength(200)]
    public string? Name { get; init; }

    /// <summary>Plain-text description (<c>sprk_description</c>) — optional.</summary>
    [MaxLength(4000)]
    public string? Description { get; init; }

    /// <summary>Assignee CONTACT id (<c>sprk_assignedto</c> → contact) — optional.</summary>
    public Guid? AssignedToContactId { get; init; }

    /// <summary>Due date as ISO <c>yyyy-MM-dd</c> (<c>sprk_duedate</c> is Date-Only) — optional.</summary>
    [MaxLength(10)]
    public string? DueDate { get; init; }

    /// <summary><c>sprk_priorityscore</c> 0-100 (client resolves from the Priority choice). Default 50 (Medium).</summary>
    [Range(0, 100)]
    public int PriorityScore { get; init; } = 50;

    /// <summary><c>sprk_effortscore</c> 0-100 (client resolves from the Effort choice). Default 50 (None).</summary>
    [Range(0, 100)]
    public int EffortScore { get; init; } = 50;

    /// <summary>
    /// Friendly regarding entity type — "Matter", "Project", or "Invoice" (the add-in "Related to" chips).
    /// Optional: absent → a standalone To Do with no regarding.
    /// </summary>
    [MaxLength(50)]
    public string? RegardingEntityType { get; init; }

    /// <summary>Regarding record id (the filed record) — required when <see cref="RegardingEntityType"/> is set.</summary>
    public Guid? RegardingRecordId { get; init; }

    /// <summary>Regarding record display name — written to <c>sprk_regardingrecordname</c> (best-effort).</summary>
    [MaxLength(200)]
    public string? RegardingRecordName { get; init; }
}

/// <summary>Response for a successful <c>POST /api/office/todo</c> — the created <c>sprk_todo</c> id + name.</summary>
public record CreateTodoResponse
{
    /// <summary>Id of the created <c>sprk_todo</c>.</summary>
    public required Guid TodoId { get; init; }

    /// <summary>The To Do's name (echoed for the add-in success indicator).</summary>
    public required string Name { get; init; }
}
