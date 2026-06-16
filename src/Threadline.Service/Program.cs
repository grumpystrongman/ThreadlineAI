using System.Text.Json.Serialization;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;
using Threadline.Infrastructure.Sqlite;
using Threadline.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var serviceOptions = ThreadlineServiceOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(serviceOptions);

var configuredDatabasePath = builder.Configuration["Threadline:DatabasePath"];
builder.Services.AddSingleton(SqliteOptions.LocalAppData(string.IsNullOrWhiteSpace(configuredDatabasePath) ? null : configuredDatabasePath));
builder.Services.AddSingleton<SqliteThreadlineStore>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IProviderConnectionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IThreadlineStoreInitializer>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IAdapterRegistry, InMemoryAdapterRegistry>();
builder.Services.AddSingleton<ISecretStore>(_ => new DpapiProtectedSecretStore(builder.Configuration["Threadline:SecretStorePath"]));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton(new CapturePolicy(DefaultRules.Create(DateTimeOffset.UtcNow)));
builder.Services.AddSingleton<ContextPreviewBuilder>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<ProviderConnectionService>();
builder.Services.AddSingleton<SecretService>();
builder.Services.AddSingleton<PromptComposer>();

var app = builder.Build();

foreach (var initializer in app.Services.GetServices<IThreadlineStoreInitializer>())
{
    await initializer.InitializeAsync();
}

app.MapThreadlineHealth(serviceOptions);
app.MapThreadlineApi();

app.Run();

internal static class DefaultRules
{
    public static IReadOnlyList<CaptureRule> Create(DateTimeOffset now) =>
    [
        CaptureRule.Create(CaptureRuleType.ProcessName, "1Password", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "KeePass", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "Bitwarden", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Private Browsing", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "InPrivate", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Patient Chart", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "bankofamerica.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "chase.com", CaptureRuleAction.Block, now)
    ];
}
