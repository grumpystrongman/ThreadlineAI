using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public sealed class PrivacyRuntimeState
{
    private readonly object _syncRoot = new();
    private IReadOnlyList<CaptureRule> _rules = [];

    public IReadOnlyList<CaptureRule> Rules
    {
        get
        {
            lock (_syncRoot)
            {
                return _rules;
            }
        }
    }

    public async Task InitializeAsync(IEnumerable<CaptureRule> defaultRules, SqlitePrivacyAndMaintenanceStore store, CancellationToken cancellationToken = default)
    {
        var persisted = await store.ListPrivacyExclusionsAsync(cancellationToken);
        Replace(defaultRules.Concat(persisted));
    }

    public void Replace(IEnumerable<CaptureRule> rules)
    {
        lock (_syncRoot)
        {
            _rules = rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
                .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(rule => rule.Source == CaptureRuleSource.User)
                .ThenBy(rule => rule.CreatedAt)
                .ToArray();
        }
    }

    public void Upsert(CaptureRule rule)
    {
        lock (_syncRoot)
        {
            _rules = _rules.Where(existing => !existing.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase)).Concat([rule]).ToArray();
        }
    }

    public void Remove(string ruleId)
    {
        lock (_syncRoot)
        {
            _rules = _rules.Where(rule => !rule.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
}
