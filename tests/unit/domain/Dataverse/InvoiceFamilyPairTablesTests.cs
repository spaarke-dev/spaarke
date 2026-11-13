using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// H1 (ebilling-r1 + unified-access-control-r2 agreed 2026-11-13): an invoice-family row is filed under its core record through
/// its own core lookups, which the invoice-family guard decides on every generic write. Its ADR-024 pair only describes that
/// record: no family table is one whose pair names a parent on its own.
/// </summary>
public class InvoiceFamilyPairTablesTests
{
    [Fact]
    public void NoInvoiceFamilyTable_IsABffManagedPairTable()
    {
        SecureRootInheritance.BffManagedPairTables.Should().NotIntersectWith(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys);
        foreach (var table in InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys)
            SecureRootInheritance.PairNamesAParentOnItsOwn(table).Should().BeFalse(table);
    }
}
