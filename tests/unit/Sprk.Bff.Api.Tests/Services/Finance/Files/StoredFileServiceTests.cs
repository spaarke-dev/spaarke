using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Files;

/// <summary>
/// The Finance file store's contracts and service (ebilling-r1 task 090; procedure-and-file-model 4.1-4.3, 9.1): ids, the canonical
/// source version, the checked size, the blob-name rule, staging, BuildRow, the verified flip, the caller upload, the app create, the
/// job read and the scan verdict. Blob, Dataverse and caller rights are fakes (StoredFileFakes.cs); step 0 is the REAL definition over
/// the shared step-0 Dataverse seam (FinanceStepZeroCaller).
/// </summary>
public sealed class StoredFileServiceTests
{
    private static readonly Guid Submission = Guid.Parse("50b00000-0000-4000-8000-000000000001");
    private static readonly Guid InvoiceId = Guid.Parse("1a000000-0000-4000-8000-000000000001");
    private static readonly StoredFileParent Invoice = new(StoredFileParentKind.Invoice, InvoiceId);
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 test invoice");

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static StoredFileRowSpec Spec(Guid? fileId = null, StoredFilePurpose purpose = StoredFilePurpose.InvoicePdf, StoredFileParent? parent = null) => new()
    {
        FileId = fileId ?? Guid.Parse("f1000000-0000-4000-8000-000000000001"),
        Purpose = purpose,
        Parent = parent ?? Invoice,
        Name = "invoice.pdf",
        ContentType = "application/pdf",
    };

    // =============================================================================================================================
    // 088 retype: sizebytes (uac-r2 asked to be pointed here)
    // =============================================================================================================================

    [Theory(DisplayName = "088 retype must-fire: a length of int.MaxValue + 1 or a negative length has no column value (refused, never cast or clamped)")]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData(long.MaxValue)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Size_OutOfRange_IsRefused(long length)
    {
        StoredFileSize.TryToColumnValue(length, out var value).Should().BeFalse();
        value.Should().Be(0);
    }

    [Theory(DisplayName = "088 retype must-not-fire: 0, 1 and int.MaxValue itself are written exactly")]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData((long)int.MaxValue)]
    public void Size_InRange_IsWrittenExactly(long length)
    {
        StoredFileSize.TryToColumnValue(length, out var value).Should().BeTrue();
        ((long)value).Should().Be(length);
    }

    [Fact(DisplayName = "088 retype must-fire: staging a stream of int.MaxValue + 1 bytes is refused before any storage call, and nothing is read")]
    public async Task Stage_OverIntMax_RefusedBeforeAnyStorageCall()
    {
        var rig = new StoredFileRig();
        var stream = new FakeLengthStream((long)int.MaxValue + 1);

        var result = await rig.Service.StageAsync(Spec(), stream, CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.SizeOutOfRange);
        rig.Gateway.StorageCalls.Should().Be(0);
        stream.Reads.Should().Be(0, "the size is checked before hashing");
    }

    [Fact(DisplayName = "088 retype must-fire: a negative remaining length (position past the end) is refused before any storage call")]
    public async Task Stage_NegativeLength_RefusedBeforeAnyStorageCall()
    {
        var rig = new StoredFileRig();
        var stream = new MemoryStream(Pdf) { Position = Pdf.Length + 10 };

        var result = await rig.Service.StageAsync(Spec(), stream, CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.SizeOutOfRange);
        rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "088 retype must-fire: the app create of an over-limit file writes no blob and no row")]
    public async Task CreateForApp_OverIntMax_NoBlobNoRow()
    {
        var rig = new StoredFileRig();

        var result = await rig.Service.CreateForAppAsync(Spec(), new FakeLengthStream((long)int.MaxValue + 1), requestedBy: null, CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.SizeOutOfRange);
        rig.Gateway.Names.Should().BeEmpty();
        rig.Dataverse.Rows.Should().BeEmpty();
        rig.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "088 retype must-not-fire: BuildRow writes int.MaxValue as sprk_sizebytes exactly; one byte more throws (no wrap)")]
    public void BuildRow_SizeAtIntMax_Written_AndOneMoreThrows()
    {
        var rig = new StoredFileRig();
        var spec = Spec();
        var path = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, spec.FileId);

        var create = rig.Service.BuildRow(spec, new StagedBlob("invoice-files", path, Sha(Pdf), int.MaxValue, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), null);
        create.Target[StoredFileSchema.SizeBytes].Should().Be(int.MaxValue);

        var act = () => rig.Service.BuildRow(spec, new StagedBlob("invoice-files", path, Sha(Pdf), (long)int.MaxValue + 1, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "088 retype: no unchecked (int) cast of a size or length exists in the file store's source")]
    public void NoUncheckedIntCastOfASize()
    {
        var dir = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "Files");
        var files = Directory.GetFiles(dir, "*.cs");
        files.Should().NotBeEmpty();
        var cast = new Regex(@"(?<!checked\()\(int\)\s*[\w.]*(size|length|bytes)", RegexOptions.IgnoreCase);
        foreach (var file in files)
        {
            var offenders = File.ReadAllLines(file).Where(l => cast.IsMatch(l)).ToList();
            offenders.Should().BeEmpty($"{Path.GetFileName(file)} must convert sizes only through StoredFileSize.TryToColumnValue");
        }

        // Must-fire control: the pattern sees an unchecked cast and not the checked one.
        cast.IsMatch("var v = (int)length;").Should().BeTrue();
        cast.IsMatch("value = checked((int)length);").Should().BeFalse();
    }

    // =============================================================================================================================
    // 088 retype: the ONE canonical source-version string
    // =============================================================================================================================

    [Theory(DisplayName = "088 retype: sprk_sourceversion and the string ForSourceVersion hashes are the same string, under invariant, de-DE and ar-SA")]
    [InlineData("")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void SourceVersion_StoredEqualsHashed_UnderAnyCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var info = string.IsNullOrEmpty(culture) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;
            const long version = 1234567890123;

            var rig = new StoredFileRig();
            var fileId = StoredFileIds.ForSourceVersion(Submission, version);
            var spec = Spec(fileId) with { SourceDocumentId = Guid.NewGuid(), SourceVersion = version };
            var path = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, fileId);
            var stored = (string)rig.Service.BuildRow(spec, new StagedBlob("invoice-files", path, Sha(Pdf), Pdf.Length, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), null)
                .Target[StoredFileSchema.SourceVersion];

            stored.Should().Be("1234567890123", "invariant digits, no group separator, no padding");
            stored.Should().Be(StoredFileSourceVersion.ToCanonical(version));
            fileId.Should().Be(InvoiceSubmissionKey($"ebilling-storedfile-version-{Submission:D}-{stored}"),
                "the id is derived from exactly the stored string");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory(DisplayName = "088 retype: a row version that does not fit 20 characters, or is not plain digits, is refused")]
    [InlineData("123456789012345678901")] // 21 digits
    [InlineData("99999999999999999999")] // 20 digits, over long
    [InlineData("-5")]
    [InlineData("1,234")]
    [InlineData(" 12")]
    [InlineData("")]
    [InlineData(null)]
    public void RowVersion_Unusable_IsRefused(string? raw) =>
        StoredFileSourceVersion.TryParseRowVersion(raw, out _).Should().BeFalse();

    [Fact(DisplayName = "088 retype: a row version string with leading zeros reads to the canonical string (no padding); negatives throw")]
    public void RowVersion_LeadingZeros_Canonicalised()
    {
        StoredFileSourceVersion.TryParseRowVersion("000123", out var v).Should().BeTrue();
        StoredFileSourceVersion.ToCanonical(v).Should().Be("123");
        StoredFileSourceVersion.ToCanonical(long.MaxValue).Length.Should().BeLessThanOrEqualTo(StoredFileSourceVersion.MaxLength);
        ((Action)(() => StoredFileSourceVersion.ToCanonical(-1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // =============================================================================================================================
    // Ids
    // =============================================================================================================================

    [Fact(DisplayName = "StoredFileIds are deterministic and distinct across the three kinds for one submission; New() is fresh")]
    public void Ids_DeterministicAndDistinct()
    {
        var version = StoredFileIds.ForSourceVersion(Submission, 0);
        var attachment = StoredFileIds.ForAttachment(Submission, "0");
        var upload = StoredFileIds.ForUpload(Submission);

        StoredFileIds.ForSourceVersion(Submission, 0).Should().Be(version);
        StoredFileIds.ForAttachment(Submission, "0").Should().Be(attachment);
        StoredFileIds.ForUpload(Submission).Should().Be(upload);
        new[] { version, attachment, upload }.Should().OnlyHaveUniqueItems();
        StoredFileIds.ForUpload(Guid.NewGuid()).Should().NotBe(upload);
        StoredFileIds.New().Should().NotBe(StoredFileIds.New());
        ((Action)(() => StoredFileIds.ForUpload(Guid.Empty))).Should().Throw<ArgumentException>();
        ((Action)(() => StoredFileIds.ForAttachment(Submission, " "))).Should().Throw<ArgumentException>();
    }

    // =============================================================================================================================
    // Name rule
    // =============================================================================================================================

    [Theory(DisplayName = "Name rule: a separator, '..', a non-GUID or non-canonical id, an unknown table, or another tenant is refused")]
    [InlineData("{T}/sprk_invoice/1a000000-0000-4000-8000-000000000001/../f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/sprk_invoice/../f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/sprk_invoice/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001/x")]
    [InlineData("{T}/sprk_invoice/not-a-guid/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/sprk_invoice/1A000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/sprk_invoice/{1a000000-0000-4000-8000-000000000001}/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/sprk_matter/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}/SPRK_INVOICE/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}//1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("/{T}/sprk_invoice/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("{T}\\sprk_invoice\\1a000000-0000-4000-8000-000000000001\\f1000000-0000-4000-8000-000000000001")]
    [InlineData("00000000-0000-0000-0000-000000000000/sprk_invoice/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    [InlineData("0ff1e000-0000-4000-8000-000000000999/sprk_invoice/1a000000-0000-4000-8000-000000000001/f1000000-0000-4000-8000-000000000001")]
    public void NameRule_Refuses(string template)
    {
        var name = template.Replace("{T}", StoredFileRig.Tenant.ToString("D"), StringComparison.Ordinal);
        StoredFileBlobName.TryParseForTenant(name, StoredFileRig.Tenant, out _, out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Name rule must-not-fire: a built name parses back to its tenant, parent and file")]
    public void NameRule_RoundTrips()
    {
        var file = Guid.NewGuid();
        var name = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file);

        name.Should().Be($"{StoredFileRig.Tenant:D}/sprk_invoice/{InvoiceId:D}/{file:D}");
        StoredFileBlobName.TryParseForTenant(name, StoredFileRig.Tenant, out var parent, out var parsed).Should().BeTrue();
        parent.Should().Be(Invoice);
        parsed.Should().Be(file);
    }

    [Fact(DisplayName = "Entity set names are the live ones (never pluralized)")]
    public void EntitySets_AreTheLiveOnes()
    {
        StoredFileSchema.EntitySet.Should().Be("sprk_storedfiles");
        new StoredFileParent(StoredFileParentKind.ProcedureVersion, Guid.NewGuid()).EntitySet.Should().Be("sprk_procedureversions");
        new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()).EntitySet.Should().Be("sprk_invoices");
        new StoredFileParent(StoredFileParentKind.BillingExport, Guid.NewGuid()).EntitySet.Should().Be("sprk_billingexports");
        StoredFileSchema.DocumentEntitySet.Should().Be("sprk_documents");
    }

    // =============================================================================================================================
    // Stage
    // =============================================================================================================================

    [Theory(DisplayName = "Stage writes to the purpose's container at the stated name, with sha256, purpose and submission metadata")]
    [InlineData(StoredFilePurpose.ProcedureSource, StoredFileParentKind.ProcedureVersion, "procedures", "sprk_procedureversion")]
    [InlineData(StoredFilePurpose.InvoicePdf, StoredFileParentKind.Invoice, "invoice-files", "sprk_invoice")]
    [InlineData(StoredFilePurpose.InvoiceSupportingFile, StoredFileParentKind.Invoice, "invoice-files", "sprk_invoice")]
    [InlineData(StoredFilePurpose.ExportFile, StoredFileParentKind.BillingExport, "billing-exports", "sprk_billingexport")]
    public async Task Stage_WritesTheStatedName(StoredFilePurpose purpose, StoredFileParentKind kind, string container, string table)
    {
        var rig = new StoredFileRig();
        var parent = new StoredFileParent(kind, InvoiceId);
        var spec = Spec(purpose: purpose, parent: parent) with { SubmissionId = Submission };

        var result = await rig.Service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None);

        var expected = $"{StoredFileRig.Tenant:D}/{table}/{InvoiceId:D}/{spec.FileId:D}";
        result.Blob.Should().BeEquivalentTo(new StagedBlob(container, expected, Sha(Pdf), Pdf.Length, false));
        var blob = rig.Gateway.Get(container, expected)!;
        blob.Content.Should().Equal(Pdf);
        blob.Metadata.Should().Contain("sha256", Sha(Pdf)).And.Contain("purpose", ((int)purpose).ToString(CultureInfo.InvariantCulture))
            .And.Contain("submissionid", Submission.ToString("D")).And.Contain("environment", StoredFileBlobs.TestEnvironment);
        blob.Tags.Should().BeEmpty("the store never writes index tags");
    }

    [Theory(DisplayName = "The environment marker is the lower-case Dataverse host; an unusable URL gives none (no row-less blob is then ever deleted)")]
    [InlineData("https://SpaarkeDev1.crm.dynamics.com/", "spaarkedev1.crm.dynamics.com")]
    [InlineData(" https://org.crm4.dynamics.com ", "org.crm4.dynamics.com")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not a url", null)]
    public void EnvironmentMarker(string? url, string? expected) =>
        StoredFileBlobs.EnvironmentMarkerOf(url).Should().Be(expected);

    [Fact(DisplayName = "Stage: a purpose under the wrong parent is refused before any storage call")]
    public async Task Stage_WrongParent_Refused()
    {
        var rig = new StoredFileRig();

        var result = await rig.Service.StageAsync(Spec(purpose: StoredFilePurpose.ProcedureSource), new MemoryStream(Pdf), CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.InvalidRequest);
        rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Stage: re-staging the same bytes reuses the blob; different bytes under the id are refused and the blob is unchanged")]
    public async Task Stage_WriteOnce()
    {
        var rig = new StoredFileRig();
        var spec = Spec();
        (await rig.Service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None)).Blob!.Reused.Should().BeFalse();

        var again = await rig.Service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None);
        again.Blob!.Reused.Should().BeTrue();

        var other = Encoding.UTF8.GetBytes("different bytes");
        var conflict = await rig.Service.StageAsync(spec, new MemoryStream(other), CancellationToken.None);
        conflict.RefusalCode.Should().Be(StoredFileRefusal.ContentConflict);
        rig.Gateway.Get("invoice-files", again.Blob.BlobPath)!.Content.Should().Equal(Pdf);
    }

    [Fact(DisplayName = "Stage: a non-seekable stream is buffered and staged; not configured refuses with no call")]
    public async Task Stage_NonSeekable_AndNotConfigured()
    {
        var rig = new StoredFileRig();
        var result = await rig.Service.StageAsync(Spec(), new NonSeekable(Pdf), CancellationToken.None);
        result.Blob!.Sha256.Should().Be(Sha(Pdf));
        result.Blob.SizeBytes.Should().Be(Pdf.Length);

        var off = new StoredFileRig(configured: false);
        (await off.Service.StageAsync(Spec(), new MemoryStream(Pdf), CancellationToken.None)).RefusalCode.Should().Be(StoredFileRefusal.NotConfigured);
        off.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Stage: a firm-visible copy (a source set) is refused; a firm-visible supporting file too")]
    public async Task Stage_FirmVisibleRule()
    {
        var rig = new StoredFileRig();
        (await rig.Service.StageAsync(Spec() with { FirmVisible = true, SourceDocumentId = Guid.NewGuid() }, new MemoryStream(Pdf), CancellationToken.None))
            .RefusalCode.Should().Be(StoredFileRefusal.InvalidRequest);
        (await rig.Service.StageAsync(Spec(purpose: StoredFilePurpose.InvoiceSupportingFile) with { FirmVisible = true }, new MemoryStream(Pdf), CancellationToken.None))
            .RefusalCode.Should().Be(StoredFileRefusal.InvalidRequest);
        (await rig.Service.StageAsync(Spec() with { FirmVisible = true }, new MemoryStream(Pdf), CancellationToken.None)).IsStaged.Should().BeTrue();
        rig.Gateway.Names.Should().HaveCount(1);
    }

    // =============================================================================================================================
    // BuildRow
    // =============================================================================================================================

    [Fact(DisplayName = "BuildRow sets Not yet servable and every authorization column, the owner and the creator person")]
    public void BuildRow_SetsEveryAuthorizationColumn()
    {
        var rig = new StoredFileRig();
        var doc = Guid.NewGuid();
        var comm = Guid.NewGuid();
        var person = Guid.NewGuid();
        var spec = Spec() with
        {
            SourceDocumentId = doc, SourceCommunicationId = comm, SourceAttachmentIdentity = "part-2", SourceVersion = 77, SourceSha256 = Sha(Pdf),
        };
        var path = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, spec.FileId);

        var row = rig.Service.BuildRow(spec, new StagedBlob("invoice-files", path, Sha(Pdf), Pdf.Length, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), person).Target;

        row.LogicalName.Should().Be("sprk_storedfile");
        row.Id.Should().Be(spec.FileId);
        row.GetAttributeValue<OptionSetValue>("sprk_filestatus").Value.Should().Be(100000000);
        row.GetAttributeValue<OptionSetValue>("sprk_purpose").Value.Should().Be(100000001);
        row.GetAttributeValue<bool>("sprk_firmvisible").Should().BeFalse();
        row.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeEquivalentTo(new EntityReference("sprk_invoice", InvoiceId));
        row.Contains("sprk_procedureversion").Should().BeFalse();
        row.Contains("sprk_billingexport").Should().BeFalse();
        row.GetAttributeValue<EntityReference>("sprk_sourcedocument").Id.Should().Be(doc);
        row.GetAttributeValue<EntityReference>("sprk_sourcecommunication").Id.Should().Be(comm);
        row["sprk_sourceattachmentidentity"].Should().Be("part-2");
        row["sprk_sourceversion"].Should().Be("77");
        row["sprk_sourcesha256"].Should().Be(Sha(Pdf));
        row["sprk_sha256"].Should().Be(Sha(Pdf));
        row["sprk_contenttype"].Should().Be("application/pdf");
        row["sprk_sizebytes"].Should().Be(Pdf.Length);
        row["sprk_blobpath"].Should().Be(path);
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", StoredFileRig.TeamId));
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(person);
    }

    [Fact(DisplayName = "BuildRow refuses a refused owner and a blob that is not the spec's")]
    public void BuildRow_Refuses()
    {
        var rig = new StoredFileRig();
        var spec = Spec();
        var path = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, spec.FileId);

        ((Action)(() => rig.Service.BuildRow(spec, new StagedBlob("invoice-files", path, Sha(Pdf), 1, false), RecordOwnerResolution.Refused("x", "y"), null)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => rig.Service.BuildRow(spec, new StagedBlob("procedures", path, Sha(Pdf), 1, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), null)))
            .Should().Throw<ArgumentException>();
        var otherFile = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, Guid.NewGuid());
        ((Action)(() => rig.Service.BuildRow(spec, new StagedBlob("invoice-files", otherFile, Sha(Pdf), 1, false), RecordOwnerResolution.Owned(StoredFileRig.TeamId), null)))
            .Should().Throw<ArgumentException>();
    }

    // =============================================================================================================================
    // Flip
    // =============================================================================================================================

    [Fact(DisplayName = "Flip: present and equal → one IfRowVersionMatches update to Servable")]
    public async Task Flip_PresentAndEqual_Flips()
    {
        var rig = new StoredFileRig();
        var file = Guid.NewGuid();
        var version = rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable).RowVersion;

        var result = await rig.Service.FlipToServableAsync(file, CancellationToken.None);

        result.Outcome.Should().Be(StoredFileFlipOutcome.Flipped);
        rig.Dataverse.Rows[file].GetAttributeValue<OptionSetValue>("sprk_filestatus").Value.Should().Be(100000001);
        rig.Dataverse.Mock.Verify(e => e.UpdateIfRowVersionMatchesAsync("sprk_storedfile", file, version, It.Is<Dictionary<string, object>>(f => f.Count == 1), It.IsAny<CancellationToken>()), Moq.Times.Once);
    }

    [Fact(DisplayName = "Flip: missing blob → definitive, no update")]
    public async Task Flip_Missing_Definitive()
    {
        var rig = new StoredFileRig();
        var file = Guid.NewGuid();
        rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable);
        await rig.Gateway.DeleteIfExistsAsync("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file), CancellationToken.None);

        var result = await rig.Service.FlipToServableAsync(file, CancellationToken.None);

        result.Should().Be(new StoredFileFlipResult(StoredFileFlipOutcome.Definitive, StoredFileRefusal.BlobMissing));
        rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("update", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Flip: SHA different → definitive, no update")]
    public async Task Flip_Mismatch_Definitive()
    {
        var rig = new StoredFileRig();
        var file = Guid.NewGuid();
        rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable);
        rig.Gateway.Overwrite("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file), Encoding.UTF8.GetBytes("tampered"), rig.Gateway.Now);

        var result = await rig.Service.FlipToServableAsync(file, CancellationToken.None);

        result.Should().Be(new StoredFileFlipResult(StoredFileFlipOutcome.Definitive, StoredFileRefusal.HashMismatch));
        rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("update", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Flip: row version changed → concurrency result, nothing written")]
    public async Task Flip_Concurrency()
    {
        var rig = new StoredFileRig();
        var file = Guid.NewGuid();
        rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable);
        rig.Dataverse.BumpVersionBeforeNextUpdate = true;

        var result = await rig.Service.FlipToServableAsync(file, CancellationToken.None);

        result.Outcome.Should().Be(StoredFileFlipOutcome.Concurrency);
        rig.Dataverse.Rows[file].GetAttributeValue<OptionSetValue>("sprk_filestatus").Value.Should().Be(100000000);
    }

    [Fact(DisplayName = "Flip: a storage fault throws (the caller retries)")]
    public async Task Flip_StorageFault_Throws()
    {
        var rig = new StoredFileRig();
        var file = Guid.NewGuid();
        rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable);
        rig.Gateway.FailEverything = new RequestFailedException(500, "storage down");

        var act = () => rig.Service.FlipToServableAsync(file, CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
        rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("update", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Flip: a path that is not the row's own, a Removed row and an unknown row are definitive, no storage read; Servable is a no-op")]
    public async Task Flip_NotFlippable()
    {
        var rig = new StoredFileRig();
        var foreign = Guid.NewGuid();
        rig.SeedFile(foreign, Invoice, Pdf, StoredFileStatus.NotYetServable,
            blobPathOverride: StoredFileBlobName.Build(StoredFileRig.Tenant, new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), foreign));
        var removed = Guid.NewGuid();
        rig.SeedFile(removed, Invoice, Pdf, StoredFileStatus.Removed);
        var servable = Guid.NewGuid();
        rig.SeedFile(servable, Invoice, Pdf, StoredFileStatus.Servable);
        var before = rig.Gateway.StorageCalls;

        (await rig.Service.FlipToServableAsync(foreign, CancellationToken.None)).RefusalCode.Should().Be(StoredFileRefusal.RowNotFlippable);
        (await rig.Service.FlipToServableAsync(removed, CancellationToken.None)).RefusalCode.Should().Be(StoredFileRefusal.RowNotFlippable);
        (await rig.Service.FlipToServableAsync(Guid.NewGuid(), CancellationToken.None)).RefusalCode.Should().Be(StoredFileRefusal.RowNotFlippable);
        (await rig.Service.FlipToServableAsync(servable, CancellationToken.None)).Outcome.Should().Be(StoredFileFlipOutcome.AlreadyServable);
        rig.Gateway.StorageCalls.Should().Be(before);
        rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("update", StringComparison.Ordinal));
    }

    // =============================================================================================================================
    // Upload and app create
    // =============================================================================================================================

    [Theory(DisplayName = "Upload must-fire: a caller step 0 refuses is refused before any read or write")]
    [InlineData(StepZeroCallerKind.FlagTrue)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow)]
    [InlineData(StepZeroCallerKind.FlagReadFault)]
    [InlineData(StepZeroCallerKind.LookupFault)]
    public async Task Upload_StepZeroRefuses_BeforeAnything(StepZeroCallerKind kind)
    {
        var rig = new StoredFileRig(stepZero: kind);
        rig.Probe.Grant("sprk_invoices", InvoiceId, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write);

        var result = await rig.Service.UploadForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.InvoiceSupportingFile, Invoice, "a.pdf", "application/pdf", new MemoryStream(Pdf), CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.CallerNotInternal);
        rig.Probe.Calls.Should().BeEmpty();
        rig.Gateway.StorageCalls.Should().Be(0);
        rig.Dataverse.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Upload must-fire: no Write on the parent → refused, no blob, no row")]
    public async Task Upload_NoWrite_Refused()
    {
        var rig = new StoredFileRig();
        rig.Probe.Grant("sprk_invoices", InvoiceId, Spaarke.Dataverse.AccessRights.Read);

        var result = await rig.Service.UploadForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.InvoiceSupportingFile, Invoice, "a.pdf", "application/pdf", new MemoryStream(Pdf), CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.NoWriteOnParent);
        rig.Probe.Calls.Should().ContainSingle().Which.Should().Be(("sprk_invoices", InvoiceId, "token"));
        rig.Gateway.StorageCalls.Should().Be(0);
        rig.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "Upload must-fire: a failed row write deletes the staged blob and rethrows")]
    public async Task Upload_RowWriteFails_BlobDeleted()
    {
        var rig = new StoredFileRig();
        rig.Probe.Grant("sprk_invoices", InvoiceId, Spaarke.Dataverse.AccessRights.Write);
        rig.Dataverse.CreateFault = new InvalidOperationException("Dataverse refused the create");

        var act = () => rig.Service.UploadForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.InvoiceSupportingFile, Invoice, "a.pdf", "application/pdf", new MemoryStream(Pdf), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        rig.Gateway.Names.Should().BeEmpty();
        rig.Gateway.Calls.Should().Contain(c => c.StartsWith("delete invoice-files/", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Upload must-not-fire: an internal caller with Write gets a Servable, not firm-visible row owned through the resolver, stamped with the caller")]
    public async Task Upload_Succeeds()
    {
        var rig = new StoredFileRig();
        rig.Probe.Grant("sprk_invoices", InvoiceId, Spaarke.Dataverse.AccessRights.Write);

        var result = await rig.Service.UploadForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.InvoiceSupportingFile, Invoice, "a.pdf", "application/pdf", new MemoryStream(Pdf), CancellationToken.None);

        result.IsCreated.Should().BeTrue();
        var row = rig.Dataverse.Rows[result.FileId!.Value];
        row.GetAttributeValue<OptionSetValue>("sprk_filestatus").Value.Should().Be(100000001);
        row.GetAttributeValue<bool>("sprk_firmvisible").Should().BeFalse();
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(FinanceStepZeroCaller.SystemUserId);
        rig.Owners.Verify(o => o.ResolveOwnerAsync(It.Is<RecordOwnershipContext>(c => c.TargetEntityLogicalName == "sprk_invoice" && c.TargetRecordId == InvoiceId), It.IsAny<CancellationToken>()), Moq.Times.Once);
    }

    [Fact(DisplayName = "App create: stage → row → flip; a retry with the same deterministic id completes without a second row or blob")]
    public async Task CreateForApp_RetryIsIdempotent()
    {
        var rig = new StoredFileRig();
        var spec = Spec(StoredFileIds.ForUpload(Submission)) with { SubmissionId = Submission };

        (await rig.Service.CreateForAppAsync(spec, new MemoryStream(Pdf), requestedBy: null, CancellationToken.None)).IsCreated.Should().BeTrue();

        // The retry meets the row (create answers "exists") and the blob (reused).
        rig.Dataverse.CreateFault = new InvalidOperationException("duplicate key");
        (await rig.Service.CreateForAppAsync(spec, new MemoryStream(Pdf), requestedBy: null, CancellationToken.None)).IsCreated.Should().BeTrue();
        rig.Dataverse.Rows.Should().HaveCount(1);
        rig.Gateway.Names.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Must-fire (review F3): a failed create whose re-read ALSO faults keeps the blob (the row may have committed)")]
    public async Task CreateFails_RereadFaults_BlobKept()
    {
        var rig = new StoredFileRig();
        rig.Dataverse.CreateFault = new TimeoutException("response lost");
        rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");
        var spec = Spec();

        var act = () => rig.Service.CreateForAppAsync(spec, new MemoryStream(Pdf), requestedBy: null, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        rig.Gateway.Get("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, spec.FileId)).Should().NotBeNull();
        rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Must-fire (review F3): a failed create over a REUSED blob (another attempt's) never deletes it")]
    public async Task CreateFails_ReusedBlob_Kept()
    {
        var rig = new StoredFileRig();
        var spec = Spec(StoredFileIds.ForUpload(Submission)) with { SubmissionId = Submission };
        (await rig.Service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None)).IsStaged.Should().BeTrue(); // the other attempt
        rig.Dataverse.CreateFault = new TimeoutException("transient");

        var act = () => rig.Service.CreateForAppAsync(spec, new MemoryStream(Pdf), requestedBy: null, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        rig.Gateway.Get("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, spec.FileId)).Should().NotBeNull();
    }

    [Fact(DisplayName = "App create: a refused owner writes nothing")]
    public async Task CreateForApp_OwnerRefused_NothingWritten()
    {
        var rig = new StoredFileRig { OwnerAnswer = RecordOwnerResolution.Refused("record_owner_no_source", "none") };

        var result = await rig.Service.CreateForAppAsync(Spec(), new MemoryStream(Pdf), requestedBy: null, CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.OwnerUnresolved);
        rig.Gateway.StorageCalls.Should().Be(0);
        rig.Dataverse.Writes.Should().Be(0);
    }

    // =============================================================================================================================
    // Job read
    // =============================================================================================================================

    [Fact(DisplayName = "OpenForProcessing: Servable and staged rows with a clean scan return the bytes; Pending, Malicious and Missing are typed")]
    public async Task OpenForProcessing_Outcomes()
    {
        var rig = new StoredFileRig();
        var clean = Guid.NewGuid();
        rig.SeedFile(clean, Invoice, Pdf, StoredFileStatus.Servable);
        var staged = Guid.NewGuid();
        rig.SeedFile(staged, Invoice, Pdf, StoredFileStatus.NotYetServable);
        var pending = Guid.NewGuid();
        rig.SeedFile(pending, Invoice, Pdf, tags: new Dictionary<string, string>());
        var malicious = Guid.NewGuid();
        rig.SeedFile(malicious, Invoice, Pdf, tags: new Dictionary<string, string> { [StoredFileBlobs.ScanResultTagKey] = "Malicious" });
        var removed = Guid.NewGuid();
        rig.SeedFile(removed, Invoice, Pdf, StoredFileStatus.Removed);

        var ok = await rig.Service.OpenForProcessingAsync(clean, CancellationToken.None);
        ok.Outcome.Should().Be(StoredFileProcessingOutcome.Ok);
        ok.Content!.ToArray().Should().Equal(Pdf);
        ok.ContentType.Should().Be("application/pdf");
        (await rig.Service.OpenForProcessingAsync(staged, CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Ok);
        (await rig.Service.OpenForProcessingAsync(pending, CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Pending);
        (await rig.Service.OpenForProcessingAsync(malicious, CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Malicious);
        (await rig.Service.OpenForProcessingAsync(removed, CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Missing);
        (await rig.Service.OpenForProcessingAsync(Guid.NewGuid(), CancellationToken.None)).Outcome.Should().Be(StoredFileProcessingOutcome.Missing);
    }

    // =============================================================================================================================
    // Scan verdict (091 condition 3)
    // =============================================================================================================================

    private static readonly DateTimeOffset Modified = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Scan-time pin (091 condition 3): tagged clean, then Last-Modified later, then refused (Pending)")]
    public void Scan_CleanThenModifiedLater_IsRefused()
    {
        var tags = StoredFileRig.CleanTags(Modified);
        StoredFileBlobs.ClassifyScan(tags, Modified).Should().Be(StoredFileScanState.Clean, "a scan at the content's own time is clean");

        StoredFileBlobs.ClassifyScan(tags, Modified.AddSeconds(1)).Should().Be(StoredFileScanState.Pending,
            "content written after the scan has no verdict yet, whatever the result tag says");
    }

    [Theory(DisplayName = "Scan verdict: absent, other, conflicting, unparseable or missing scan time → Pending; Malicious anywhere → Malicious")]
    [InlineData(null, null, StoredFileScanState.Pending)]
    [InlineData("Not scanned", "2026-10-22T12:05:00Z", StoredFileScanState.Pending)]
    [InlineData("Error", "2026-10-22T12:05:00Z", StoredFileScanState.Pending)]
    [InlineData("SAM259201: Scan timed out", "2026-10-22T12:05:00Z", StoredFileScanState.Pending)]
    [InlineData("no threats found", "2026-10-22T12:05:00Z", StoredFileScanState.Pending)] // values compare exactly
    [InlineData("No threats found", null, StoredFileScanState.Pending)]
    [InlineData("No threats found", "yesterday", StoredFileScanState.Pending)]
    [InlineData("No threats found", "2026-10-22T11:59:59Z", StoredFileScanState.Pending)]
    [InlineData("Malicious", "2026-10-22T12:05:00Z", StoredFileScanState.Malicious)]
    [InlineData("Malicious", null, StoredFileScanState.Malicious)]
    [InlineData("No threats found", "2026-10-22T12:00:00Z", StoredFileScanState.Clean)]
    [InlineData("No threats found", "2026-10-22T12:05:00.1234567Z", StoredFileScanState.Clean)]
    public void Scan_Verdicts(string? result, string? time, StoredFileScanState expected)
    {
        var tags = new Dictionary<string, string>();
        if (result is not null) tags[StoredFileBlobs.ScanResultTagKey] = result;
        if (time is not null) tags[StoredFileBlobs.ScanTimeTagKey] = time;

        StoredFileBlobs.ClassifyScan(tags, Modified).Should().Be(expected);
    }

    [Fact(DisplayName = "Scan verdict: the key is matched ignoring case (Learn and the lab spell it differently); conflicting keys are Pending, Malicious wins")]
    public void Scan_KeyCaseAndConflicts()
    {
        StoredFileBlobs.ClassifyScan(new Dictionary<string, string>
        {
            ["Malware scanning scan result"] = "No threats found",
            ["malware scanning scan time utc"] = "2026-10-22T12:01:00Z",
        }, Modified).Should().Be(StoredFileScanState.Clean);

        StoredFileBlobs.ClassifyScan(new Dictionary<string, string>
        {
            ["Malware scanning scan result"] = "No threats found",
            ["Malware Scanning scan result"] = "Error",
            [StoredFileBlobs.ScanTimeTagKey] = "2026-10-22T12:01:00Z",
        }, Modified).Should().Be(StoredFileScanState.Pending);

        StoredFileBlobs.ClassifyScan(new Dictionary<string, string>
        {
            ["Malware scanning scan result"] = "No threats found",
            ["Malware Scanning scan result"] = "Malicious",
        }, Modified).Should().Be(StoredFileScanState.Malicious);
    }

    [Fact(DisplayName = "Scan verdict: only an explicit MalwareScanningEnabled=false yields NotScanned; absent means enabled")]
    public async Task Scan_OnlyExplicitFalseIsNotScanned()
    {
        foreach (var (setting, expected) in new (bool?, StoredFileScanState)[] { (false, StoredFileScanState.NotScanned), (null, StoredFileScanState.Pending), (true, StoredFileScanState.Pending) })
        {
            var rig = new StoredFileRig(malwareScanningEnabled: setting);
            var file = Guid.NewGuid();
            rig.SeedFile(file, Invoice, Pdf, tags: new Dictionary<string, string>());

            var scan = await rig.Blobs.GetScanStateAsync("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file), CancellationToken.None);

            scan!.Value.State.Should().Be(expected, $"MalwareScanningEnabled = {setting?.ToString() ?? "absent"}");
        }
    }

    // =============================================================================================================================

    private static Guid InvoiceSubmissionKey(string key) => Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.IdOf(key);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }

    private sealed class NonSeekable(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
