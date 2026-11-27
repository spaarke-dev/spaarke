using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// The Guid mapping of <see cref="DataverseServiceClientImpl.CreateProcessingJobAsync"/> and
/// <see cref="DataverseServiceClientImpl.UpdateProcessingJobAsync"/> (spaarke-intake-pipeline-r1 task 011 finding 1, fixed in
/// task 013): a Guid written to a lookup column must be an <see cref="EntityReference"/>, or Dataverse refuses the whole write
/// and the first intake run create or output-document update fails.
///
/// <para>Tested through the pure <see cref="DataverseServiceClientImpl.ProcessingJobGuidValue"/>, which both mappers call,
/// because the <c>ServiceClient</c> is built from configuration inside the class (precedent: <see cref="ConditionalUpdateTests"/>).
/// The Office cases pin that the existing writes are unchanged.</para>
///
/// KEEP path: tests/unit/domain/** (ADR-038 — pure mapping logic).
/// </summary>
public class ProcessingJobLookupMappingTests
{
    private static readonly Guid Id = Guid.Parse("01300000-0000-4000-8000-000000000011");

    [Theory]
    [InlineData("sprk_servicerequest", "sprk_servicerequest", true)]
    [InlineData("sprk_servicerequest", "sprk_servicerequest", false)]
    [InlineData("sprk_outputdocument", "sprk_document", true)]
    [InlineData("sprk_outputdocument", "sprk_document", false)]
    public void ProcessingJobGuidValue_IntakeRunLookup_IsAnEntityReferenceOnCreateAndUpdate(string column, string target, bool onCreate)
    {
        var value = DataverseServiceClientImpl.ProcessingJobGuidValue(column, Id, onCreate);

        value.Should().BeOfType<EntityReference>()
            .Which.Should().BeEquivalentTo(new EntityReference(target, Id), o => o.Including(r => r.LogicalName).Including(r => r.Id));
    }

    [Fact]
    public void ProcessingJobGuidValue_OfficeCreatorOnCreate_StaysASystemUserReference()
    {
        var value = DataverseServiceClientImpl.ProcessingJobGuidValue("sprk_initiatedby", Id, onCreate: true);

        value.Should().BeOfType<EntityReference>().Which.LogicalName.Should().Be("systemuser");
    }

    [Theory]
    [InlineData("sprk_initiatedby", false)] // the Office update path never sends it; its behaviour is unchanged
    [InlineData("sprk_someguidcolumn", true)]
    [InlineData("sprk_someguidcolumn", false)]
    public void ProcessingJobGuidValue_ColumnThatIsNotAMappedLookup_IsWrittenAsTheGuid(string column, bool onCreate)
    {
        DataverseServiceClientImpl.ProcessingJobGuidValue(column, Id, onCreate).Should().Be(Id);
    }
}
