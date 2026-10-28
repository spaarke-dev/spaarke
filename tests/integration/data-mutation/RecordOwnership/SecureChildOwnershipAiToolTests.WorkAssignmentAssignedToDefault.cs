using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using Harness = Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Harness;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 037 (spec FR-36, owner 2026-10-27, #2354; UAC-r2 conditions 1-4): on
/// <c>POST /api/v1/child-records/sprk_workassignment</c> — the REAL route over the REAL <c>OwnedChildWrite</c> core — a work
/// assignment whose request leaves <c>sprk_assignedto</c> UNSET gets its own <c>sprk_assignedtointernal</c> contact, as-is,
/// in the create itself; and the REAL <see cref="AssignedAccessMaterializer"/> (uac-r2's harness), reading the created row
/// after the create, treats it exactly as a supplied value (Secure → a suggestion, Restricted → no contact grant). A
/// supplied value or an explicit null is kept; an update never back-fills it. The payload is the Create Work Assignment
/// wizard's (<see cref="WizardWorkAssignmentPayload"/>) plus the mapped Assigned To Internal it posts today.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid WaInternal = Guid.Parse("a0370000-0000-4000-8000-0000000000c1");
    private static readonly Guid WaChosen = Guid.Parse("a0370000-0000-4000-8000-0000000000c2");

    private const string AssignedToBind = "sprk_AssignedTo@odata.bind";

    private Dictionary<string, object?> WizardWorkAssignmentFor(Guid? assignedToInternal)
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        if (assignedToInternal is { } contact)
            payload["sprk_AssignedToInternal@odata.bind"] = $"/contacts({contact:D})";
        return payload;
    }

    // =====================================================================================
    // Must fire: unset → the row's own Assigned To Internal, in the create itself
    // =====================================================================================

    [Fact]
    public async Task WorkAssignmentCreate_AssignedToUnset_IsCreatedWithItsOwnAssignedToInternal_AsIs()
    {
        var result = await CreateChild("sprk_workassignment", WizardWorkAssignmentFor(WaInternal));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Bind(fields, AssignedToBind).Should().Be($"/contacts({WaInternal:D})",
            "FR-36: the contact Assigned To Internal names, copied as-is (both are contact lookups)");
        Bind(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({WaInternal:D})");
    }

    // =====================================================================================
    // Must not fire: the caller's value wins — a contact, or an explicit null (UNSET vs set)
    // =====================================================================================

    [Fact]
    public async Task WorkAssignmentCreate_ASuppliedAssignedTo_IsKept_NotReplacedByAssignedToInternal()
    {
        var payload = WizardWorkAssignmentFor(WaInternal);
        payload[AssignedToBind] = $"/contacts({WaChosen:D})";

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        Bind(_appCreates.Should().ContainSingle().Subject.Fields, AssignedToBind).Should().Be($"/contacts({WaChosen:D})",
            "a supplied value is never replaced (fill-only, caller wins)");
    }

    /// <summary>
    /// The distinction a future "Assigned To" picker on the wizard's Enter Info step relies on (task 038): an ABSENT column is
    /// unset and takes the default; a column the caller sends as null — as a null bind or a plain null — is the caller's
    /// explicit choice and stays null.
    /// </summary>
    [Theory]
    [InlineData(AssignedToBind)]
    [InlineData("sprk_assignedto")]
    public async Task WorkAssignmentCreate_AnExplicitNullAssignedTo_StaysNull_TheDefaultDoesNotApply(string key)
    {
        var payload = WizardWorkAssignmentFor(WaInternal);
        payload[key] = null;

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey(AssignedToBind, "the caller's clear is written as the navigation property's null bind");
        fields[AssignedToBind].Should().Match<object?>(v => v == null || ((JsonElement)v).ValueKind == JsonValueKind.Null,
            "an explicit null is a value the caller chose: it wins over the default");
    }

    [Fact]
    public async Task WorkAssignmentCreate_AssignedToInternalUnsetToo_LeavesAssignedToBlank_AndLogsAStructuredWarning()
    {
        var log = new CopiedDefaultLog();

        var result = await ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", Payload(WizardWorkAssignmentFor(assignedToInternal: null)), _user, _world.Resolver(),
            _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            HttpContextOfCaller(), log, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Subject.Fields.Keys
            .Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase),
                "no source, no default: both stay blank (never a team, never a name or email match)");
        var warning = log.Entries.Should().ContainSingle(e => e.Message.StartsWith("copied_default_source_unset")).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Values["Entity"].Should().Be("sprk_workassignment");
        warning.Values["Column"].Should().Be("sprk_assignedto");
        warning.Values["Source"].Should().Be("sprk_assignedtointernal");
    }

    // =====================================================================================
    // The same downstream path: the materializer reads the defaulted value as it reads a supplied one
    // =====================================================================================

    public static TheoryData<bool, bool, bool> SuppliedOrDefaulted_ByFamily() => new()
    {
        // supplied, secure, restricted
        { false, false, false }, { true, false, false },
        { false, true, false }, { true, true, false },
        { false, false, true }, { true, false, true },
    };

    /// <summary>
    /// UAC-r2 condition 2: the defaulted Assigned To is ON the row the create writes, so the "Assigned *" materialisation the
    /// route runs after the create (I-12 L1) reads it there and decides exactly as for a caller-supplied one: an ordinary
    /// record grants the contact, a Secure one only suggests (nothing granted, nothing shared), a Restricted one grants no
    /// contact. The twin with a SUPPLIED Assigned To must reach the same ledger state.
    /// </summary>
    [Theory]
    [MemberData(nameof(SuppliedOrDefaulted_ByFamily))]
    public async Task WorkAssignmentCreate_TheAssignedToMaterialisation_TreatsTheDefaultedValueExactlyAsASuppliedOne(
        bool supplied, bool secure, bool restricted)
    {
        var harness = new Harness
        {
            Participations = new(new RootRecordFlags(IsSecure: secure, IsRestricted: restricted)),
        };
        var contact = harness.Contact();
        _onAppCreate = (table, id, fields) =>
        {
            // The created row as Dataverse holds it: what the materializer's root read returns.
            if (table != "sprk_workassignment")
                return;
            harness.Store.Root(ExternalGrantRootType.WorkAssignment, id);
            foreach (var (key, value) in fields)
            {
                if (key.EndsWith("@odata.bind", StringComparison.Ordinal) && key.StartsWith("sprk_Assigned", StringComparison.Ordinal)
                    && (value is JsonElement { ValueKind: JsonValueKind.String } bind ? bind.GetString() : value as string) is { } path
                    && path.StartsWith("/contacts(", StringComparison.Ordinal))
                {
                    harness.Store.Assign(ExternalGrantRootType.WorkAssignment, id, key[..^"@odata.bind".Length].ToLowerInvariant(),
                        Guid.Parse(path["/contacts(".Length..^1]));
                }
            }
        };
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);
        var payload = WizardWorkAssignmentFor(contact);
        if (supplied)
            payload[AssignedToBind] = $"/contacts({contact:D})";

        var result = await CreateChild("sprk_workassignment", payload,
            scopes: services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var waId = _appCreates.Should().ContainSingle().Subject.Id;
        var row = harness.Store.RowsOf(waId, contact).Should().ContainSingle(r => r.SourceField == "sprk_assignedto",
            "the materializer evaluated the row's Assigned To — the defaulted value is on the row it reads").Subject;
        var granted = harness.Grants.ActiveRowsOf(waId, contact);
        if (secure)
        {
            row.State.Should().Be(AssignedAccessState.PendingConfirmation, "Secure: a suggestion only");
            granted.Should().BeEmpty();
            harness.Shares.Writes.Should().BeEmpty();
        }
        else if (restricted)
        {
            row.Reason.Should().Be(AssignedAccessReason.Restricted, "Restricted: no contact grant");
            granted.Should().BeEmpty();
        }
        else
        {
            row.State.Should().Be(AssignedAccessState.Granted, "an ordinary record: the contact's Collaborate grant");
            granted.Should().ContainSingle();
        }
    }

    // =====================================================================================
    // Must not fire: an update never back-fills it (create only)
    // =====================================================================================

    [Fact]
    public async Task UpdateRecord_OfAWorkAssignmentWithAnAssignedToInternal_NeverSetsAssignedTo()
    {
        var workAssignment = Guid.Parse("a0370000-0000-4000-8000-0000000000a1");
        _world.WithRecord("sprk_workassignment", workAssignment, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_assignedtointernal"] = new EntityReference("contact", WaInternal) });

        var result = await UpdateRecord("sprk_workassignment", workAssignment,
            ("sprk_name", JsonSerializer.SerializeToElement("Renamed")), Lookup("sprk_assignedtointernal", "contact", WaChosen));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var patch = _user.Patches.Should().ContainSingle().Subject;
        patch.Body.Should().NotContain("sprk_AssignedTo@", "an update never back-fills the copied default (create only)");
        _appCreates.Should().BeEmpty();
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The route's log, with each entry's structured values.</summary>
    private sealed class CopiedDefaultLog : ILogger<Program>
    {
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception),
                (state as IReadOnlyList<KeyValuePair<string, object?>>)?.ToDictionary(kv => kv.Key, kv => kv.Value)
                ?? new Dictionary<string, object?>()));
    }
}
