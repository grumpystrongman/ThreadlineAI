using Threadline.Core;
using Threadline.Infrastructure;

namespace Threadline.Infrastructure.Tests;

public sealed class InMemoryAdapterRegistryTests
{
    [Fact]
    public async Task RegisterAndGet_PersistsRegistration()
    {
        var registry = new InMemoryAdapterRegistry();
        var registration = AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Test Adapter",
            AdapterPermission.ReadSessions,
            DateTimeOffset.UtcNow);

        var saved = await registry.RegisterAsync(registration);
        var retrieved = await registry.GetAsync(saved.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(saved.Id, retrieved.Id);
        Assert.Equal("Test Adapter", retrieved.DisplayName);
    }

    [Fact]
    public async Task Get_ReturnsNullForUnknownId()
    {
        var registry = new InMemoryAdapterRegistry();
        var result = await registry.GetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task List_ReturnsRegistrationsOrderedByDisplayName()
    {
        var registry = new InMemoryAdapterRegistry();
        var now = DateTimeOffset.UtcNow;
        var b = AdapterRegistration.Create(AdapterKind.BrowserExtension, "Beta", AdapterPermission.ReadSessions, now);
        var a = AdapterRegistration.Create(AdapterKind.BrowserExtension, "Alpha", AdapterPermission.ReadSessions, now);

        await registry.RegisterAsync(b);
        await registry.RegisterAsync(a);
        var list = await registry.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("Alpha", list[0].DisplayName);
        Assert.Equal("Beta", list[1].DisplayName);
    }

    [Fact]
    public async Task MarkSeen_UpdatesLastSeenTimestamp()
    {
        var registry = new InMemoryAdapterRegistry();
        var registration = AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Seen Adapter",
            AdapterPermission.ReadSessions,
            DateTimeOffset.UtcNow);

        await registry.RegisterAsync(registration);
        var seenTime = DateTimeOffset.UtcNow.AddMinutes(5);
        var updated = await registry.MarkSeenAsync(registration.Id, seenTime);

        Assert.NotNull(updated);
        Assert.Equal(seenTime, updated.LastSeenAt);
    }

    [Fact]
    public async Task MarkSeen_ReturnsNullForUnknownId()
    {
        var registry = new InMemoryAdapterRegistry();
        var result = await registry.MarkSeenAsync("unknown", DateTimeOffset.UtcNow);
        Assert.Null(result);
    }

    [Fact]
    public async Task Register_OverwritesExistingRegistrationWithSameId()
    {
        var registry = new InMemoryAdapterRegistry();
        var registration = AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Original",
            AdapterPermission.ReadSessions,
            DateTimeOffset.UtcNow);

        await registry.RegisterAsync(registration);

        var updated = registration with { DisplayName = "Updated" };
        await registry.RegisterAsync(updated);

        var retrieved = await registry.GetAsync(registration.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.DisplayName);
        Assert.Single(await registry.ListAsync());
    }
}
