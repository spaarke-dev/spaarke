using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Auth;

/// <summary>
/// unified-access-control-r2 task 146 c1 — the ONE F3 decision (<see cref="SecureDesignationRemoval"/>): owner round 3b
/// F3 ("removing Secure is limited to Full Access holders plus the creator") as task 150's unsecure endpoint decides it,
/// extended to a CHILD moved out of a secure root by owner round 10 item 7. Pure decision logic over the caller's two
/// answers (WhoAmI, RetrievePrincipalAccess), stated here as delegates: no transport, no DI.
/// </summary>
public class SecureDesignationRemovalTests
{
    private static readonly Guid Me = Guid.Parse("f3f3f3f3-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("f3f3f3f3-0000-4000-8000-000000000002");
    private static readonly SecuredRecordRef SecureMatter = new("sprk_matter", Guid.Parse("f3f3f3f3-0000-4000-8000-0000000000a1"));
    private static readonly SecuredRecordRef SecureProject = new("sprk_project", Guid.Parse("f3f3f3f3-0000-4000-8000-0000000000a2"));

    /// <summary>Full Access: Collaborate plus Delete (RecordShareLevels).</summary>
    private const AccessRights FullAccess =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share
        | AccessRights.Delete;

    /// <summary>Collaborate: Write without Delete — a Write holder F3 does not admit.</summary>
    private const AccessRights Collaborate =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    private static SecureRemovalCaller Caller(Func<SecuredRecordRef, AccessRights> rights, Guid? me = null) =>
        new(_ => Task.FromResult<Guid?>(me ?? Me), (record, _) => Task.FromResult(rights(record)));

    private static SecureRemovalQuestion Moving(SecureRemovalCaller? caller, params SecuredRecordRef[] roots) => new()
    {
        Caller = caller,
        SecuredRecords = roots,
        CreatedBy = SomeoneElse,
    };

    [Fact]
    public async Task AFullAccessHolderOnTheSecureRoot_IsPermitted()
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(_ => FullAccess), SecureMatter), CancellationToken.None);

        decision.IsPermitted.Should().BeTrue();
        decision.Basis.Should().Be(SecureRemovalBasis.FullAccess);
        decision.CallerSystemUserId.Should().Be(Me);
    }

    [Fact]
    public async Task TheCreator_IsPermitted_WithoutFullAccess_AndWithoutARightsProbe()
    {
        var probed = false;
        var caller = new SecureRemovalCaller(_ => Task.FromResult<Guid?>(Me), (_, _) =>
        {
            probed = true;
            return Task.FromResult(Collaborate);
        });

        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(caller, SecureMatter) with { CreatedBy = Me }, CancellationToken.None);

        decision.IsPermitted.Should().BeTrue();
        decision.Basis.Should().Be(SecureRemovalBasis.Creator);
        probed.Should().BeFalse("the creator recorded on the row needs no rights probe");
    }

    [Fact]
    public async Task ThePersonRecordedAsCreatingAnAppCreatedRow_IsPermitted()
    {
        // createdby is the application user; sprk_createdbyperson (task 133) names the person.
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(_ => Collaborate), SecureMatter) with { CreatedByPerson = Me }, CancellationToken.None);

        decision.IsPermitted.Should().BeTrue();
        decision.Basis.Should().Be(SecureRemovalBasis.Creator);
    }

    [Fact]
    public async Task AWriteOnlyHolder_WhoDidNotCreateIt_IsRefusedNotPermitted_403()
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(_ => Collaborate), SecureMatter), CancellationToken.None);

        decision.IsPermitted.Should().BeFalse();
        decision.ReasonCode.Should().Be(SecureDesignationRemoval.NotPermittedReasonCode);
        decision.ReasonCode.Should().Be("sdap.unsecure.not_permitted", "task 150's machine-readable code, verbatim");
        decision.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task WhenTheRightsProbeThrows_ItIsUnverifiable_500_NeverPermitted()
    {
        var caller = new SecureRemovalCaller(
            _ => Task.FromResult<Guid?>(Me),
            (_, _) => Task.FromException<AccessRights>(new HttpRequestException("RetrievePrincipalAccess unavailable")));

        var decision = await SecureDesignationRemoval.DecideAsync(Moving(caller, SecureMatter), CancellationToken.None);

        decision.IsPermitted.Should().BeFalse();
        decision.ReasonCode.Should().Be(SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        decision.ReasonCode.Should().Be("sdap.unsecure.permission_unverifiable");
        decision.StatusCode.Should().Be(StatusCodes.Status500InternalServerError, "a failed read is retryable");
    }

    [Theory]
    [InlineData(true)]  // a writer that acts for no person (background, app-only automation)
    [InlineData(false)] // WhoAmI could not establish the caller
    public async Task WhenTheCallerCannotBeEstablished_ItIsUnverifiable_403(bool noCallerAtAll)
    {
        var caller = noCallerAtAll ? null : new SecureRemovalCaller(_ => Task.FromResult<Guid?>(null), (_, _) => Task.FromResult(FullAccess));

        var decision = await SecureDesignationRemoval.DecideAsync(Moving(caller, SecureMatter), CancellationToken.None);

        decision.ReasonCode.Should().Be(SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        decision.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task WhenTheCreatorPersonColumnIsAbsent_ANonFullAccessCallerIsUnverifiable_NeverPermitted()
    {
        // Main-session condition 2: sprk_createdbyperson read by its logical name before task 133's schema has run —
        // absent is "could not tell", never "allowed".
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(_ => Collaborate), SecureMatter) with
            {
                ReadCreatedByPersonAsync = _ => Task.FromResult(CreatorPersonAnswer.ColumnAbsent),
            },
            CancellationToken.None);

        decision.IsPermitted.Should().BeFalse();
        decision.ReasonCode.Should().Be(SecureDesignationRemoval.PermissionUnverifiableReasonCode);
    }

    [Fact]
    public async Task LeavingTwoSecureRoots_NeedsFullAccessOnEach()
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(record => record == SecureMatter ? FullAccess : Collaborate), SecureMatter, SecureProject),
            CancellationToken.None);

        decision.ReasonCode.Should().Be(SecureDesignationRemoval.NotPermittedReasonCode);
    }

    /// <summary>
    /// A secure record that cannot be named (c1-r1, verifier c1 item 1): there is no root to ask Full Access about, so the
    /// Full Access branch must not be read as "every root granted it" — only the creator is admitted.
    /// </summary>
    private static SecureRemovalQuestion UnidentifiedOnly(SecureRemovalCaller caller) => new()
    {
        Caller = caller,
        SecuredRecords = Array.Empty<SecuredRecordRef>(),
        IncludesUnidentifiedSecureRecord = true,
        CreatedBy = SomeoneElse,
        ReadCreatedByPersonAsync = _ => Task.FromResult(CreatorPersonAnswer.Recorded(SomeoneElse)),
    };

    [Fact]
    public async Task AnUnidentifiedSecureRecordWithNoNamedRoot_IsNeverPermitted_ForANonCreator_EvenWithFullAccess()
    {
        var probed = false;
        var caller = new SecureRemovalCaller(_ => Task.FromResult<Guid?>(Me), (_, _) =>
        {
            probed = true;
            return Task.FromResult(FullAccess);
        });

        var decision = await SecureDesignationRemoval.DecideAsync(UnidentifiedOnly(caller), CancellationToken.None);

        decision.IsPermitted.Should().BeFalse("an empty list of roots is not Full Access on every root");
        decision.ReasonCode.Should().Be(SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        decision.Basis.Should().Be(SecureRemovalBasis.SecureRecordUnidentified);
        probed.Should().BeFalse("there is no record to ask about");
    }

    [Fact]
    public async Task AnUnidentifiedSecureRecordWithNoNamedRoot_AdmitsItsCreator()
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            UnidentifiedOnly(Caller(_ => Collaborate)) with { CreatedBy = Me }, CancellationToken.None);

        decision.IsPermitted.Should().BeTrue();
        decision.Basis.Should().Be(SecureRemovalBasis.Creator);
    }

    [Fact]
    public async Task ARefusal_IsTheUnsecureEndpointsProblemDetails()
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            Moving(Caller(_ => Collaborate), SecureMatter), CancellationToken.None);
        var detail = decision.MoveOutDetail("document");

        var problem = await Render(decision.ToProblem(detail, "trace-146"));

        problem.Status.Should().Be(403);
        problem.Title.Should().Be("Forbidden");
        problem.Detail.Should().Be(detail).And.Contain("Full Access").And.Contain("created this document");
        problem.Extensions["reasonCode"]!.ToString().Should().Be("sdap.unsecure.not_permitted");
        problem.Extensions["traceId"]!.ToString().Should().Be("trace-146");
    }

    /// <summary>Executes the result against a bare HttpContext and reads the ProblemDetails it wrote.</summary>
    private static async Task<ProblemDetails> Render(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        // The request's own trace id — what every route passes (httpContext.TraceIdentifier), as the unsecure endpoint does.
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "trace-146" };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(((IStatusCodeHttpResult)result).StatusCode);
        context.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }
}
