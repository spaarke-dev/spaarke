using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// The BFF's own (app-only) read of a Signal's core-record pair, as <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> asks it when the
/// caller's read returns neither half (field-mapping-server-write-path-r1 task 044 fix). Harnesses that serve a no-core Signal answer it
/// with "no stored pair", so their existing no-core behaviour is unchanged. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).
/// </summary>
internal static class CorePairAppOnlyFakes
{
    /// <summary>The app-only read of any Signal returns a row with neither half of the pair.</summary>
    public static Mock<IGenericEntityService> WithNoStoredCorePair(this Mock<IGenericEntityService> entities)
    {
        entities.Setup(e => e.RetrieveAsync(SignalCoreRecordAccess.SignalTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Entity(table, id));
        return entities;
    }

    /// <summary>The app-only read of any Signal returns a stored pair: the type half, the id half, or both.</summary>
    public static Mock<IGenericEntityService> WithStoredCorePair(this Mock<IGenericEntityService> entities, Guid? typeRefId, Guid? coreId)
    {
        entities.Setup(e => e.RetrieveAsync(SignalCoreRecordAccess.SignalTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
            {
                var row = new Entity(table, id);
                if (typeRefId is { } t) row[SignalCoreRecordAccess.CoreRecordTypeColumn] = new EntityReference("sprk_recordtype_ref", t);
                if (coreId is { } c) row[SignalCoreRecordAccess.CoreRecordIdColumn] = c.ToString("D");
                return row;
            });
        return entities;
    }

    /// <summary>The app-only read of any Signal throws <paramref name="fault"/>.</summary>
    public static Mock<IGenericEntityService> WithCorePairReadFault(this Mock<IGenericEntityService> entities, Exception fault)
    {
        entities.Setup(e => e.RetrieveAsync(SignalCoreRecordAccess.SignalTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);
        return entities;
    }
}
