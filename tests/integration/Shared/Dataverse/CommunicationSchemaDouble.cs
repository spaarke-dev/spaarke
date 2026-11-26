using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// fm task 056: an <see cref="IGenericEntityService"/> whose only answer is <c>sprk_communication</c>'s published column metadata,
/// for the reads that ask whether the private-message reference columns exist
/// (<see cref="PrivateContextColumns.NotPrivateODataFilterAsync"/>). <see cref="WithoutReferenceColumns"/> is an environment the
/// schema scripts have not reached: the app-only record reads run exactly as before. Any other call answers Moq's default.
/// </summary>
/// <remarks>Global namespace, like the other shared Dataverse doubles. A new instance each time: the presence answer is
/// remembered per entity service.</remarks>
public static class CommunicationSchemaDouble
{
    /// <summary><c>sprk_communication</c> without the reference columns.</summary>
    public static IGenericEntityService WithoutReferenceColumns() => Over();

    /// <summary><c>sprk_communication</c> with all three reference columns.</summary>
    public static IGenericEntityService WithReferenceColumns() => Over(PrivateContextColumns.All.ToArray());

    /// <summary><c>sprk_communication</c> with exactly <paramref name="columns"/> besides its primary key.</summary>
    public static IGenericEntityService Over(params string[] columns)
    {
        var metadata = columns.Append("sprk_communicationid")
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        return entities.Object;
    }
}
