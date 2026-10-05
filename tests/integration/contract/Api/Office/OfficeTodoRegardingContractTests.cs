using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Contract tests for <c>POST /api/office/todo</c>'s FR-14 widening (spaarkeai-word-add-in-r1 task 035):
/// the document/communication "carrying" regarding, written alongside the existing record regarding, and
/// the single-slot ADR-024 resolver fields (<c>sprk_regardingrecordid</c> / <c>-name</c> / <c>-type</c>)
/// continuing to describe the RECORD, never the carrier — see
/// <c>projects/spaarkeai-word-add-in-r1/notes/035-todo-regarding-decision.md</c>.
/// </summary>
/// <remarks>
/// A SEPARATE test class + factory from the sibling <c>OfficeEndpointsContractTests.cs</c>, per this
/// task's boundary: that file is being appended to by another agent concurrently, and shared test
/// fixture/factory/auth-handler files must not be touched. <see cref="TodoRegardingTestWebAppFactory"/>
/// below subclasses the shared, public, override-able <see cref="OfficeTestWebAppFactory"/> (declared in
/// <c>OfficeEndpointsContractTests.cs</c>) and layers its own <see cref="IDataverseService"/> +
/// <see cref="CoreAncestorResolver"/> doubles on top — it does not modify that file.
/// </remarks>
[Trait("status", "repaired")]
public class OfficeTodoRegardingContractTests
{
    [Fact]
    public async Task Post_OfficeCreateTodo_WordDocumentAndMatterRegarding_WritesBothLookups_AndResolverFieldsDescribeTheMatter()
    {
        // Arrange — Word: a resolved sprk_document alongside the Matter the pane filed it to.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();

        var matterId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var request = new CreateTodoRequest
        {
            Name = "Review NDA red-lines",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
            RegardingRecordName = "Acme Corp — NDA",
            DocumentId = documentId,
            PriorityScore = 50,
            EffortScore = 50,
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().HaveCount(1);
        var entity = factory.CreatedEntities[0];

        // BOTH lookups written (constraint: "Setting only one when both are available is a defect").
        entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().NotBeNull();
        entity.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(matterId);
        entity.GetAttributeValue<EntityReference>("sprk_regardingdocument").Should().NotBeNull();
        entity.GetAttributeValue<EntityReference>("sprk_regardingdocument")!.Id.Should().Be(documentId);

        // Resolver fields describe the RECORD (the Matter), never the document.
        entity.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matterId.ToString());
        entity.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp — NDA");
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_OutlookCommunicationAndProjectRegarding_WritesBothLookups_AndResolverFieldsDescribeTheProject()
    {
        // Arrange — Outlook: the filed sprk_communication alongside the Project the pane filed it to.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();

        var projectId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var request = new CreateTodoRequest
        {
            Name = "Follow up with counsel",
            RegardingEntityType = "Project",
            RegardingRecordId = projectId,
            RegardingRecordName = "Beta LLC — Renewal",
            CommunicationId = communicationId,
            PriorityScore = 50,
            EffortScore = 50,
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().HaveCount(1);
        var entity = factory.CreatedEntities[0];

        entity.GetAttributeValue<EntityReference>("sprk_regardingproject").Should().NotBeNull();
        entity.GetAttributeValue<EntityReference>("sprk_regardingproject")!.Id.Should().Be(projectId);
        entity.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Should().NotBeNull();
        entity.GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(communicationId);

        entity.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(projectId.ToString());
        entity.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Beta LLC — Renewal");
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_RecordOnly_NoDocumentOrCommunication_CreatesWithRecordRegardingOnly_NoError()
    {
        // Arrange — acceptance criterion 4: only a record known (no resolved document) — graceful, not an error.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();

        var matterId = Guid.NewGuid();
        var request = new CreateTodoRequest
        {
            Name = "Draft engagement letter",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
            PriorityScore = 50,
            EffortScore = 50,
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().HaveCount(1);
        var entity = factory.CreatedEntities[0];

        entity.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(matterId);
        entity.Contains("sprk_regardingdocument").Should().BeFalse();
        entity.Contains("sprk_regardingcommunication").Should().BeFalse();
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_WhenUnauthenticated_Returns401_AndCreatesNoRow()
    {
        // Arrange — negative case (acceptance criterion 6), extended beyond the sibling file's status-code-only
        // assertion to also prove no sprk_todo row is created.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var request = new CreateTodoRequest
        {
            Name = "Should never be created",
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.CreatedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_WithWhitespaceOnlyName_ReturnsValidationFailure_AndCreatesNoRow()
    {
        // Arrange — negative case (acceptance criterion 7): a validation failure, not an unhandled exception,
        // and (extended beyond the sibling file's assertion) no row created.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();

        var request = new CreateTodoRequest
        {
            Name = "   ",
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.CreatedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_WhenCoreAncestorStampFails_ReturnsFailure_AndCreatesNoRow()
    {
        // Arrange — the fail-closed core-ancestor stamp (OfficeService.CreateTodoAsync) MUST NOT be
        // softened to a warning. Invoice is CHILD-class (CoreAncestorResolver.ChildRecordEntities), so
        // deriving its stamp requires an IGenericEntityService.RetrieveAsync read — this factory's mock
        // throws for "sprk_invoice" on purpose so StampAsync's Error path is exercised without a real
        // Dataverse round-trip.
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();

        var request = new CreateTodoRequest
        {
            Name = "Should not be created either",
            RegardingEntityType = "Invoice",
            RegardingRecordId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert — OfficeEndpoints.CreateTodoAsync maps a null service result to 403 (OFFICE_010).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.CreatedEntities.Should().BeEmpty();
    }
}

/// <summary>
/// Subclasses the shared <see cref="OfficeTestWebAppFactory"/> (declared in
/// <c>OfficeEndpointsContractTests.cs</c> — public, and its <c>ConfigureWebHost</c> is override-able)
/// rather than modifying it. Replaces <see cref="IDataverseService"/> with an entity-capturing mock so
/// tests can assert exactly what <c>OfficeService.CreateTodoAsync</c> writes, and
/// <see cref="CoreAncestorResolver"/> with an instance built from an in-memory
/// <see cref="CoreAncestorResolver.EntityColumnProbe"/> — the ADR-010-sanctioned test seam — so the
/// fail-closed core-ancestor stamp can be exercised without a real Dataverse/<c>MetadataService</c>
/// round-trip (the base factory's default <c>CoreAncestorResolver</c> registration opens a real scope
/// against <c>MetadataService</c>, which is why the sibling file's happy-path To Do test stayed Skip'd).
/// </summary>
public sealed class TodoRegardingTestWebAppFactory : OfficeTestWebAppFactory
{
    /// <summary>Every <see cref="Entity"/> passed to <c>IGenericEntityService.CreateAsync</c> during this factory's lifetime, in call order.</summary>
    public List<Entity> CreatedEntities { get; } = new();

    /// <summary>
    /// Task 083: the caller's resolved <c>systemuserid</c> (task 067's existing resolver). Loose by default —
    /// an unconfigured test leaves the caller Unresolved, matching the pre-083 behavior these tests already relied
    /// on (CreateTodo's owner-team resolution does not require it when a target is named).
    /// </summary>
    public Mock<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver> CallerResolver { get; } =
        new(MockBehavior.Loose);

    /// <summary>
    /// Task 083: task 141's user↔contact link, as <c>OfficeService.CreateTodoAsync</c> reads it
    /// (<c>IIdentityNormalizationService.ResolveAsync(systemUserId, ct).ContactId</c>). Loose by default — an
    /// unconfigured call returns a default (SystemUserId-only, ContactId null) <c>PersonIdentity</c>, i.e. "no
    /// linked contact", never a throw.
    /// </summary>
    public Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService> Identity { get; } =
        new(MockBehavior.Loose);

    /// <summary>In-memory log capture so a test can assert the "no linked contact" warning was actually written.</summary>
    public LogCapture Logs { get; } = new();

    public TodoRegardingTestWebAppFactory()
    {
        // Defaults so a test that does not touch these doubles keeps the PRE-083 behavior (caller unresolved,
        // no contact) instead of a loose mock returning null for a sealed record and 500ing the request. A
        // test overrides either Setup individually with its own It.IsAny/specific-id match.
        CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("test-default-unresolved"));
        Identity
            .Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid systemUserId, CancellationToken _) =>
                Task.FromResult(new Sprk.Bff.Api.Services.Ai.Membership.Models.PersonIdentity(systemUserId)));
    }

    /// <summary>Columns assumed present on every probed entity — covers every sprk_regarding* lookup these tests touch.</summary>
    private static readonly IReadOnlySet<string> ProbedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_regardingmatter",
        "sprk_regardingproject",
        "sprk_regardinginvoice",
        "sprk_regardingworkassignment",
        "sprk_regardingservicerequest",
        "sprk_regardingdocument",
        "sprk_regardingcommunication",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            dataverseMock
                .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Entity entity, CancellationToken _) =>
                {
                    var id = Guid.NewGuid();
                    entity.Id = id;
                    CreatedEntities.Add(entity);
                    return id;
                });
            // Deliberate failure seam for the fail-closed stamp test (task 035): Invoice is CHILD-class,
            // so CoreAncestorResolver.ResolveStampsAsync reads its own core-ancestor lookups via this call.
            dataverseMock
                .Setup(d => d.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Test: simulated core-ancestor read failure for sprk_invoice."));
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            // CoreAncestorResolver: replace the base registration (which opens a real MetadataService
            // scope) with one built from an in-memory column probe.
            services.RemoveAll<CoreAncestorResolver>();
            services.AddSingleton(sp => new CoreAncestorResolver(
                sp.GetRequiredService<IGenericEntityService>(),
                (string _, CancellationToken _) => Task.FromResult(ProbedColumns),
                sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

            // Task 083: the caller's systemuserid (task 067) and task 141's user↔contact link, both doubled
            // so a test can choose whether the caller resolves and whether their linked contact exists.
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver>();
            services.AddScoped(_ => CallerResolver.Object);
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>();
            services.AddSingleton(Identity.Object);

            // Capture logs in-memory so a test can assert the "no linked contact" warning was actually written.
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    /// <summary>
    /// In-memory <see cref="ILoggerProvider"/> capturing what the host logged, for assertion. Mirrors
    /// <c>ProvisionProjectTestFixture.LogCapture</c> (same shape, separate copy per this task's file-ownership
    /// boundary — see the class remarks above).
    /// </summary>
    public sealed class LogCapture : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new QueueLogger(_entries);

        public void Dispose() { }

        private sealed class QueueLogger : ILogger
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _entries;

            public QueueLogger(System.Collections.Concurrent.ConcurrentQueue<LogEntry> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    /// <summary>One captured log entry.</summary>
    public sealed record LogEntry(LogLevel Level, string Message);
}
