using Threadline.Core;
using Threadline.Infrastructure.Security;

namespace Threadline.Infrastructure.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public async Task InMemorySecretStore_ReturnsReferenceAndResolvesValue()
    {
        var store = new InMemorySecretStore();

        var descriptor = await store.SetSecretAsync("provider/openai/credential", "test-secret-value");
        var value = await store.GetSecretAsync(descriptor.Reference);
        var described = await store.DescribeSecretAsync(descriptor.Reference);

        Assert.StartsWith("secret://memory/", descriptor.Reference);
        Assert.Equal("test-secret-value", value);
        Assert.NotNull(described);
        Assert.Equal(SecretProtectionKind.InMemory, described.ProtectionKind);
    }

    [Fact]
    public async Task InMemorySecretStore_DeletesValue()
    {
        var store = new InMemorySecretStore();
        var descriptor = await store.SetSecretAsync("provider/test/credential", "delete-me-now");

        var deleted = await store.DeleteSecretAsync(descriptor.Reference);
        var value = await store.GetSecretAsync(descriptor.Reference);

        Assert.True(deleted);
        Assert.Null(value);
    }

    [Fact]
    public async Task SecretService_AuditsWithoutSecretValue()
    {
        var store = new InMemorySecretStore();
        var audit = new TestAuditRepository();
        var service = new SecretService(store, new SystemClock(), audit);

        var descriptor = await service.SetAsync("provider/test/credential", "super-sensitive-value");
        await service.GetValueAsync(descriptor.Reference);
        await service.DeleteAsync(descriptor.Reference);

        Assert.Equal(3, audit.Events.Count);
        Assert.All(audit.Events, auditEvent => Assert.DoesNotContain("super-sensitive-value", auditEvent.Message));
        Assert.All(audit.Events, auditEvent => Assert.DoesNotContain("super-sensitive-value", string.Join('|', auditEvent.Metadata?.Values ?? [])));
    }

    private sealed class TestAuditRepository : IAuditRepository
    {
        public List<AuditEvent> Events { get; } = [];

        public Task AppendAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> GetRecentAuditEventsAsync(string? sessionId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events.TakeLast(take).ToArray());
    }
}
