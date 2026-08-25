using System.Text.Json;
using System.Text.Json.Nodes;

namespace Threadline.McpCommon;

public abstract class McpStdioServer
{
    protected JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    protected abstract string ServerName { get; }
    protected abstract string ServerVersion { get; }
    protected abstract string Instructions { get; }
    protected abstract string ToolFailurePrefix { get; }

    protected abstract JsonObject BuildToolList();
    protected abstract Task<JsonObject> ExecuteToolAsync(JsonObject? parameters, CancellationToken cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonObject? request;
            try
            {
                request = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException ex)
            {
                await WriteErrorAsync(null, -32700, ex.Message);
                continue;
            }

            if (request is null) continue;
            var id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(method))
            {
                if (id is not null) await WriteErrorAsync(id, -32600, "Invalid Request");
                continue;
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        if (id is not null) await WriteResultAsync(id, Initialize(request["params"] as JsonObject));
                        break;
                    case "notifications/initialized":
                    case "notifications/cancelled":
                        break;
                    case "ping":
                        if (id is not null) await WriteResultAsync(id, new JsonObject());
                        break;
                    case "tools/list":
                        if (id is not null) await WriteResultAsync(id, BuildToolList());
                        break;
                    case "tools/call":
                        if (id is not null) await WriteResultAsync(id, await ExecuteToolAsync(request["params"] as JsonObject, cancellationToken));
                        break;
                    default:
                        if (id is not null) await WriteErrorAsync(id, -32601, $"Method not found: {method}");
                        break;
                }
            }
            catch (Exception ex) when (IsExpectedToolException(ex))
            {
                if (id is not null) await WriteResultAsync(id, ToolResult($"{ToolFailurePrefix}: {ex.Message}", true));
            }
        }
    }

    protected virtual bool IsExpectedToolException(Exception ex) =>
        ex is IOException or TimeoutException or InvalidOperationException or ArgumentException or JsonException
            or OperationCanceledException or UnauthorizedAccessException or HttpRequestException;

    protected static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema
    };

    protected static JsonObject Prop(string name, string type, string description) => new()
    {
        ["_name"] = name,
        ["type"] = type,
        ["description"] = description
    };

    protected static JsonObject Schema(IEnumerable<JsonObject> properties, IEnumerable<string> required)
    {
        var objectProperties = new JsonObject();
        foreach (var property in properties)
        {
            var clone = (JsonObject)property.DeepClone();
            var name = clone["_name"]?.GetValue<string>() ?? throw new InvalidOperationException("MCP property name is missing.");
            clone.Remove("_name");
            objectProperties[name] = clone;
        }

        var requiredNodes = required.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray();
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = objectProperties,
            ["required"] = new JsonArray(requiredNodes),
            ["additionalProperties"] = false
        };
    }

    protected static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text }
        },
        ["isError"] = isError
    };

    private JsonObject Initialize(JsonObject? parameters)
    {
        var requested = parameters?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = string.IsNullOrWhiteSpace(requested) ? "2025-06-18" : requested,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
            ["instructions"] = Instructions
        };
    }

    private async Task WriteResultAsync(JsonNode id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        };
        await Console.Out.WriteLineAsync(response.ToJsonString(JsonOptions));
        await Console.Out.FlushAsync();
    }

    private async Task WriteErrorAsync(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        await Console.Out.WriteLineAsync(response.ToJsonString(JsonOptions));
        await Console.Out.FlushAsync();
    }
}
