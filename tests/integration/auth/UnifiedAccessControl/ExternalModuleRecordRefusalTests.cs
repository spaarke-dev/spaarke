// ebilling-r1 task 130-0 fix round 3b: GET .../api/dataverse/record/{entity}/{id} answers ONE refusal for every record the
// caller cannot be shown. A record that is outside the caller's accessible set, an id that does not exist, and a record that is
// inside the set but no longer exists all get the same status and the same body, so the answer cannot be used to tell which
// records exist. A faulted read stays a distinct 500, and an in-set record that exists is returned.
//
// KEEP-path classification (ADR-038 section 2 path #1 security-auth): every test asserts an AUTHORIZATION response on an
// external read route, so the file lives under tests/integration/auth/**.
//
// Driven through the REAL record pipeline (ExternalModuleDataEndpoints.GetScopedRecordCoreAsync) with a double for the one thing
// that cannot run in-process, the app-only Dataverse read.

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

[Trait("category", "external-access")]
public sealed class ExternalModuleRecordRefusalTests
{
    private const string ProjectEntity = "sprk_project";

    private static readonly Guid InSet = Guid.Parse("13b00000-0000-4000-8000-0000000000a1");
    private static readonly Guid OutOfSet = Guid.Parse("13b00000-0000-4000-8000-0000000000b2");

    private static ExternalModuleRegistry Registry()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(new ExternalModuleDescriptor
        {
            Name = "collaboration",
            RecordEntity = ProjectEntity,
            RecordIdAttribute = "sprk_projectid",
            AccessibleRecordIds = p => p.GetAccessibleProjectIds().ToHashSet(),
            PrimaryNameAttribute = "sprk_projectnumber",
            ReadableColumns = new HashSet<string> { "sprk_projectid", "sprk_projectname", "sprk_projectnumber" },
        });
        return registry;
    }

    private static CallerPrincipal Caller() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@lawfirm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(InSet, ExternalAccessLevel.Collaborate) },
    };

    /// <summary>The app-only read, replaced: counts its calls and either returns the record or throws what the real read throws.</summary>
    private sealed class ReadDouble
    {
        private readonly Func<Guid, IReadOnlyDictionary<string, object?>> _answer;

        public ReadDouble(Func<Guid, IReadOnlyDictionary<string, object?>> answer) => _answer = answer;

        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>> Read(string entity, Guid id, string[]? columns, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_answer(id));
        }
    }

    private static ReadDouble Gone() => new(id => throw new RecordNotFoundException(ProjectEntity, id));

    private static ReadDouble Existing() => new(id => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_projectid"] = id,
        ["sprk_projectnumber"] = "PRJ-0042",
        ["sprk_projectname"] = "Alpha diligence",
    });

    private static Task<IResult> Record(Guid id, ReadDouble read) =>
        ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            ProjectEntity, id, select: null, Caller(), Registry(), read.Read, NullLogger.Instance, CancellationToken.None);

    /// <summary>A refusal as a caller sees it: the status and the whole problem body.</summary>
    private static (int Status, string Body) Refusal(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        return (problem.StatusCode, JsonSerializer.Serialize(problem.ProblemDetails));
    }

    [Fact(DisplayName = "130-0 fix3b /record (must fire): a missing id, a record gone from the caller's set and an out-of-set id answer the same status and body")]
    public async Task MissingGoneAndOutOfSet_AnswerTheSameRefusal()
    {
        var missingRead = Gone();
        var goneRead = Gone();
        var outOfSetRead = Existing();

        var missing = Refusal(await Record(Guid.NewGuid(), missingRead));      // an id nobody holds: outside the set
        var gone = Refusal(await Record(InSet, goneRead));                     // inside the set, deleted since the set was built
        var outOfSet = Refusal(await Record(OutOfSet, outOfSetRead));          // exists, but the caller holds nothing on it

        outOfSet.Status.Should().Be(StatusCodes.Status403Forbidden, "the out-of-set answer is the reference");
        missing.Should().Be(outOfSet);
        gone.Should().Be(outOfSet, "a record the caller can no longer be shown is indistinguishable from one it never could");
        missingRead.Calls.Should().Be(0, "an id outside the set is refused before any read");
        outOfSetRead.Calls.Should().Be(0);
        goneRead.Calls.Should().Be(1, "the gone record is only discovered by the read");
        gone.Body.Should().Contain(ExternalModuleDataEndpoints.DenyRecordNotAccessible);
        gone.Body.Should().NotContain("DV_RECORD_NOT_FOUND").And.NotContain(InSet.ToString());
    }

    [Fact(DisplayName = "130-0 fix3b /record (must not fire): an in-set record that exists is returned")]
    public async Task AnInSetRecordThatExists_IsReturned()
    {
        var read = Existing();

        var result = await Record(InSet, read);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        record["sprk_projectnumber"].Should().Be("PRJ-0042");
        read.Calls.Should().Be(1);
    }

    [Fact(DisplayName = "130-0 fix3b /record (must not fire): a read that fails stays a distinct 500, not the refusal")]
    public async Task AFailedRead_StaysThe500()
    {
        var read = new ReadDouble(_ => throw new InvalidOperationException("the read failed (test)"));

        var result = await Record(InSet, read);

        result.Should().BeOfType<ProblemHttpResult>().Subject.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
}
