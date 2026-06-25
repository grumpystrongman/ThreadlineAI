namespace Threadline.Service;

internal static class MetadataHelpers
{
    public static Dictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string>? incoming)
    {
        var metadata = NormalizeMetadata(existing);
        if (incoming is null) return metadata;

        foreach (var item in incoming)
        {
            if (!string.IsNullOrWhiteSpace(item.Key)) metadata[item.Key] = item.Value;
        }

        return metadata;
    }
}
