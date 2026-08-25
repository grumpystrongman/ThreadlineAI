using System.Text;
using System.Text.Json;
using Threadline.Core;

namespace Threadline.Windows.Services;

/// <summary>
/// Owner-controlled authority persistence. This file is read/written by the interactive
/// AIKA/JARVIS app; no MCP tool can modify it. Protected operations remain confirmation-gated.
/// </summary>
public sealed class DeviceAuthorityStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DeviceAuthorityStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI",
            "device-authority.json");
    }

    public string Path => _path;

    public DeviceAuthorityGrant Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Save(DeviceAuthorityGrant.OwnerDefault);
                return DeviceAuthorityGrant.OwnerDefault;
            }
            var envelope = JsonSerializer.Deserialize<AuthorityEnvelope>(File.ReadAllText(_path), _json);
            return envelope?.Grant ?? DeviceAuthorityGrant.OwnerDefault;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return DeviceAuthorityGrant.OwnerDefault;
        }
    }

    public void Save(DeviceAuthorityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        var envelope = new AuthorityEnvelope(1, DateTimeOffset.UtcNow, grant);
        File.WriteAllText(temp, JsonSerializer.Serialize(envelope, _json), new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed record AuthorityEnvelope(int SchemaVersion, DateTimeOffset UpdatedAt, DeviceAuthorityGrant Grant);
}
