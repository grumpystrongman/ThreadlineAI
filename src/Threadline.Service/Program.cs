using System.Text.Json.Serialization;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;
using Threadline.Infrastructure.Sqlite;
using Threadline.Infrastructure.Windowing;
using Threadline.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var serviceOptions = ThreadlineServiceOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(serviceOptions);

if (serviceOptions.CorsAllowedOrigins.Count > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("ThreadlineLockedCors", policy =>
        {
            policy.WithOrigins(serviceOptions.CorsAllowedOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

var configuredDatabasePath = builder.Configuration["Threadline:DatabasePath"];
var sqliteOptions = SqliteOptions.LocalAppData(string.IsNullOrWhiteSpace(configuredDatabasePath) ? null : configuredDatabasePath);
builder.Services.AddSingleton(sqliteOptions);
builder.Services.AddSingleton<SqliteThreadlineStore>();
builder.Services.AddSingleton<SqliteWorkThreadStore>();
builder.Services.AddSingleton<SqlitePrivacyAndMaintenanceStore>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IProviderConnectionRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IWorkThreadRepository>(sp => sp.GetRequiredService<SqliteWorkThreadStore>());
builder.Services.AddSingleton<IThreadlineStoreInitializer>(sp => sp.GetRequiredService<SqliteThreadlineStore>());
builder.Services.AddSingleton<IThreadlineStoreInitializer>(sp => sp.GetRequiredService<SqliteWorkThreadStore>());
builder.Services.AddSingleton<IThreadlineStoreInitializer>(sp => sp.GetRequiredService<SqlitePrivacyAndMaintenanceStore>());
builder.Services.AddSingleton<IAdapterRegistry, InMemoryAdapterRegistry>();
builder.Services.AddSingleton<IWindowAttachmentRepository, InMemoryWindowAttachmentRepository>();
builder.Services.AddSingleton(sp => new DpapiProtectedSecretStore(builder.Configuration["Threadline:SecretStorePath"]));
builder.Services.AddSingleton<ISecretStore>(sp => sp.GetRequiredService<DpapiProtectedSecretStore>());

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<PrivacyRuntimeState>();
builder.Services.AddSingleton(sp => new CapturePolicy(() => sp.GetRequiredService<PrivacyRuntimeState>().Rules));
builder.Services.AddSingleton<ContextPreviewBuilder>();
builder.Services.AddSingleton<CapabilityRegistry>();
builder.Services.AddSingleton<ThreadlineActionCatalog>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<ProviderConnectionService>();
builder.Services.AddSingleton<SecretService>();
builder.Services.AddSingleton<WindowAttachmentService>();
builder.Services.AddSingleton<PromptComposer>();
builder.Services.AddSingleton<ThreadlineAskService>();
builder.Services.AddSingleton<ThreadlineProviderProbeService>();
builder.Services.AddSingleton<ThreadlineDoctorService>();

var app = builder.Build();

if (serviceOptions.CorsAllowedOrigins.Count > 0)
{
    app.UseCors("ThreadlineLockedCors");
}

var recovery = await SqliteStartupRecovery.RecoverIfNeededAsync(sqliteOptions);
if (recovery.Recovered)
{
    app.Logger.LogWarning("Threadline SQLite startup recovery moved a corrupt database aside: {Detail}. Backup: {BackupPath}", recovery.Detail, recovery.BackupPath);
}

foreach (var initializer in app.Services.GetServices<IThreadlineStoreInitializer>())
{
    await initializer.InitializeAsync();
}

await app.Services.GetRequiredService<PrivacyRuntimeState>().InitializeAsync(
    DefaultRules.Create(DateTimeOffset.UtcNow),
    app.Services.GetRequiredService<SqlitePrivacyAndMaintenanceStore>());

app.MapThreadlineHealth(serviceOptions);
app.MapThreadlineReliabilityApi();
app.MapThreadlineSecurityPrivacyApi();
app.MapThreadlineWorkThreadApi();
app.MapThreadlineApi();

app.Run();

internal static class DefaultRules
{
    public static IReadOnlyList<CaptureRule> Create(DateTimeOffset now) =>
    [
        CaptureRule.Create(CaptureRuleType.ProcessName, "1Password", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "KeePass", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "Bitwarden", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "LastPass", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "Dashlane", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ProcessName, "CredentialUIBroker", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.ApplicationName, "Windows Security", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Private Browsing", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "InPrivate", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Patient Chart", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.WindowTitleContains, "Password", CaptureRuleAction.Ask, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "bankofamerica.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "chase.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "wellsfargo.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "1password.com", CaptureRuleAction.Block, now),
        CaptureRule.Create(CaptureRuleType.DomainContains, "bitwarden.com", CaptureRuleAction.Block, now)
    ];
}
