using System.Net;
using System.Text;
using Threadline.Core;
using Threadline.Infrastructure;

namespace Threadline.Infrastructure.Tests;

public sealed class OpenAiCompatibleProviderTests
{
    [Fact]
    public async Task CompleteAsync_ChatCompletionsProvider_ReturnsContentAndEndpointMetadata()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            return Json(HttpStatusCode.OK, """
                {
                  "choices": [
                    { "message": { "role": "assistant", "content": "OK from chat completions" } }
                  ]
                }
                """);
        }));
        var provider = new OpenAiCompatibleProvider(
            client,
            new OpenAiCompatibleProviderOptions("Local Provider", "https://local-provider.test/v1", "test-key", "local-model"));

        var response = await provider.CompleteAsync(new LlmRequest("", [LlmMessage.User("health check")], Temperature: 0));

        Assert.Equal("Local Provider", response.ProviderName);
        Assert.Equal("local-model", response.Model);
        Assert.Equal("OK from chat completions", response.Content);
        Assert.Equal("chat/completions", response.Metadata!["providerEndpoint"]);
    }

    [Fact]
    public async Task CompleteAsync_OpenAiProvider_UsesResponsesEndpointAndOutputText()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/v1/responses", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, """{ "output_text": "OK from responses" }""");
        }));
        var provider = new OpenAiCompatibleProvider(
            client,
            new OpenAiCompatibleProviderOptions("OpenAI", "https://api.openai.com/v1", "sk-test", "gpt-test"));

        var response = await provider.CompleteAsync(new LlmRequest("gpt-test", [LlmMessage.User("health check")], Temperature: 0));

        Assert.Equal("OK from responses", response.Content);
        Assert.Equal("responses", response.Metadata!["providerEndpoint"]);
    }

    [Fact]
    public async Task CompleteAsync_ProviderHttpFailure_ThrowsHttpRequestException()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}")));
        var provider = new OpenAiCompatibleProvider(
            client,
            new OpenAiCompatibleProviderOptions("Local Provider", "https://local-provider.test/v1", "test-key", "local-model"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.CompleteAsync(new LlmRequest("local-model", [LlmMessage.User("health check")], Temperature: 0)));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string payload) =>
        new(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
