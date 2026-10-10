using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Notifications;

/// <summary>
/// appnotification option values (D-98 item 9, PB-03b/03c/04): the constants match the checked-in live-metadata snapshot, and
/// every direct writer refuses a value Dataverse would reject instead of failing at first fire.
/// </summary>
public class AppNotificationOptionsTests
{
    private static JsonElement Snapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Services", "Fixtures", "appnotification-options.snapshot.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static Dictionary<string, int> Section(string name) =>
        Snapshot().GetProperty(name).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

    private static Dictionary<string, int> Constants(Type holder) =>
        holder.GetFields().Where(f => f.IsLiteral).ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);

    [Fact]
    public void Priority_MatchesTheLiveSnapshot() =>
        Constants(typeof(AppNotificationOptions.Priority)).Should().BeEquivalentTo(Section("priority"));

    [Fact]
    public void ToastType_MatchesTheLiveSnapshot() =>
        Constants(typeof(AppNotificationOptions.ToastType)).Should().BeEquivalentTo(Section("toasttype"));

    [Fact]
    public void IconType_MatchesTheLiveSnapshot() =>
        Constants(typeof(AppNotificationOptions.IconType)).Should().BeEquivalentTo(Section("icontype"));

    [Theory]
    [InlineData(100_000_000)]
    [InlineData(200_000_002)]   // the old "Critical" the held-email alert and last-day reminder sent
    [InlineData(300_000_000)]   // an LLM-authored "Urgent"
    [InlineData(0)]
    public void Validate_RejectsAPriorityOutsideTheLiveSet(int priority) =>
        AppNotificationOptions.Validate(priority, AppNotificationOptions.ToastType.Timed)
            .Should().Contain(priority.ToString()).And.Contain("priority");

    [Theory]
    [InlineData(100_000_000)]   // the old "Hidden" NotificationActionCore compared with
    [InlineData(300_000_000)]
    public void Validate_RejectsAToastTypeOutsideTheLiveSet(int toast) =>
        AppNotificationOptions.Validate(AppNotificationOptions.Priority.Normal, toast)
            .Should().Contain(toast.ToString()).And.Contain("toastType");

    [Fact]
    public void Validate_AcceptsEveryLiveCombination()
    {
        foreach (var p in Section("priority").Values)
            foreach (var t in Section("toasttype").Values)
                AppNotificationOptions.Validate(p, t).Should().BeNull();
    }

    // NotificationService: the single writer behind the held-email alert, the grant reminder and the others.

    [Fact]
    public async Task NotificationService_RejectsAnInvalidPriority_BeforeAnyWrite()
    {
        var entities = new Mock<IGenericEntityService>();
        var sut = new NotificationService(entities.Object, NullLogger<NotificationService>.Instance);

        var act = () => sut.CreateNotificationAsync(Guid.NewGuid(), "t", priority: 200_000_002);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*200000002*");
        entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NotificationActionCore (PB-04): only a Timed toast carries the Open action; Hidden suppresses it.

    private static NotificationActionInput Input(int priority, int toast) => new(
        Title: "t", Body: "b", Category: "c", Priority: priority, ToastType: toast, ActionUrl: "/main.aspx?id=1",
        RecipientId: Guid.NewGuid(), RegardingId: null, RegardingType: null, DueDate: null, RegardingName: null,
        SourceEntityType: null, SourceId: null, SourceModifiedOn: null, SourceOwningUser: null, ViaMatterId: null,
        ViaMatterName: null, ViaMatterMemberships: null, Source: "dispatch", CorrelationId: "k");

    private static (NotificationActionCore Core, List<Entity> Created) Core()
    {
        var created = new List<Entity>();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        return (new NotificationActionCore(entities.Object, NullLogger.Instance), created);
    }

    [Theory]
    [InlineData(AppNotificationOptions.ToastType.Timed, true)]
    [InlineData(AppNotificationOptions.ToastType.Hidden, false)]
    public async Task Core_OnlyATimedToastCarriesTheOpenAction(int toast, bool expectsActions)
    {
        var (core, created) = Core();

        await core.CreateAsync(Input(AppNotificationOptions.Priority.High, toast), CancellationToken.None);

        created.Should().ContainSingle();
        ((OptionSetValue)created[0]["toasttype"]).Value.Should().Be(toast);
        created[0].GetAttributeValue<string>("data").Contains("\"actions\"").Should().Be(expectsActions);
    }

    [Theory]
    [InlineData(300_000_000, AppNotificationOptions.ToastType.Timed)]
    [InlineData(AppNotificationOptions.Priority.Normal, 100_000_000)]
    public async Task Core_RefusesAValueOutsideTheLiveSet_AndWritesNothing(int priority, int toast)
    {
        var (core, created) = Core();

        var act = () => core.CreateAsync(Input(priority, toast), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        created.Should().BeEmpty();
    }

    // ActionSeam (PB-03c): an LLM-supplied value comes back as a loud failed result, never a throw.

    [Fact]
    public async Task ActionSeam_ReturnsAFailedResult_ForAnLlmSuppliedPriorityOutsideTheLiveSet()
    {
        // Validation runs before any collaborator is touched, so an uninitialised instance is enough.
        var seam = (ActionSeam)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ActionSeam));

        var result = await seam.CreateNotificationAsync(new CreateNotificationRequest
        {
            Title = "t",
            Body = "b",
            RecipientId = Guid.NewGuid(),
            Priority = 300_000_000,
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("300000000");
    }
}
