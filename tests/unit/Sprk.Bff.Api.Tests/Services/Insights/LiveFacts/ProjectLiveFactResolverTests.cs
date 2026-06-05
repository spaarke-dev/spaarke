using System.Collections.ObjectModel;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.LiveFacts;

/// <summary>
/// Unit tests for <see cref="ProjectLiveFactResolver"/> (r2 Wave D5 task 034). Verifies the
/// per-entity resolver pattern for the <c>project:</c> subject scheme per design-a6 §6.2.
/// </summary>
public class ProjectLiveFactResolverTests
{
    private const string TenantId = "tenant-acme";
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string ProjectSubject = $"project:{ProjectId}";

    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new(MockBehavior.Strict);

    private ProjectLiveFactResolver CreateSut()
        => new(_entityServiceMock.Object, NullLogger<ProjectLiveFactResolver>.Instance);

    private static Entity BuildProject(bool withStatusFormatted = true)
    {
        var project = new Entity("sprk_project", ProjectId);
        project["sprk_projectid"] = ProjectId;
        project["sprk_name"] = "Atlas migration";
        project["sprk_projectmanager"] = new EntityReference("contact", ManagerId) { Name = "Riley Chen" };
        project["sprk_externalaccount"] = new EntityReference("account", ClientId) { Name = "Atlas Holdings" };
        project["sprk_status"] = new OptionSetValue(2);
        if (withStatusFormatted)
        {
            project.FormattedValues.Add("sprk_status", "Active");
        }
        return project;
    }

    private void SetupReturnsProject(Entity project)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_project",
                ProjectId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
    }

    [Fact]
    public async Task ResolveAsync_ProjectNamePredicate_ReturnsPlainStringFact()
    {
        SetupReturnsProject(BuildProject());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectName", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Subject.Should().Be(ProjectSubject);
        fact.Predicate.Should().Be("projectName");
        fact.Confidence.Should().Be(1.0);
        fact.Value.DisplayHint.Should().Be("text");
        fact.Value.Raw.GetString().Should().Be("Atlas migration");
        fact.ProducedBy.Id.Should().Be("dataverse://sprk_project");
        fact.Evidence[0].Ref.Should().Be($"dataverse://sprk_project/{ProjectId}#projectName");
    }

    [Fact]
    public async Task ResolveAsync_ProjectManagerPredicate_ReturnsEntityReferenceFact()
    {
        SetupReturnsProject(BuildProject());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectManager", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Predicate.Should().Be("projectManager");
        fact.Value.DisplayHint.Should().Be("entity-reference");
        fact.Value.Raw.GetProperty("id").GetString().Should().Be(ManagerId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("Riley Chen");
    }

    [Fact]
    public async Task ResolveAsync_ClientPredicate_ReturnsAccountReference()
    {
        SetupReturnsProject(BuildProject());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "client", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.Raw.GetProperty("id").GetString().Should().Be(ClientId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("Atlas Holdings");
    }

    [Fact]
    public async Task ResolveAsync_ProjectStatus_ReturnsFormattedValue()
    {
        SetupReturnsProject(BuildProject(withStatusFormatted: true));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectStatus", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.Raw.GetString().Should().Be("Active");
    }

    [Fact]
    public async Task ResolveAsync_ProjectStatus_FallsBackToNumericWhenNoFormattedValue()
    {
        SetupReturnsProject(BuildProject(withStatusFormatted: false));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectStatus", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        // FormattedValue lookup absent → resolver emits numeric value as string
        fact!.Value.Raw.GetString().Should().Be("2");
    }

    [Fact]
    public async Task ResolveAsync_CurrentProjectFactsComposite_ReturnsAllSubvalues()
    {
        SetupReturnsProject(BuildProject());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "currentProjectFacts", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.DisplayHint.Should().Be("project-facts");
        var raw = fact.Value.Raw;
        raw.GetProperty("projectName").GetString().Should().Be("Atlas migration");
        raw.GetProperty("projectManager").GetProperty("name").GetString().Should().Be("Riley Chen");
        raw.GetProperty("client").GetProperty("name").GetString().Should().Be("Atlas Holdings");
        raw.GetProperty("projectStatus").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedPredicate_Throws()
    {
        SetupReturnsProject(BuildProject());
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync(ProjectSubject, "unknownPredicate", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_InvalidScheme_Throws()
    {
        // Direct call with the wrong scheme — defensive check. (At dispatch layer the
        // parser validates first.)
        var sut = CreateSut();
        Func<Task> act = () => sut.ResolveAsync($"matter:{Guid.NewGuid()}", "projectName", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_ProjectNotFound_ReturnsNull()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_project",
                ProjectId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity sprk_project with id ... was not found."));

        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectName", TenantId, CancellationToken.None);

        fact.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnsetField_ReturnsNull()
    {
        var partial = new Entity("sprk_project", ProjectId);
        partial["sprk_projectid"] = ProjectId;
        // sprk_name intentionally unset
        SetupReturnsProject(partial);
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(ProjectSubject, "projectName", TenantId, CancellationToken.None);

        fact.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullEntityService_Throws()
    {
        Action act = () => new ProjectLiveFactResolver(null!, NullLogger<ProjectLiveFactResolver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("entityService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new ProjectLiveFactResolver(_entityServiceMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
