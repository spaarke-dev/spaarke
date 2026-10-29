using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// The status-to-code mapping every caller-identity Dataverse answer goes through (<see cref="DataverseUserClient.MapErrorCode"/>).
/// Callers branch on these codes: the Ontology authoring routes treat a WhoAmI <see cref="DataverseUserClientErrorCodes.AccessDenied"/>
/// as the single caller_unresolved 403 and a write's AccessDenied as Dataverse refusing the caller, read a 412 (BadRequest code,
/// status 412) as a concurrent change, and a 5xx as unavailable. A change here silently re-routes those decisions.
/// </summary>
/// <remarks>KEEP path: tests/unit/domain/** (ADR-038 §2 #7, a pure mapping). Pins the mapping as it is (ontology R1 task 101, review
/// pass 2, F3-b); it does not change it.</remarks>
public class DataverseUserClientErrorMappingTests
{
    [Theory]
    [InlineData(401, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData(403, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData(404, DataverseUserClientErrorCodes.NotFound)]
    [InlineData(400, DataverseUserClientErrorCodes.BadRequest)]
    [InlineData(412, DataverseUserClientErrorCodes.BadRequest)]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(500, DataverseUserClientErrorCodes.ServiceError)]
    [InlineData(502, DataverseUserClientErrorCodes.ServiceError)]
    [InlineData(503, DataverseUserClientErrorCodes.ServiceError)]
    [InlineData(504, DataverseUserClientErrorCodes.ServiceError)]
    public void MapErrorCode_MapsTheStatusToTheSharedVocabulary(int status, string expected) =>
        DataverseUserClient.MapErrorCode(status).Should().Be(expected);
}
