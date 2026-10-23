using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// #2188 — an <see cref="IDataverseUserClient"/> (the documented mock boundary for user-OBO Dataverse access) that answers
/// the ONE caller read <c>MappingSourceCallerMask</c> makes: <c>GET {entitySet}({id})</c> with no <c>$select</c> (#2191 review
/// K3), in the Web API's shape. It answers from a row the test supplies — by default the row the app-only read returned
/// (<see cref="MirroringAppReads"/>), i.e. a caller who can read every column the application can.
/// </summary>
/// <remarks>
/// <para>Web API fidelity that the mask's tests rely on: every column of the row is answered — a lookup only as
/// <c>_col_value</c> (its id), any other column by its logical name, the primary key always; a column under field security the
/// caller cannot read (<see cref="Secured"/>) is <c>null</c>, which is how Dataverse masks it; a property in
/// <see cref="Absent"/> is not answered at all. A read with a <c>$select</c> or any other shape is a 400.</para>
/// <para>Global namespace, like <c>ReadableDocumentsUserClient</c>. Only GET is supported; any other call fails.</para>
/// </remarks>
public sealed class MappingSourceCallerReadDouble : IDataverseUserClient
{
    private static readonly Regex ReadPath = new(
        @"^(?<set>[a-z_]+)\((?<id>[0-9a-fA-F-]{36})\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Func<Guid, Entity?> _rows;

    /// <param name="rows">The row the caller's Dataverse holds for an id (<see langword="null"/> → 404).</param>
    public MappingSourceCallerReadDouble(Func<Guid, Entity?> rows)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <summary>
    /// A caller who reads exactly what the app-only read of the same id returned: the latest <c>RetrieveAsync</c> the
    /// <paramref name="entities"/> mock answered for that id. Reads the mock's recorded invocations, so it adds no call to it.
    /// </summary>
    public static MappingSourceCallerReadDouble MirroringAppReads(Mock<IGenericEntityService> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return new MappingSourceCallerReadDouble(id =>
        {
            var read = entities.Invocations.LastOrDefault(i =>
                i.Method.Name == nameof(IGenericEntityService.RetrieveAsync)
                && i.Arguments.Count > 1
                && i.Arguments[1] is Guid asked
                && asked == id);
            return read?.ReturnValue is Task<Entity> { IsCompletedSuccessfully: true } task ? task.Result : null;
        });
    }

    /// <summary>Columns (logical names) under field security the caller cannot read — answered <c>null</c>.</summary>
    public HashSet<string> Secured { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Web API properties left OUT of the response altogether.</summary>
    public HashSet<string> Absent { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every read answers this failure status (403 denied, 404 missing, 500, 0 = no user context).</summary>
    public int? FailWithStatus { get; set; }

    /// <summary>When set, every read throws this (e.g. a <see cref="TaskCanceledException"/> timeout).</summary>
    public Exception? Throw { get; set; }

    /// <summary>Every path read, in order.</summary>
    public List<string> Paths { get; } = new();

    /// <summary>When true, every read waits until its token is cancelled — a Dataverse that never answers.</summary>
    public bool HangUntilCancelled { get; set; }

    public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        lock (Paths)
        {
            Paths.Add(relativePath);
        }

        if (HangUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return await Answer(relativePath, cancellationToken);
    }

    private Task<DataverseUserResponse> Answer(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Throw is { } exception)
        {
            throw exception;
        }

        if (FailWithStatus is { } status)
        {
            return Task.FromResult(DataverseUserResponse.Fail(status, CodeFor(status), $"HTTP {status}"));
        }

        var match = ReadPath.Match(relativePath);
        if (!match.Success)
        {
            return Task.FromResult(DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.BadRequest, "Unexpected read."));
        }

        var id = Guid.Parse(match.Groups["id"].Value);
        if (_rows(id) is not { } row)
        {
            return Task.FromResult(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found."));
        }

        var primaryKey = row.LogicalName + "id";
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            var properties = row.Attributes
                .Where(a => a.Key != primaryKey)
                .Select(a => (Property: a.Value is EntityReference ? $"_{a.Key}_value" : a.Key, Column: a.Key, a.Value))
                .Prepend((Property: primaryKey, Column: primaryKey, Value: row.Contains(primaryKey) ? row[primaryKey] : row.Id));
            foreach (var (property, column, value) in properties)
            {
                if (Absent.Contains(property))
                {
                    continue;
                }

                json.WritePropertyName(property);
                if (Secured.Contains(column))
                {
                    json.WriteNullValue();
                }
                else
                {
                    WriteValue(json, value);
                }
            }

            json.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
        return Task.FromResult(DataverseUserResponse.Ok(200, doc.RootElement.Clone()));
    }

    /// <summary>
    /// For a host whose <see cref="IDataverseUserClient"/> already answers other caller reads (a scripted world): a client that
    /// sends THIS double only the mapping-source caller read — a single-row read with no <c>$select</c> of a row this double
    /// holds — and every other call to <paramref name="other"/>.
    /// </summary>
    public IDataverseUserClient RoutedOver(IDataverseUserClient other) => new Router(this, other);

    private bool IsMappingSourceRead(string relativePath)
    {
        var match = ReadPath.Match(relativePath);
        return match.Success && _rows(Guid.Parse(match.Groups["id"].Value)) is not null;
    }

    private sealed class Router(MappingSourceCallerReadDouble source, IDataverseUserClient other) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
            => source.IsMappingSourceRead(relativePath)
                ? source.GetAsync(relativePath, cancellationToken)
                : other.GetAsync(relativePath, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
            => other.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
            => other.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
            => other.PatchAsync(relativePath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
            => other.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
            => other.DeleteAsync(relativePath, cancellationToken);
    }

    private static void WriteValue(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null: json.WriteNullValue(); break;
            case EntityReference reference: json.WriteStringValue(reference.Id.ToString("D")); break;
            case string text: json.WriteStringValue(text); break;
            case Guid guid: json.WriteStringValue(guid.ToString("D")); break;
            case bool flag: json.WriteBooleanValue(flag); break;
            case int number: json.WriteNumberValue(number); break;
            case long number: json.WriteNumberValue(number); break;
            case decimal number: json.WriteNumberValue(number); break;
            case double number: json.WriteNumberValue(number); break;
            case DateTime date: json.WriteStringValue(date.ToString("o", CultureInfo.InvariantCulture)); break;
            case OptionSetValue option: json.WriteNumberValue(option.Value); break;
            case Money money: json.WriteNumberValue(money.Value); break;
            case OptionSetValueCollection options: json.WriteStringValue(string.Join(",", options.Select(o => o.Value))); break;
            case byte[] bytes: json.WriteStringValue(Convert.ToBase64String(bytes)); break;
            default: json.WriteStringValue(value.ToString()); break; // a party list and anything else: non-null
        }
    }

    private static string CodeFor(int status) => status switch
    {
        0 => DataverseUserClientErrorCodes.UserContextRequired,
        403 => DataverseUserClientErrorCodes.AccessDenied,
        404 => DataverseUserClientErrorCodes.NotFound,
        429 => DataverseUserClientErrorCodes.RateLimited,
        _ => DataverseUserClientErrorCodes.ServiceError,
    };

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
        => throw new NotSupportedException("The mapping-source caller read is a GET.");

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        => throw new NotSupportedException("The mapping-source caller read is a GET.");

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        => throw new NotSupportedException("The mapping-source caller read is a GET.");

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
        => throw new NotSupportedException("The mapping-source caller read is a GET.");
}
