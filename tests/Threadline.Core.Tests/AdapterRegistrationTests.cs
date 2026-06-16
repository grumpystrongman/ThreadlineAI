using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class AdapterRegistrationTests
{
    [Fact]
    public void Create_AssignsIdentityAndPermissions()
    {
        var now = DateTimeOffset.UtcNow;

        var registration = AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Browser Adapter",
            AdapterPermission.ReadSessions | AdapterPermission.WriteContext,
            now,
            version: "0.1.0");

        Assert.StartsWith("adp_", registration.Id);
        Assert.Equal(AdapterKind.BrowserExtension, registration.Kind);
        Assert.Equal("Browser Adapter", registration.DisplayName);
        Assert.True(registration.Permissions.HasFlag(AdapterPermission.WriteContext));
        Assert.Equal(now, registration.LastSeenAt);
    }

    [Fact]
    public void Seen_UpdatesLastSeenTimestamp()
    {
        var registration = AdapterRegistration.Create(AdapterKind.Terminal, "Terminal Adapter", AdapterPermission.WriteContext, DateTimeOffset.UtcNow);
        var seenAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var updated = registration.Seen(seenAt);

        Assert.Equal(seenAt, updated.LastSeenAt);
    }
}
