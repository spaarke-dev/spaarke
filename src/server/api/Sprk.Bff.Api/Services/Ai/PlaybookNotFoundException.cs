namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Exception thrown when a playbook is not found.
/// </summary>
public class PlaybookNotFoundException : Exception
{
    /// <summary>
    /// The playbook name that was not found.
    /// </summary>
    public string? PlaybookName { get; init; }

    /// <summary>
    /// The playbook ID that was not found.
    /// </summary>
    public Guid? PlaybookId { get; init; }

    public PlaybookNotFoundException(string message)
        : base(message)
    {
    }

    public PlaybookNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for a playbook not found by name.
    /// </summary>
    public static PlaybookNotFoundException ByName(string name)
    {
        return new PlaybookNotFoundException($"Playbook '{name}' not found")
        {
            PlaybookName = name
        };
    }

    /// <summary>
    /// Creates an exception for a playbook not found by ID.
    /// </summary>
    public static PlaybookNotFoundException ById(Guid id)
    {
        return new PlaybookNotFoundException($"Playbook with ID '{id}' not found")
        {
            PlaybookId = id
        };
    }

    // Required for property initialization in factory methods
    private PlaybookNotFoundException()
        : base("Playbook not found")
    {
    }
}
