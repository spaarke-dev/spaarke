using FluentAssertions;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.Scheduling.Tests;

public class ScheduledJobRegistryTests
{
    [Fact]
    public void Register_AddsJobAndResolveReturnsSameInstance()
    {
        var registry = new ScheduledJobRegistry();
        var job = new FakeScheduledJob("test-job");

        registry.Register(job);

        registry.Resolve("test-job").Should().BeSameAs(job);
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Resolve_UnknownJobId_ReturnsNull()
    {
        var registry = new ScheduledJobRegistry();
        registry.Resolve("missing").Should().BeNull();
    }

    [Fact]
    public void Register_DuplicateJobId_Throws()
    {
        var registry = new ScheduledJobRegistry();
        registry.Register(new FakeScheduledJob("dup"));

        var act = () => registry.Register(new FakeScheduledJob("dup"));

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*'dup' is already registered*");
    }

    [Fact]
    public void EnumerateAll_ReturnsAllRegisteredJobs()
    {
        var registry = new ScheduledJobRegistry();
        var a = new FakeScheduledJob("a");
        var b = new FakeScheduledJob("b");
        var c = new FakeScheduledJob("c");

        registry.Register(a);
        registry.Register(b);
        registry.Register(c);

        registry.EnumerateAll().Should().BeEquivalentTo(new[] { a, b, c });
    }

    [Fact]
    public void Register_NullJob_Throws()
    {
        var registry = new ScheduledJobRegistry();
        var act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
