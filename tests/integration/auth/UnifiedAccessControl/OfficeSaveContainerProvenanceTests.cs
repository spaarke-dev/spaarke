using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 085 — the Office save contract must not let a caller name the SPE
/// container the bytes land in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> <c>POST /api/office/save</c> carries <c>.AddEntityAccessFilter()</c>, which
/// authorizes the caller against <c>SaveRequest.TargetEntity</c> — and then wrote the bytes into
/// <c>SaveRequest.ContainerId</c>, a DIFFERENT client-supplied field on the same body. The
/// authorization key and the write destination were two independently caller-chosen values for one
/// decision, on an app-only MI write where no SPE ACL would catch the mismatch. Task 083 named that
/// shape option (B) and rejected it; this was it, live, in shipped code.
/// </para>
/// <para>
/// <b>Why a contract test and not only a behaviour test.</b> No shipped client ever sent
/// <c>ContainerId</c>, so the hole was the CONTRACT rather than the traffic — a behavioural test over
/// today's callers would have passed throughout the defect's life. What must stay true is that the
/// field cannot come back: a property that still deserializes is a property some future change starts
/// honouring again. So these assert the SHAPE of the request type, and fail if it is reinstated.
/// (Precedent: <c>FolderPath</c> sat dormant and always-null on this same type for the life of the
/// feature, and was removed for the same reason.)
/// </para>
/// <para>ADR-038 §2 path #1 (security-auth). The complementary mechanical proof that the container is
/// now SERVER-derived lives in <c>SpeWriteSinkContainerProvenanceGuardTests</c>, where both Office
/// sinks moved from <c>ClientSupplied</c> to <c>ServerDerivedRecord</c>.</para>
/// </remarks>
public class OfficeSaveContainerProvenanceTests
{
    /// <summary>
    /// THE test this class exists for. The request type must expose no way to name a container.
    /// </summary>
    /// <remarks>
    /// Checks for any container-naming property rather than the one historical name, because the
    /// defect is the CAPABILITY, not the spelling — reintroducing it as <c>DriveId</c> or
    /// <c>TargetContainer</c> would be the same hole with a different label.
    /// </remarks>
    [Fact]
    [Trait("Category", "OfficeSaveContainerProvenance")]
    public void SaveRequest_ExposesNoPropertyThatNamesAStorageContainer()
    {
        var offenders = typeof(SaveRequest)
            .GetProperties()
            .Where(p =>
                p.Name.Contains("Container", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("Drive", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("FolderPath", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.PropertyType.Name} {p.Name}")
            .ToList();

        offenders.Should().BeEmpty(
            "the Office save path derives its container from the record the caller was authorized " +
            "against (task 085). A property here that names a container or drive re-opens option (B): " +
            "the caller would choose the destination of an app-only MI write, on the same body that " +
            "carries the authorization key. If a caller genuinely needs to influence placement, change " +
            "the RECORD it targets — never add a second, independently-chosen value");
    }

    /// <summary>
    /// A hand-written body naming a container must be inert. This is the runtime half of the assertion
    /// above: the property is gone, so <c>System.Text.Json</c> discards the member rather than binding
    /// it anywhere.
    /// </summary>
    [Fact]
    [Trait("Category", "OfficeSaveContainerProvenance")]
    public void SaveRequest_WhenTheBodyNamesAContainer_TheValueIsDiscarded()
    {
        var json = """
        {
          "contentType": "Document",
          "targetEntity": { "entityType": "sprk_matter", "entityId": "8f14e45f-ceea-467a-9a2b-4d5a1b2c3d4e" },
          "containerId": "b!ATTACKER-CHOSEN-CONTAINER",
          "driveId": "b!ATTACKER-CHOSEN-DRIVE",
          "document": { "fileName": "contract.docx", "contentBase64": "" }
        }
        """;

        var request = JsonSerializer.Deserialize<SaveRequest>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        request.Should().NotBeNull("the body is otherwise valid — the point is what happens to the extra members");
        request!.TargetEntity!.EntityType.Should().Be("sprk_matter", "the legitimate fields still bind");

        // Nothing on the deserialized request carries either attacker-chosen value.
        JsonSerializer.Serialize(request)
            .Should().NotContain("ATTACKER-CHOSEN",
                "a caller-named container must not survive deserialization anywhere on the request — " +
                "including into the ProcessingJob payload, where before task 085 it outlived the request " +
                "inside a Dataverse row");
    }

    /// <summary>
    /// TargetEntity remains optional, and that is deliberate — but it means the no-record branch is
    /// reachable by contract. Pinning it here so a future reader does not assume a record is guaranteed:
    /// the container for that branch comes from configuration, server-side, and is fail-closed when
    /// unset. It is NOT derived from the acting user's business unit.
    /// </summary>
    /// <remarks>
    /// The acting-user derivation was this task's original brief and was deliberately not implemented:
    /// <c>RecordContainerResolver</c>'s own contract argues against it, because users sit in the
    /// Operations subtree while secure records are owned in Secure Projects — so acting-user resolution
    /// writes a secure record's content into the general Operations container, the exact isolation
    /// failure this project exists to close.
    /// </remarks>
    [Fact]
    [Trait("Category", "OfficeSaveContainerProvenance")]
    public void SaveRequest_TargetEntityIsStillOptional_SoTheNoRecordBranchIsReachableByContract()
    {
        var property = typeof(SaveRequest).GetProperty(nameof(SaveRequest.TargetEntity));

        property.Should().NotBeNull();
        Nullable.GetUnderlyingType(property!.PropertyType).Should().BeNull(
            "TargetEntity is a reference type; this assertion documents that the check below is about " +
            "the ATTRIBUTE contract, not nullable value semantics");

        property.GetCustomAttributes(
                typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: true)
            .Should().BeEmpty(
                "TargetEntity is optional by contract. If it ever becomes [Required], the no-record " +
                "branch in OfficeService.ResolveContainerAsync becomes dead code and should be removed " +
                "rather than left to rot — and the EntityAccessFilter pass-through on an absent target " +
                "stops being reachable from this route, which is worth re-checking at that point");
    }
}
