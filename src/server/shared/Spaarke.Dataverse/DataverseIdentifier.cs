using System.Diagnostics.CodeAnalysis;

namespace Spaarke.Dataverse;

/// <summary>
/// #2253 fix round 1 (F1-a): whether a name may be pasted into a Dataverse Web API URL, or into an <c>@odata.id</c> /
/// <c>@odata.bind</c> reference, as a table, entity set, column or navigation name. Every builder in this library that
/// interpolates such a name asks this first and throws on anything else, before a URL is built or a request is sent.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A name is pasted into the URL unescaped (<c>EntityDefinitions(LogicalName='{name}')</c>,
/// <c>{set}({id})</c>). A crafted name such as <c>sprk_externalrecordaccess')?$select=EntitySetName#</c> ends the key early
/// and turns the rest into a fragment, so Dataverse answers for <c>sprk_externalrecordaccess</c> while every check that
/// compared the whole string saw an unknown table. A bare identifier cannot do that.</para>
/// <para><b>The shapes.</b> A table logical name or an entity-set name: an ASCII letter, then ASCII letters, digits or
/// underscores (<see cref="IsTableName"/>). A column (attribute) or navigation name may also start with an underscore, for
/// a lookup's <c>_{column}_value</c> (<see cref="IsColumnName"/>). Dataverse names are ASCII; the check is a character
/// loop, not a regular expression, so no culture rule and no <c>$</c>-before-a-trailing-newline can widen it.</para>
/// </remarks>
public static class DataverseIdentifier
{
    /// <summary>A table logical name or an entity-set name: <c>^[A-Za-z][A-Za-z0-9_]*$</c>, no whitespace.</summary>
    public static bool IsTableName([NotNullWhen(true)] string? name) => IsIdentifier(name, leadingUnderscore: false);

    /// <summary>A column (attribute) or navigation name: <c>^[A-Za-z_][A-Za-z0-9_]*$</c>, no whitespace.</summary>
    public static bool IsColumnName([NotNullWhen(true)] string? name) => IsIdentifier(name, leadingUnderscore: true);

    /// <summary>Returns <paramref name="name"/> when it is a table or entity-set name (<see cref="IsTableName"/>).</summary>
    /// <exception cref="ArgumentException">It is not; nothing was built or sent.</exception>
    public static string RequireTableName(string? name, string paramName) =>
        IsTableName(name)
            ? name!
            : throw new ArgumentException(
                "A Dataverse table or entity-set name must be a bare identifier (a letter, then letters, digits or " +
                "underscores); nothing was sent.", paramName);

    /// <summary>Returns <paramref name="name"/> when it is a column or navigation name (<see cref="IsColumnName"/>).</summary>
    /// <exception cref="ArgumentException">It is not; nothing was built or sent.</exception>
    public static string RequireColumnName(string? name, string paramName) =>
        IsColumnName(name)
            ? name!
            : throw new ArgumentException(
                "A Dataverse column or navigation name must be a bare identifier (a letter or an underscore, then letters, " +
                "digits or underscores); nothing was sent.", paramName);

    /// <summary>
    /// The metadata collections a multi-row read may name under one table's definition (#2253 fix round 3, F1). Closed:
    /// the field-mapping push reads <c>Attributes</c> and <c>ManyToOneRelationships</c>; the other three are the
    /// remaining per-table metadata collections of the same shape, which add no new capability.
    /// </summary>
    private static readonly HashSet<string> MetadataCollections = new(StringComparer.Ordinal)
    {
        "Attributes", "ManyToOneRelationships", "OneToManyRelationships", "ManyToManyRelationships", "Keys",
    };

    private const string MetadataPathPrefix = "EntityDefinitions(LogicalName='";
    private const string MetadataPathKeyEnd = "')/";

    /// <summary>
    /// Whether <paramref name="path"/> may be the collection of a multi-row read: exactly one of two shapes.
    /// <list type="bullet">
    ///   <item><description>A bare entity-set name (<see cref="IsTableName"/>).</description></item>
    ///   <item><description><c>EntityDefinitions(LogicalName='{table}')/{Collection}</c>, where <c>{table}</c> is a bare
    ///   table name and <c>{Collection}</c> is one of <c>Attributes</c>, <c>ManyToOneRelationships</c>,
    ///   <c>OneToManyRelationships</c>, <c>ManyToManyRelationships</c> or <c>Keys</c> (ordinal).</description></item>
    /// </list>
    /// </summary>
    /// <remarks>#2253 fix round 3 (F1): the field-mapping push reads a table's attribute and relationship metadata through
    /// the impersonated multi-row read, so a bare-only guard refused every push. The quoted key is still a bare
    /// identifier, so a crafted name cannot end it early.</remarks>
    public static bool IsCollectionPath([NotNullWhen(true)] string? path)
    {
        if (IsTableName(path))
            return true;

        if (path is null || !path.StartsWith(MetadataPathPrefix, StringComparison.Ordinal))
            return false;

        var rest = path[MetadataPathPrefix.Length..];
        var keyEnd = rest.IndexOf(MetadataPathKeyEnd, StringComparison.Ordinal);
        if (keyEnd < 0)
            return false;

        return IsTableName(rest[..keyEnd])
               && MetadataCollections.Contains(rest[(keyEnd + MetadataPathKeyEnd.Length)..]);
    }

    /// <summary>Returns <paramref name="path"/> when it is a multi-row read's collection (<see cref="IsCollectionPath"/>).</summary>
    /// <exception cref="ArgumentException">It is not; nothing was built or sent.</exception>
    public static string RequireCollectionPath(string? path, string paramName) =>
        IsCollectionPath(path)
            ? path!
            : throw new ArgumentException(
                "A Dataverse multi-row read must name a bare entity set, or EntityDefinitions(LogicalName='<table>')/" +
                "<Attributes|ManyToOneRelationships|OneToManyRelationships|ManyToManyRelationships|Keys>; nothing was sent.",
                paramName);

    private static bool IsIdentifier([NotNullWhen(true)] string? name, bool leadingUnderscore)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var first = name[0];
        if (!char.IsAsciiLetter(first) && !(leadingUnderscore && first == '_'))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
