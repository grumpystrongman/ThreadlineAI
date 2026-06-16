# Provider strategy

ThreadlineAI should not scrape consumer chat websites. The product should connect to models through supported developer APIs, gateway APIs, or local OpenAI-compatible endpoints.

| Provider | MVP connection method |
|---|---|
| OpenAI | API key / project key |
| Anthropic Claude | API key |
| Gemini | API key first, OAuth later |
| DeepSeek | API key / bearer token |
| OpenRouter | API key or OAuth PKCE |
| Local Llama | OpenAI-compatible localhost |

All providers implement `ILlmProvider`. Provider credentials must not be stored in plaintext. Use Windows Credential Manager, DPAPI, or an enterprise vault integration.
