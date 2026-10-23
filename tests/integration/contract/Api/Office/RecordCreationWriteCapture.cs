using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 018b-1b: captures <c>RecordCreationService</c>'s single step-11 write
/// (<see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/>, a Web API PATCH-upsert, retried on 412) and presents
/// its body as the SDK <see cref="Entity"/> the old core passed to <c>IGenericEntityService.CreateAsync</c> — so a test that
/// asserts on that entity is re-pointed by swapping its capture helper and nothing else.
/// </summary>
/// <remarks>
/// <para>The view: a <c>Nav@odata.bind</c> of <c>/set(id)</c> becomes the lookup column's <see cref="EntityReference"/> (column and
/// table resolved through the <see cref="OfficeCallerDataverseDouble"/>'s lookups and entity sets; a <see langword="null"/>
/// bind is a cleared lookup); a Picklist column's number becomes an <see cref="OptionSetValue"/>, a Money column's a
/// <see cref="Money"/>, a DateTime column's text a UTC <see cref="DateTime"/>; any other <see cref="JsonElement"/> its CLR value. A bind or set the world does not know throws.</para>
/// <para><see cref="FailAttempt"/> faults chosen attempts (e.g. a 412 <see cref="HttpRequestException"/> on attempt 1), so the
/// service's number retry is driven the way the old tests drive <c>CreateAsync</c>.</para>
/// </remarks>
public sealed partial class RecordCreationWriteCapture
{
    private readonly OfficeCallerDataverseDouble _world;

    private RecordCreationWriteCapture(OfficeCallerDataverseDouble world) => _world = world;

    /// <summary>Every write attempt, in order (one per call, including attempts <see cref="FailAttempt"/> faulted).</summary>
    public List<(string Table, Guid Id, Dictionary<string, object?> Fields)> Attempts { get; } = new();

    /// <summary>The writes that landed (attempts that did not fault).</summary>
    public List<(string Table, Guid Id, Dictionary<string, object?> Fields)> Writes { get; } = new();

    /// <summary>The fault for a 1-based attempt number, or <see langword="null"/> to let it land.</summary>
    public Func<int, Exception?>? FailAttempt { get; set; }

    /// <summary>The last landed write as an SDK entity (the old <c>CreatedHolder.Entity</c>); <see langword="null"/> when none.</summary>
    public Entity? Entity => Writes.Count == 0 ? null : ToEntity(Writes[^1].Table, Writes[^1].Id, Writes[^1].Fields);

    /// <summary>Every landed write as an SDK entity, in order.</summary>
    public IReadOnlyList<Entity> Entities => Writes.Select(w => ToEntity(w.Table, w.Id, w.Fields)).ToList();

    /// <summary>Arranges <paramref name="fieldMappings"/>' <c>UpdateRecordFieldsAsync</c> to record into a new capture.</summary>
    public static RecordCreationWriteCapture Attach(
        Mock<IFieldMappingDataverseService> fieldMappings, OfficeCallerDataverseDouble world)
    {
        ArgumentNullException.ThrowIfNull(fieldMappings);
        ArgumentNullException.ThrowIfNull(world);
        var capture = new RecordCreationWriteCapture(world);
        fieldMappings
            .Setup(m => m.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .Returns((string table, Guid id, Dictionary<string, object?> fields, CancellationToken _, Guid? _) =>
            {
                var copy = new Dictionary<string, object?>(fields, StringComparer.Ordinal);
                int attempt;
                lock (capture.Attempts)
                {
                    capture.Attempts.Add((table, id, copy));
                    attempt = capture.Attempts.Count;
                }

                if (capture.FailAttempt?.Invoke(attempt) is { } fault)
                    return Task.FromException(fault);

                lock (capture.Writes)
                {
                    capture.Writes.Add((table, id, copy));
                }

                return Task.CompletedTask;
            });
        return capture;
    }

    [GeneratedRegex(@"^/(?<set>[a-z_]+)\((?<id>[0-9a-fA-F-]{36})\)$")]
    private static partial Regex BindPath();

    /// <summary>The SDK view of one Web API body (see remarks).</summary>
    public Entity ToEntity(string table, Guid id, IReadOnlyDictionary<string, object?> fields)
    {
        var entity = new Entity(table, id);
        var lookups = _world.Lookups.GetValueOrDefault(table) ?? new();
        var columns = _world.Columns.GetValueOrDefault(table) ?? new();
        foreach (var (key, value) in fields)
        {
            const string bindSuffix = "@odata.bind";
            if (key.EndsWith(bindSuffix, StringComparison.Ordinal))
            {
                var navigation = key[..^bindSuffix.Length];
                var lookup = lookups.FirstOrDefault(l => string.Equals(l.Navigation, navigation, StringComparison.Ordinal));
                if (lookup.Column is null)
                    throw new UnscriptedCallerRequestException($"write {table} {key}", $"no lookup of '{table}' has navigation '{navigation}'");

                var path = value is JsonElement { ValueKind: JsonValueKind.String } text ? text.GetString() : value as string;
                if (path is null)
                {
                    entity[lookup.Column] = null;
                    continue;
                }

                var bind = BindPath().Match(path);
                if (!bind.Success)
                    throw new UnscriptedCallerRequestException($"write {table} {key}", $"'{path}' is not a /set(id) bind");

                entity[lookup.Column] = new EntityReference(
                    _world.TableOfSet(bind.Groups["set"].Value, $"write {table} {key}"), Guid.Parse(bind.Groups["id"].Value));
                continue;
            }

            var clr = value is JsonElement element ? FromJson(element) : value;
            entity[key] = (columns.TryGetValue(key, out var column) ? column.Type : null, clr) switch
            {
                ("Picklist", int option) => new OptionSetValue(option),
                ("Money", int amount) => new Money(amount),
                ("Money", long amount) => new Money(amount),
                ("Money", decimal amount) => new Money(amount),
                ("DateTime", string text) => DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                _ => clr,
            };
        }

        return entity;
    }

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        // Boxed per arm: a bare conditional would widen every number to decimal.
        JsonValueKind.Number => element.TryGetInt32(out var small) ? (object)small
            : element.TryGetInt64(out var large) ? (object)large
            : element.GetDecimal(),
        _ => element.GetRawText(),
    };
}
