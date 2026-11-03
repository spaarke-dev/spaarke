using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="RateCardImportService"/> (ebilling-r1 task 078, FR-29 / FR-08): the template, the all-or-nothing import, the overlap rule at
/// import and at publish, and the Draft-to-Published move.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees admins and the rules (046, 071, 072) rely on — an invalid row writes nothing (no record, no
/// file), nothing is Published or Approved before publish, overlap is refused twice, and the source workbook lands unlinked in the
/// Procedure Library. Dataverse and SPE are substituted at their narrow seams; the parser, the validator and the orchestration are real.
/// </remarks>
public sealed class RateCardImportServiceTests
{
    internal static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    internal static readonly Guid UsdId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Drive = "b!procedurelibrary";

    // ── Workbook helpers (rows are positional per RateCardWorkbook's column order) ──

    internal static object?[] Tk(string? name, string? classification = "Partner", string? from = "2026-01-01", string? to = "2026-12-31", string? email = null) =>
        new object?[] { name, classification, from, to, email };

    internal static object?[] Rate(string? timekeeper, string? classification, string? approved = "800", string? rack = "1000",
        string? currency = "USD", string? from = "2026-01-01", string? to = "2026-12-31") =>
        new object?[] { timekeeper, classification, approved, rack, currency, from, to };

    internal static byte[] Workbook(object?[][] timekeepers, object?[][] rates) => RateCardWorkbook.Build(timekeepers, rates);

    internal static byte[] ValidWorkbook() => Workbook(
        new[] { Tk("Jane Doe"), Tk("John Roe", "Associate") },
        new[] { Rate("Jane Doe", null), Rate("John Roe", null, "500", "600"), Rate(null, "Paralegal", "200", "250") });

    // ── Harness ──

    internal sealed class Harness
    {
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IDocumentDataverseService> Documents = new();
        public readonly Mock<SpeFileStore> Spe;
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public readonly FinanceOptions Options = new() { ProcedureLibraryContainerId = Drive };

        public readonly Dictionary<string, List<Entity>> Rows = new();
        public readonly List<Entity> Created = new();
        public readonly List<(string Entity, Guid Id)> Deleted = new();
        public readonly List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Updated = new();
        public readonly List<(string Entity, List<(Guid id, Dictionary<string, object> fields)> Updates)> BulkUpdated = new();
        public readonly List<(string Drive, string Name, byte[] Bytes, ConflictBehavior Conflict)> Uploads = new();
        public readonly List<(string Drive, string Item)> FileDeletes = new();
        public readonly List<CreateDocumentRequest> DocumentCreates = new();
        public readonly List<UpdateDocumentRequest> DocumentUpdates = new();
        public readonly List<RecordOwnershipContext> OwnerContexts = new();
        public Func<Entity, bool>? FailCreateWhen;
        public Exception CreateFailure = new InvalidOperationException("simulated Dataverse failure");
        public RecordOwnerResolution OwnerAnswer = RecordOwnerResolution.Owned(TeamId);

        public int CreatedWrites => Created.Count + DocumentCreates.Count + Uploads.Count;

        public Harness()
        {
            Seed(new Entity("sprk_organization", FirmId));
            Seed(Currency("USD", UsdId), Currency("EUR", Guid.NewGuid()));

            var gcf = Mock.Of<IGraphClientFactory>();
            Spe = new Mock<SpeFileStore>(
                MockBehavior.Strict,
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
                null!);

            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream content, ConflictBehavior conflict, CancellationToken _) =>
                {
                    using var copy = new MemoryStream();
                    content.CopyTo(copy);
                    Uploads.Add((drive, name, copy.ToArray(), conflict));
                    return new FileHandleDto("item-1", name, null, copy.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, "https://web/item-1", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    FileDeletes.Add((drive, item));
                    return true;
                });

            Documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) =>
                {
                    DocumentCreates.Add(r);
                    return DocumentId.ToString();
                });
            Documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback((string _, UpdateDocumentRequest r, CancellationToken _) => DocumentUpdates.Add(r))
                .Returns(Task.CompletedTask);

            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RecordOwnershipContext c, CancellationToken _) =>
                {
                    OwnerContexts.Add(c);
                    return OwnerAnswer;
                });

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => Query(q));
            Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Entity entity, CancellationToken _) =>
                {
                    if (FailCreateWhen?.Invoke(entity) == true)
                    {
                        throw CreateFailure;
                    }

                    entity.Id = Guid.NewGuid();
                    Created.Add(entity);
                    Seed(entity);
                    return entity.Id;
                });
            Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((string entity, Guid id, CancellationToken _) =>
                {
                    Deleted.Add((entity, id));
                    Rows.GetValueOrDefault(entity)?.RemoveAll(r => r.Id == id);
                })
                .Returns(Task.CompletedTask);
            Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) => Updated.Add((entity, id, fields)))
                .Returns(Task.CompletedTask);
            Entities.Setup(e => e.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
                .Callback((string entity, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken _) => BulkUpdated.Add((entity, updates)))
                .Returns(Task.CompletedTask);
        }

        public RateCardImportService Service() => new(
            Entities.Object, Documents.Object, Spe.Object, Ownership.Object,
            Mock.Of<IOptionsMonitor<FinanceOptions>>(m => m.CurrentValue == Options),
            Mock.Of<ILogger<RateCardImportService>>());

        public void Seed(params Entity[] rows)
        {
            foreach (var row in rows)
            {
                if (!Rows.TryGetValue(row.LogicalName, out var list))
                {
                    Rows[row.LogicalName] = list = new List<Entity>();
                }

                list.Add(row);
            }
        }

        private static Entity Currency(string iso, Guid id)
        {
            var e = new Entity("transactioncurrency", id);
            e["isocurrencycode"] = iso;
            return e;
        }

        /// <summary>Entity whose reads report <c>MoreRecords</c> (the 5,000-row cap of IGenericEntityService).</summary>
        public string? TruncatedEntity;

        private EntityCollection Query(QueryExpression q)
        {
            var rows = (Rows.GetValueOrDefault(q.EntityName) ?? new List<Entity>())
                .Where(r => q.Criteria.Conditions.All(c => Matches(r, c)))
                .ToList();
            return new EntityCollection(q.TopCount is { } top ? rows.Take(top).ToList() : rows)
            {
                MoreRecords = TruncatedEntity == q.EntityName,
            };
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            object? actual = c.AttributeName == row.LogicalName + "id"
                ? row.Id
                : row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == "statecode" ? new OptionSetValue(0) : null;
            actual = actual switch { EntityReference r => r.Id, OptionSetValue o => o.Value, _ => actual };
            var wanted = c.Values.Select(v => v is OptionSetValue o ? o.Value : v).ToList();
            return c.Operator switch
            {
                ConditionOperator.Equal => Equals(actual, wanted[0]),
                ConditionOperator.In => wanted.Any(w => w is object[] arr
                    ? arr.Any(a => Same(actual, a))
                    : Same(actual, w)),
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }

        private static bool Same(object? a, object? b) =>
            a is string sa && b is string sb ? string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase) : Equals(a, b);
    }

    internal static RateCardImportRequest Request(RateCardCoreRecord? core = null, Guid? firm = null) =>
        new(firm ?? FirmId, "2026 Acme rates", core, CallerOid);

    // ═══════════════════════════════════════════════════════════════════════════
    // Template and parser
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Template_RoundTripsThroughTheParser_WithBothSheetsAndTheExampleRowIgnored()
    {
        var bytes = new Harness().Service().BuildTemplate();

        var read = RateCardWorkbook.Read(new MemoryStream(bytes));

        read.Errors.Should().BeEmpty();
        read.TimekeeperRows.Should().BeEmpty("the template's example row is skipped");
        read.RateRows.Should().BeEmpty();
    }

    [Fact]
    public void Template_ColumnsAreTheSingleSource_AndAReadWorkbookUsesThem()
    {
        var read = RateCardWorkbook.Read(new MemoryStream(Workbook(
            new[] { Tk("Jane Doe", email: "jane@firm.com") }, new[] { Rate("Jane Doe", null) })));

        read.Errors.Should().BeEmpty();
        read.TimekeeperRows.Single().Cells.Keys.Should().BeSubsetOf(RateCardWorkbook.TimekeeperColumns);
        read.RateRows.Single().Cells.Keys.Should().BeSubsetOf(RateCardWorkbook.RateColumns);
        read.TimekeeperRows.Single().Row.Should().Be(2);
    }

    [Fact]
    public async Task Import_OfTheUnmodifiedTemplate_IsRefusedAsEmpty_AndWritesNothing()
    {
        var h = new Harness();

        var outcome = await h.Service().ImportAsync(Request(), h.Service().BuildTemplate(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("no timekeeper or rate rows");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_OfANonWorkbookFile_ReturnsAnErrorAndWritesNothing()
    {
        var h = new Harness();

        var outcome = await h.Service().ImportAsync(Request(), new byte[] { 1, 2, 3, 4 }, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("not a valid .xlsx");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_OfAZipWhoseSheetXmlIsCorrupt_IsABadFile_NotAServerFault()
    {
        var h = new Harness();
        var bytes = ValidWorkbook();
        using (var ms = new MemoryStream())
        {
            ms.Write(bytes);
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
            {
                var entry = zip.Entries.First(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
                var name = entry.FullName;
                entry.Delete();
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("<worksheet><sheetData><row r=\"1\"><c"); // truncated, not well-formed
            }

            bytes = ms.ToArray();
        }

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("not a valid .xlsx");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_OfAWorkbookThatUnpacksTooLarge_IsRefusedBeforeItIsParsed()
    {
        var h = new Harness();
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("xl/worksheets/sheet1.xml", System.IO.Compression.CompressionLevel.Optimal).Open();
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < (RateCardWorkbook.MaxUnpackedBytes / chunk.Length) + 2; i++)
            {
                s.Write(chunk);
            }
        }

        var outcome = await h.Service().ImportAsync(Request(), ms.ToArray(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("too large when unpacked");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_WithAMissingSheet_NamesTheSheet_AndWritesNothing()
    {
        var h = new Harness();
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
            var part = wb.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
            part.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData());
            wb.Workbook.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Sheets>()!.Append(
                new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = wb.GetIdOfPart(part), SheetId = 1, Name = "Timekeepers" });
            wb.Workbook.Save();
        }

        var outcome = await h.Service().ImportAsync(Request(), ms.ToArray(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Sheet == "Rates" && e.Message.Contains("no sheet named 'Rates'"));
        outcome.Errors.Should().Contain(e => e.Sheet == "Timekeepers" && e.Column == "Name" && e.Message.Contains("missing from the header"));
        h.CreatedWrites.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Import: valid
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_Valid_CreatesOneDraftCardWithItsRatesAndPendingTimekeepers_NothingPublishedOrApproved()
    {
        var h = new Harness();

        var outcome = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        outcome.Value!.Status.Should().Be("Draft");
        outcome.Value.RateCount.Should().Be(3);
        outcome.Value.TimekeepersCreated.Should().Be(2);

        var card = h.Created.Single(e => e.LogicalName == "sprk_ratecard");
        card.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RateCardImportService.CardDraft);
        card.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        card.GetAttributeValue<EntityReference>("sprk_sourcedocument").Id.Should().Be(DocumentId);
        card.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
        card.GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        card.GetAttributeValue<DateTime>("sprk_effectiveto").Should().Be(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        card.Contains("sprk_matter").Should().BeFalse();

        var timekeepers = h.Created.Where(e => e.LogicalName == "sprk_timekeeper").ToList();
        timekeepers.Should().HaveCount(2);
        timekeepers.Should().OnlyContain(t => t.GetAttributeValue<OptionSetValue>("statuscode").Value == RateCardImportService.TimekeeperPending);
        timekeepers.Should().OnlyContain(t => t.GetAttributeValue<EntityReference>("sprk_firm").Id == FirmId);
        timekeepers.Select(t => t.GetAttributeValue<string>("sprk_name")).Should().BeEquivalentTo("Jane Doe", "John Roe");
        timekeepers.Single(t => t.GetAttributeValue<string>("sprk_name") == "John Roe")
            .GetAttributeValue<OptionSetValue>("sprk_classification").Value.Should().Be(100000003); // Associate

        var rates = h.Created.Where(e => e.LogicalName == "sprk_ratecardrate").ToList();
        rates.Should().HaveCount(3);
        rates.Should().OnlyContain(r => r.GetAttributeValue<EntityReference>("sprk_ratecard").Id == card.Id);
        var janeId = timekeepers.Single(t => t.GetAttributeValue<string>("sprk_name") == "Jane Doe").Id;
        var jane = rates.Single(r => r.GetAttributeValue<EntityReference>("sprk_timekeeper")?.Id == janeId);
        jane.GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(800m);
        jane.GetAttributeValue<Money>("sprk_rackrate").Value.Should().Be(1000m);
        jane.GetAttributeValue<EntityReference>("transactioncurrencyid").Id.Should().Be(UsdId);
        var byClassification = rates.Single(r => r.Contains("sprk_classification"));
        byClassification.GetAttributeValue<OptionSetValue>("sprk_classification").Value.Should().Be(100000004); // Paralegal
        byClassification.Contains("sprk_timekeeper").Should().BeFalse();

        h.Updated.Should().BeEmpty("import never moves anything to Published or Approved");
        h.BulkUpdated.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_Valid_StoresTheWorkbookInTheProcedureLibrary_AsAnUnlinkedDocument_LinkedAsTheCardsSource()
    {
        var h = new Harness();
        var workbook = ValidWorkbook();

        var outcome = await h.Service().ImportAsync(Request(), workbook, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        var upload = h.Uploads.Should().ContainSingle().Subject;
        upload.Drive.Should().Be(Drive);
        upload.Bytes.Should().Equal(workbook);
        upload.Name.Should().EndWith(".xlsx").And.NotContain("/");

        h.DocumentCreates.Should().ContainSingle().Which.ContainerId.Should().Be(Drive);
        h.DocumentCreates[0].OwningTeamId.Should().Be(TeamId);
        var pointers = h.DocumentUpdates.Should().ContainSingle().Subject;
        pointers.GraphDriveId.Should().Be(Drive);
        pointers.GraphItemId.Should().Be("item-1");

        // DEF-015: no Procedure Library document row is linked to a matter, project or work assignment (or any other record).
        typeof(UpdateDocumentRequest).GetProperties()
            .Where(p => p.Name.EndsWith("Lookup", StringComparison.Ordinal))
            .Should().OnlyContain(p => p.GetValue(pointers) == null, "the source document is created unlinked (DEF-015)");
        typeof(CreateDocumentRequest).GetProperties()
            .Where(p => p.Name.EndsWith("Lookup", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WithACoreRecord_ScopesTheCardToItAndOwnsItByTheRecord()
    {
        var h = new Harness();
        var matterId = Guid.NewGuid();
        h.Seed(new Entity("sprk_matter", matterId));

        var outcome = await h.Service().ImportAsync(Request(new RateCardCoreRecord("sprk_matter", matterId)), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        var card = h.Created.Single(e => e.LogicalName == "sprk_ratecard");
        card.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(matterId);
        h.OwnerContexts.Should().ContainSingle().Which.TargetRecordId.Should().Be(matterId);
        h.OwnerContexts[0].CallerObjectId.Should().Be(CallerOid);
        // 194b R3b-2: asked through ForRow for the card's table, the core under the column the card writes it to.
        h.OwnerContexts[0].ChildEntityLogicalName.Should().Be(RateCardImportService.RateCardEntity);
        h.OwnerContexts[0].Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", matterId));

        // DEF-015: even when the card is scoped to a matter, the Procedure Library document is not linked to it.
        typeof(UpdateDocumentRequest).GetProperties()
            .Where(p => p.Name.EndsWith("Lookup", StringComparison.Ordinal))
            .Should().OnlyContain(p => p.GetValue(h.DocumentUpdates.Single()) == null);
    }

    [Fact]
    public async Task Import_WithNoCoreRecord_ResolvesTheOwnerFromTheCaller()
    {
        var h = new Harness();

        await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        var context = h.OwnerContexts.Should().ContainSingle().Subject;
        context.HasTarget.Should().BeFalse();
        context.CallerObjectId.Should().Be(CallerOid);
    }

    [Fact]
    public async Task Import_AcceptsExcelDateAndNumberCells_AndLedesCodesAndEmailContacts()
    {
        var h = new Harness();
        var contactId = Guid.NewGuid();
        var contact = new Entity("contact", contactId);
        contact["emailaddress1"] = "Jane.Doe@Firm.com";
        h.Seed(contact);
        var bytes = Workbook(
            new[] { new object?[] { "Jane Doe", "PARTNR", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), "jane.doe@firm.com" } },
            new[] { new object?[] { "Jane Doe", null, 800.5, 1000, "usd", new DateTime(2026, 1, 1), null } });

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        h.Created.Single(e => e.LogicalName == "sprk_timekeeper").GetAttributeValue<EntityReference>("sprk_contact").Id.Should().Be(contactId);
        h.Created.Single(e => e.LogicalName == "sprk_ratecardrate").GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(800.5m);
        h.Created.Single(e => e.LogicalName == "sprk_ratecard").Contains("sprk_effectiveto").Should().BeFalse("an open-ended rate leaves the card open-ended");
    }

    [Fact]
    public async Task Import_ReusesTimekeepersAlreadyOnTheFirmRoster_InsteadOfDuplicatingThem()
    {
        var h = new Harness();
        var rosterId = Guid.NewGuid();
        var rostered = new Entity("sprk_timekeeper", rosterId);
        rostered["sprk_name"] = "Jane  Doe";
        rostered["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        // The sheet row's values (the Tk defaults) equal the stored row's, so it is reused (a different value is the 078 review F1-2 rule).
        rostered["sprk_classification"] = new OptionSetValue(100000001);
        rostered["sprk_effectivefrom"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        rostered["sprk_effectiveto"] = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var other = new Entity("sprk_timekeeper", Guid.NewGuid());
        other["sprk_name"] = "Pat Smith";
        other["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        h.Seed(rostered, other);
        var bytes = Workbook(
            new[] { Tk("jane doe") },                                   // on the sheet AND on the roster: reused
            new[] { Rate("Jane Doe", null), Rate("Pat Smith", null) });  // Pat is on the roster only

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        h.Created.Where(e => e.LogicalName == "sprk_timekeeper").Should().BeEmpty();
        outcome.Value!.TimekeepersReused.Should().Be(1);
        var rateTargets = h.Created.Where(e => e.LogicalName == "sprk_ratecardrate")
            .Select(r => r.GetAttributeValue<EntityReference>("sprk_timekeeper").Id).ToList();
        rateTargets.Should().BeEquivalentTo(new[] { rosterId, other.Id });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Import: invalid rows write nothing
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_WithSeveralInvalidRows_ReportsEachOneWithSheetRowColumnAndMessage_AndWritesNothing()
    {
        var h = new Harness();
        var bytes = Workbook(
            new[]
            {
                Tk("Jane Doe"),
                Tk(null),                                  // row 3: name missing
                Tk("Bad Class", "Wizard"),                 // row 4: unknown classification
                Tk("Bad Date", "Partner", "01/02/2026"),   // row 5: date format
                Tk("Backwards", "Partner", "2026-06-01", "2026-01-01"), // row 6: To before From
                Tk("Bad Mail", "Partner", "2026-01-01", null, "not-an-email"), // row 7
                Tk("No Contact", "Partner", "2026-01-01", null, "nobody@firm.com"), // row 8
            },
            new[]
            {
                Rate("Jane Doe", null),                                  // row 2 fine
                Rate("Ghost", null),                                     // row 3: unknown timekeeper
                Rate(null, null),                                        // row 4: neither
                Rate("Jane Doe", "Partner"),                             // row 5: both
                Rate(null, "Partner", "1200", "1000"),                   // row 6: approved above rack
                Rate(null, "Associate", "abc", "1000"),                  // row 7: not a number
                Rate(null, "Associate", "10.123", "20"),                 // row 8: 3 decimals
                Rate(null, "Associate", "-5", "20"),                     // row 9: negative
                Rate(null, "Analyst", "10", "20", "XXX"),                // row 10: unknown currency
                Rate(null, "Analyst", "10", "20", null),                 // row 11: currency missing
                Rate(null, "Wizard"),                                    // row 12: unknown classification
            });

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        var errors = outcome.Errors;
        bool Has(string sheet, int row, string column, string fragment) =>
            errors.Any(e => e.Sheet == sheet && e.Row == row && e.Column == column && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        Has("Timekeepers", 3, "Name", "required").Should().BeTrue();
        Has("Timekeepers", 4, "Classification", "Wizard").Should().BeTrue();
        Has("Timekeepers", 5, "Effective From", "yyyy-mm-dd").Should().BeTrue();
        Has("Timekeepers", 6, "Effective To", "before").Should().BeTrue();
        Has("Timekeepers", 7, "Contact Email", "valid email").Should().BeTrue();
        Has("Timekeepers", 8, "Contact Email", "No contact").Should().BeTrue();
        Has("Rates", 3, "Timekeeper", "Ghost").Should().BeTrue();
        Has("Rates", 4, "Timekeeper", "either").Should().BeTrue();
        Has("Rates", 5, "Classification", "not both").Should().BeTrue();
        Has("Rates", 6, "Approved Rate", "higher than Rack").Should().BeTrue();
        Has("Rates", 7, "Approved Rate", "not a number").Should().BeTrue();
        Has("Rates", 8, "Approved Rate", "2 decimal").Should().BeTrue();
        Has("Rates", 9, "Approved Rate", "between").Should().BeTrue();
        Has("Rates", 10, "Currency", "XXX").Should().BeTrue();
        Has("Rates", 11, "Currency", "required").Should().BeTrue();
        Has("Rates", 12, "Classification", "Wizard").Should().BeTrue();

        h.CreatedWrites.Should().Be(0, "an invalid import writes no record, no document and no file");
        h.Updated.Should().BeEmpty();
        h.Ownership.Verify(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_OneBadRowAmongManyGoodOnes_WritesNothing()
    {
        var h = new Harness();
        var rates = Enumerable.Range(0, 20)
            .Select(i => Rate(null, "Paralegal", "100", "120", "USD", $"{2020 + i}-01-01", $"{2020 + i}-12-31"))
            .Append(Rate(null, "Analyst", "300", "250")) // approved above rack
            .ToArray();

        var outcome = await h.Service().ImportAsync(Request(), Workbook(Array.Empty<object?[]>(), rates), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Should().Match<RateCardImportError>(e => e.Row == 22 && e.Column == "Approved Rate");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_ATimekeeperWithNoRate_IsRefused_BecausePublishCouldNeverApproveIt()
    {
        var h = new Harness();
        var bytes = Workbook(new[] { Tk("Jane Doe"), Tk("Lonely") }, new[] { Rate("Jane Doe", null) });

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Should().Match<RateCardImportError>(
            e => e.Sheet == "Timekeepers" && e.Row == 3 && e.Message.Contains("Lonely"));
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_UnknownFirmOrCoreRecord_IsReportedAndWritesNothing()
    {
        var h = new Harness();

        var outcome = await h.Service().ImportAsync(
            Request(new RateCardCoreRecord("sprk_matter", Guid.NewGuid()), firm: Guid.NewGuid()), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Select(e => e.Column).Should().Contain(new[] { "firmId", "coreRecord" });
        h.CreatedWrites.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Overlap, at import
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2026-01-01", "2026-06-30", "2026-06-30", "2026-12-31", true)]   // share the boundary day
    [InlineData("2026-01-01", "2026-06-30", "2026-03-01", "2026-04-01", true)]   // contained
    [InlineData("2026-01-01", null, "2030-01-01", "2030-12-31", true)]           // open-ended runs into the later range
    [InlineData("2026-01-01", "2026-06-30", "2026-07-01", "2026-12-31", false)]  // adjacent, no shared day
    [InlineData("2026-01-01", "2026-06-30", "2025-01-01", "2025-12-31", false)]  // earlier
    public async Task Import_SameTimekeeper_OverlappingRanges_AreRefusedOnTheLaterRow(string aFrom, string? aTo, string bFrom, string bTo, bool overlaps)
    {
        var h = new Harness();
        var bytes = Workbook(
            new[] { Tk("Jane Doe") },
            new[] { Rate("Jane Doe", null, "800", "1000", "USD", aFrom, aTo), Rate("Jane Doe", null, "800", "1000", "USD", bFrom, bTo) });

        var outcome = await h.Service().ImportAsync(Request(), bytes, default);

        if (overlaps)
        {
            outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
            outcome.Errors.Should().ContainSingle().Which.Should().Match<RateCardImportError>(
                e => e.Sheet == "Rates" && e.Row == 3 && e.Message.Contains("overlap row 2"));
            h.CreatedWrites.Should().Be(0);
        }
        else
        {
            outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        }
    }

    [Fact]
    public async Task Import_SameClassification_Overlapping_IsRefused_ButDifferentTimekeepersAndClassificationsAreIndependent()
    {
        var h = new Harness();
        var overlapping = Workbook(Array.Empty<object?[]>(), new[] { Rate(null, "Paralegal"), Rate(null, "PARALG", "100", "120", "USD", "2026-06-01", "2027-01-01") });
        var independent = Workbook(
            new[] { Tk("Jane Doe"), Tk("John Roe") },
            new[] { Rate("Jane Doe", null), Rate("John Roe", null), Rate(null, "Paralegal"), Rate(null, "Analyst") });

        (await h.Service().ImportAsync(Request(), overlapping, default)).Kind.Should().Be(RateCardOutcomeKind.Invalid);
        h.CreatedWrites.Should().Be(0);
        (await h.Service().ImportAsync(Request(), independent, default)).Kind.Should().Be(RateCardOutcomeKind.Done);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Import: refusals and failure
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_WhenTheProcedureLibraryIsNotConfigured_IsRefusedWith503_AndWritesNothing()
    {
        var h = new Harness();
        h.Options.ProcedureLibraryContainerId = string.Empty;

        var outcome = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be(RateCardImportService.CodeContainerNotConfigured);
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task Import_WhenNoOwnerResolves_IsRefusedBeforeAnythingIsWritten()
    {
        var h = new Harness();
        h.OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team");

        var outcome = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.OwnerRefused);
        outcome.OwnerRefusal!.RefusalCode.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        h.CreatedWrites.Should().Be(0, "no file is uploaded and no row is written when the owner cannot be resolved");
    }

    [Fact]
    public async Task Import_WhenAWriteFailsPartWay_RemovesEverythingItCreated_IncludingTheUploadedFile()
    {
        var h = new Harness();
        var rateWrites = 0;
        h.FailCreateWhen = e => e.LogicalName == "sprk_ratecardrate" && ++rateWrites == 2;

        var outcome = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be(RateCardImportService.CodeImportFailed);
        outcome.StatusCode.Should().Be(500);
        h.Created.Count.Should().Be(2 + 1 + 1, "two timekeepers, the card and the first rate had been written");
        h.Deleted.Select(d => d.Id).Should().BeEquivalentTo(h.Created.Select(e => e.Id).Append(DocumentId));
        h.Deleted.First().Entity.Should().Be("sprk_ratecardrate", "cleanup runs in reverse order");
        h.FileDeletes.Should().ContainSingle().Which.Should().Be((Drive, "item-1"));
        h.Rows["sprk_ratecard"].Should().BeEmpty();
        h.Rows["sprk_timekeeper"].Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WhenTheRequestIsCancelledPartWay_RemovesWhatItCreated_AndStillCancels()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        h.FailCreateWhen = e =>
        {
            if (e.LogicalName != "sprk_ratecard") return false;
            cts.Cancel(); // the client went away (a cancellation that is not the caller's goes to ratecard.import_failed)
            return true;
        };
        h.CreateFailure = new OperationCanceledException();

        var act = () => h.Service().ImportAsync(Request(), ValidWorkbook(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Rows["sprk_timekeeper"].Should().BeEmpty("the timekeepers written before the cancel are removed");
        h.FileDeletes.Should().ContainSingle("the uploaded workbook is removed");
        h.Deleted.Select(d => d.Entity).Should().Contain("sprk_timekeeper");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════════════════

    internal static Entity Card(Guid id, int status = RateCardImportService.CardDraft)
    {
        var e = new Entity("sprk_ratecard", id);
        e["statuscode"] = new OptionSetValue(status);
        e["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        return e;
    }

    internal static Entity Tkp(Guid id, int status, Guid? firm = null)
    {
        var e = new Entity("sprk_timekeeper", id);
        e["statuscode"] = new OptionSetValue(status);
        e["sprk_firm"] = new EntityReference("sprk_organization", firm ?? FirmId);
        return e;
    }

    internal static Entity StoredRate(Guid cardId, string name, Guid? timekeeper, int? classification, DateTime? from, DateTime? to)
    {
        var e = new Entity("sprk_ratecardrate", Guid.NewGuid());
        e["sprk_name"] = name;
        e["sprk_ratecard"] = new EntityReference("sprk_ratecard", cardId);
        if (timekeeper is { } t) e["sprk_timekeeper"] = new EntityReference("sprk_timekeeper", t);
        if (classification is { } c) e["sprk_classification"] = new OptionSetValue(c);
        if (from is { } f) e["sprk_effectivefrom"] = f;
        if (to is { } x) e["sprk_effectiveto"] = x;
        return e;
    }

    [Fact]
    public async Task Publish_MovesTheCardToPublished_AndItsPendingTimekeepersToApproved_CardLast()
    {
        var h = new Harness();
        var cardId = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var approved = Guid.NewGuid();
        var otherFirmPending = Guid.NewGuid();
        var unrelatedPending = Guid.NewGuid();
        h.Seed(Card(cardId),
            Tkp(pending, RateCardImportService.TimekeeperPending),
            Tkp(approved, RateCardImportService.TimekeeperApproved),
            Tkp(otherFirmPending, RateCardImportService.TimekeeperPending, firm: Guid.NewGuid()),
            Tkp(unrelatedPending, RateCardImportService.TimekeeperPending),
            StoredRate(cardId, "A", pending, null, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)),
            StoredRate(cardId, "B", approved, null, new DateTime(2026, 1, 1), null),
            StoredRate(cardId, "C", otherFirmPending, null, new DateTime(2026, 1, 1), null),
            StoredRate(cardId, "D", null, 100000004, new DateTime(2026, 1, 1), null));

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        outcome.Value!.Status.Should().Be("Published");
        outcome.Value.TimekeepersApproved.Should().Be(1);
        var bulk = h.BulkUpdated.Should().ContainSingle().Subject;
        bulk.Entity.Should().Be("sprk_timekeeper");
        bulk.Updates.Select(u => u.id).Should().Equal(pending);
        ((OptionSetValue)bulk.Updates[0].fields["statuscode"]).Value.Should().Be(RateCardImportService.TimekeeperApproved);
        var update = h.Updated.Should().ContainSingle().Subject;
        update.Entity.Should().Be("sprk_ratecard");
        update.Id.Should().Be(cardId);
        ((OptionSetValue)update.Fields["statuscode"]).Value.Should().Be(RateCardImportService.CardPublished);
    }

    [Fact]
    public async Task Publish_RefusesRangesOverlappingAfterAFormEdit_AndChangesNothing()
    {
        var h = new Harness();
        var cardId = Guid.NewGuid();
        var jane = Guid.NewGuid();
        h.Seed(Card(cardId), Tkp(jane, RateCardImportService.TimekeeperPending),
            StoredRate(cardId, "Jane H1", jane, null, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30)),
            StoredRate(cardId, "Jane H2 (edited on the form)", jane, null, new DateTime(2026, 6, 1), new DateTime(2026, 12, 31)));

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("overlaps");
        h.Updated.Should().BeEmpty();
        h.BulkUpdated.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenTheRateReadIsTruncated_IsRefusedAndChangesNothing()
    {
        var h = new Harness();
        var cardId = Guid.NewGuid();
        var jane = Guid.NewGuid();
        h.Seed(Card(cardId), Tkp(jane, RateCardImportService.TimekeeperPending),
            StoredRate(cardId, "Jane H1", jane, null, new DateTime(2026, 1, 1), null));
        h.TruncatedEntity = "sprk_ratecardrate";

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be(RateCardImportService.CodeResultTruncated);
        outcome.StatusCode.Should().Be(422);
        h.Updated.Should().BeEmpty();
        h.BulkUpdated.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WhenTheFirmRosterReadIsTruncated_IsRefusedAndWritesNothing()
    {
        var h = new Harness();
        h.TruncatedEntity = "sprk_timekeeper";

        var outcome = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be(RateCardImportService.CodeResultTruncated);
        h.Created.Should().BeEmpty();
        h.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_RefusesOverlappingClassificationRates_AndRowsMissingTheirDates()
    {
        var h = new Harness();
        var cardId = Guid.NewGuid();
        h.Seed(Card(cardId),
            StoredRate(cardId, "P1", null, 100000004, new DateTime(2026, 1, 1), null),
            StoredRate(cardId, "P2", null, 100000004, new DateTime(2027, 1, 1), null),
            StoredRate(cardId, "NoDates", null, 100000003, null, null),
            StoredRate(cardId, "Nobody", null, null, new DateTime(2026, 1, 1), null),
            StoredRate(cardId, "Backwards", null, 100000006, new DateTime(2026, 5, 1), new DateTime(2026, 1, 1)));

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Select(e => e.Message).Should().Contain(m => m.Contains("P2") && m.Contains("overlaps"))
            .And.Contain(m => m.Contains("NoDates") && m.Contains("no Effective From"))
            .And.Contain(m => m.Contains("Nobody") && m.Contains("neither"))
            .And.Contain(m => m.Contains("Backwards") && m.Contains("ends before"));
        h.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_OfAPublishedCard_IsRefusedWith409_AndOfAMissingCardWith404()
    {
        var h = new Harness();
        var published = Guid.NewGuid();
        h.Seed(Card(published, RateCardImportService.CardPublished));

        var already = await h.Service().PublishAsync(published, default);
        var missing = await h.Service().PublishAsync(Guid.NewGuid(), default);

        already.Kind.Should().Be(RateCardOutcomeKind.Refused);
        already.StatusCode.Should().Be(409);
        already.ErrorCode.Should().Be(RateCardImportService.CodeNotDraft);
        missing.StatusCode.Should().Be(404);
        missing.ErrorCode.Should().Be(RateCardImportService.CodeNotFound);
        h.Updated.Should().BeEmpty();
        h.BulkUpdated.Should().BeEmpty();
    }

    [Fact]
    public async Task EndToEnd_ImportThenPublish_NothingIsInForceUntilPublish()
    {
        var h = new Harness();
        var imported = await h.Service().ImportAsync(Request(), ValidWorkbook(), default);
        imported.Kind.Should().Be(RateCardOutcomeKind.Done);
        h.Created.Where(e => e.LogicalName == "sprk_ratecard")
            .Should().OnlyContain(c => c.GetAttributeValue<OptionSetValue>("statuscode").Value == RateCardImportService.CardDraft);

        var published = await h.Service().PublishAsync(imported.Value!.RateCardId, default);

        published.Kind.Should().Be(RateCardOutcomeKind.Done);
        published.Value!.TimekeepersApproved.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Pure pieces
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Classifications_AreTheTwentyLedesOptions_WithDistinctValuesAndCodes()
    {
        TimekeeperClassifications.All.Should().HaveCount(20);
        TimekeeperClassifications.All.Select(c => c.Value).Should().OnlyHaveUniqueItems();
        TimekeeperClassifications.All.Select(c => c.Code).Should().OnlyHaveUniqueItems();
        TimekeeperClassifications.TryParse(" partner ", out var partner).Should().BeTrue();
        partner.Should().Be(100000001);
        TimekeeperClassifications.TryParse("Senior Partner", out _).Should().BeFalse("not a LEDES classification since D-84");
    }

    [Theory]
    [InlineData("2026-03-04", true)]
    [InlineData("46086", true)]      // an Excel date serial
    [InlineData("2026-3-4x", false)]
    [InlineData("0", false)]
    [InlineData("04/03/2026", false)]
    public void TryParseDate_AcceptsIsoTextAndExcelSerials_Only(string text, bool ok) =>
        RateCardImportService.TryParseDate(text, out _).Should().Be(ok);
}
