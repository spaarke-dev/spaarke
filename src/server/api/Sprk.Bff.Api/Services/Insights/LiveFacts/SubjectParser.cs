using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Parses Insights subject strings of the form <c>&lt;scheme&gt;:&lt;guid&gt;</c> per
/// design-a6 §2.1. Validates the scheme against a config-driven catalog
/// (<see cref="SubjectSchemeCatalogOptions"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stateless</b> — safe to register as a singleton per A6-D8.
/// </para>
/// </remarks>
public interface ISubjectParser
{
    /// <summary>
    /// Try to parse a subject string. Returns true on success and populates
    /// <paramref name="parsed"/> with the entity type + GUID. On failure returns false and
    /// populates <paramref name="error"/> with a caller-visible message.
    /// </summary>
    bool TryParse(string subject, out ParsedSubject parsed, out string error);

    /// <summary>
    /// Parse a subject string or throw. Use when failure indicates a programmer error
    /// (e.g., a node was authored with an invalid subject and reached runtime).
    /// </summary>
    /// <exception cref="InvalidSubjectFormatException">
    /// Thrown when the subject is missing a scheme separator, has an unparseable GUID, or
    /// has an empty scheme/id segment.
    /// </exception>
    /// <exception cref="UnknownSubjectSchemeException">
    /// Thrown when the scheme is well-formed but not registered in the catalog.
    /// </exception>
    ParsedSubject Parse(string subject);
}

/// <summary>
/// Parsed shape of a subject string. <see cref="EntityType"/> is the lower-case scheme
/// name; <see cref="EntityId"/> is the GUID.
/// </summary>
public readonly record struct ParsedSubject(string EntityType, Guid EntityId)
{
    /// <summary>Round-trip serialization back to the original subject form.</summary>
    public string ToSubjectString() => $"{EntityType}:{EntityId}";
}

/// <summary>
/// Thrown when a subject string is structurally invalid (missing <c>:</c>, empty scheme,
/// empty id, or unparseable GUID).
/// </summary>
public sealed class InvalidSubjectFormatException : Exception
{
    public InvalidSubjectFormatException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a subject's scheme is well-formed but not registered in the
/// <see cref="SubjectSchemeCatalogOptions"/> catalog. Indicates a playbook authoring
/// error (the scheme has no resolver wired up).
/// </summary>
public sealed class UnknownSubjectSchemeException : Exception
{
    public UnknownSubjectSchemeException(string scheme)
        : base($"Subject scheme '{scheme}' is not registered in the SubjectSchemeCatalogOptions. " +
               $"Register the scheme in appsettings.json under Insights:Subject:Schemes[] AND add a " +
               $"matching ILiveFactResolver registration in InsightsModule.")
    {
        Scheme = scheme;
    }

    public string Scheme { get; }
}

/// <summary>
/// Default <see cref="ISubjectParser"/> implementation backed by
/// <see cref="SubjectSchemeCatalogOptions"/>. Reads the configured catalog (or the built-in
/// default when no catalog is bound) and validates parsed scheme names against it.
/// </summary>
public sealed class SubjectParser : ISubjectParser
{
    private readonly HashSet<string> _registeredSchemes;

    public SubjectParser(IOptions<SubjectSchemeCatalogOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value.Schemes;
        var schemes = configured is { Count: > 0 }
            ? configured
            : (IReadOnlyList<SubjectSchemeOptions>)SubjectSchemeCatalogOptions.DefaultSchemes;

        _registeredSchemes = new HashSet<string>(
            schemes
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => s.Name.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool TryParse(string subject, out ParsedSubject parsed, out string error)
    {
        parsed = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(subject))
        {
            error = "Subject is empty.";
            return false;
        }

        var colonIdx = subject.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= subject.Length - 1)
        {
            error = $"Subject '{subject}' is missing a '<scheme>:<id>' separator.";
            return false;
        }

        var scheme = subject[..colonIdx].Trim().ToLowerInvariant();
        var idPart = subject[(colonIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = $"Subject '{subject}' has an empty scheme.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(idPart))
        {
            error = $"Subject '{subject}' has an empty id.";
            return false;
        }

        if (!_registeredSchemes.Contains(scheme))
        {
            error = $"Subject scheme '{scheme}' is not registered in the SubjectSchemeCatalogOptions.";
            return false;
        }

        if (!Guid.TryParse(idPart, out var entityId) || entityId == Guid.Empty)
        {
            error = $"Subject '{subject}' has an invalid or empty GUID id.";
            return false;
        }

        parsed = new ParsedSubject(scheme, entityId);
        return true;
    }

    /// <inheritdoc />
    public ParsedSubject Parse(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidSubjectFormatException("Subject is empty.");
        }

        var colonIdx = subject.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= subject.Length - 1)
        {
            throw new InvalidSubjectFormatException(
                $"Subject '{subject}' is missing a '<scheme>:<id>' separator.");
        }

        var scheme = subject[..colonIdx].Trim().ToLowerInvariant();
        var idPart = subject[(colonIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(scheme))
        {
            throw new InvalidSubjectFormatException($"Subject '{subject}' has an empty scheme.");
        }
        if (string.IsNullOrWhiteSpace(idPart))
        {
            throw new InvalidSubjectFormatException($"Subject '{subject}' has an empty id.");
        }

        if (!_registeredSchemes.Contains(scheme))
        {
            throw new UnknownSubjectSchemeException(scheme);
        }

        if (!Guid.TryParse(idPart, out var entityId) || entityId == Guid.Empty)
        {
            throw new InvalidSubjectFormatException(
                $"Subject '{subject}' has an invalid or empty GUID id.");
        }

        return new ParsedSubject(scheme, entityId);
    }
}
