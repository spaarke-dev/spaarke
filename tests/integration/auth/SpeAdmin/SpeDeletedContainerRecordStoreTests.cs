using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// #1652 — the deleted-container ownership record store, the ONLY source the SPE admin recycle bin trusts for a deleted
/// container. The real store over an in-memory Cosmos <c>memory</c> container (<see cref="FakeSpeDeletedContainerRecords"/>):
/// a record is written in its own partition with a TTL past SharePoint Embedded's retention, and a read accepts only a
/// complete record of exactly the asked container — a forged, tampered or misplaced document is "no record" (the uniform
/// 404), and a store with no answer throws (the 503), never "no record". ADR-038 §2 path #1 (security-auth KEEP).
/// </summary>
public sealed class SpeDeletedContainerRecordStoreTests
{
    private const string ContainerId = "b!deleted-container";
    private const string TypeT = "77777777-0000-0000-0000-000000000077";
    private static readonly Guid Unit = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid Config = Guid.Parse("ca000000-0000-0000-0000-00000000000a");

    private readonly FakeSpeDeletedContainerRecords _cosmos = new();

    [Fact]
    public async Task Write_StoresTheRecordInItsOwnPartition_WithATtlPastTheRecycleBinsRetention_AndReadsItBack()
    {
        var record = new SpeDeletedContainerRecord(ContainerId, "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow);

        await _cosmos.Store.WriteAsync(record);

        var document = _cosmos.DocumentOf(ContainerId)!;
        document.Should().NotBeNull();
        document.PartitionKey.Should().Be(SpeDeletedContainerRecordStore.PartitionValue,
            "never a tenant id — other memory documents are partitioned by the caller's tid");
        document.DocumentType.Should().Be(SpeDeletedContainerRecordStore.DocumentTypeValue);
        document.Id.Should().Be(SpeDeletedContainerRecordStore.DocumentId(ContainerId)).And.StartWith("spedc-");
        document.Ttl.Should().BeGreaterThan((int)TimeSpan.FromDays(93).TotalSeconds,
            "SharePoint Embedded keeps a deleted container 93 days; the record must outlive it");
        (await _cosmos.Store.ReadAsync(ContainerId)).Should().Be(record);
    }

    public static TheoryData<string> TamperedDocuments() => new()
    {
        "another-containers-record",   // a genuine record of another container copied to this id
        "other-document-type",         // a document of another type at this id
        "no-customer",
        "empty-business-unit",
        "no-config",
        "no-container-type",
        "no-deleted-at",
    };

    [Theory]
    [MemberData(nameof(TamperedDocuments))]
    public async Task Read_OfAForgedOrTamperedDocument_IsNoRecord(string tampering)
    {
        await _cosmos.Store.WriteAsync(new SpeDeletedContainerRecord(ContainerId, "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow));
        await _cosmos.Store.WriteAsync(new SpeDeletedContainerRecord("b!other", "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow));
        var document = _cosmos.DocumentOf(ContainerId)!;
        switch (tampering)
        {
            case "another-containers-record": _cosmos.Put(ContainerId, _cosmos.DocumentOf("b!other")!); break;
            case "other-document-type": document.DocumentType = "workspace-tab"; break;
            case "no-customer": document.CustomerId = " "; break;
            case "empty-business-unit": document.BusinessUnitId = Guid.Empty; break;
            case "no-config": document.ConfigId = null; break;
            case "no-container-type": document.ContainerTypeId = null; break;
            case "no-deleted-at": document.DeletedAt = null; break;
        }

        (await _cosmos.Store.ReadAsync(ContainerId)).Should().BeNull();
    }

    [Fact]
    public async Task Read_OfADocumentOutsideTheRecordPartition_IsNoRecord()
    {
        await _cosmos.Store.WriteAsync(new SpeDeletedContainerRecord(ContainerId, "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow));
        var document = _cosmos.DocumentOf(ContainerId)!;
        _cosmos.Reset();
        _cosmos.Put(ContainerId, document, partition: "[\"11111111-2222-3333-4444-555555555555\"]");

        (await _cosmos.Store.ReadAsync(ContainerId)).Should().BeNull("a record is read only from its own partition");
    }

    [Fact]
    public async Task Read_WhenTheStoreGetsNoAnswer_Throws_NeverNoRecord()
    {
        _cosmos.FaultReads = true;

        var act = () => _cosmos.Store.ReadAsync(ContainerId);

        await act.Should().ThrowAsync<SpeDeletedContainerRecordUnavailableException>();
    }

    [Fact]
    public async Task Write_WhenTheStoreGetsNoAnswer_Throws()
    {
        _cosmos.FaultWrites = true;

        var act = () => _cosmos.Store.WriteAsync(
            new SpeDeletedContainerRecord(ContainerId, "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow));

        await act.Should().ThrowAsync<SpeDeletedContainerRecordUnavailableException>();
        _cosmos.DocumentOf(ContainerId).Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesTheRecord_AndAMissingOneIsNotAnError()
    {
        await _cosmos.Store.WriteAsync(new SpeDeletedContainerRecord(ContainerId, "cust1", Unit, Config, TypeT, DateTimeOffset.UtcNow));

        await _cosmos.Store.RemoveAsync(ContainerId);
        var again = () => _cosmos.Store.RemoveAsync(ContainerId);

        _cosmos.DocumentOf(ContainerId).Should().BeNull();
        await again.Should().NotThrowAsync();
    }
}
