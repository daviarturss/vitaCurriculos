namespace Vita.App.Services;

public sealed record AiProviderPreset(
    string Id,
    string Name,
    string BaseUrl,
    string Model,
    string Description,
    string? KeyUrl = null,
    string? KeyLabel = null);

// Central place for the providers Vita can talk to. Ollama local is the only
// one that needs no account; the others need a key the user provides.
public static class AiProviderCatalog
{
    public const string None = "none";
    public const string OllamaLocal = "ollama-local";
    public const string OllamaCloud = "ollama-cloud";
    public const string OpenAiCompatible = "openai-compatible";

    public static readonly string[] LocalModels =
    {
        "llama3.1",
        "llama3.2:3b",
        "qwen2.5:7b",
        "gemma3:4b",
        "mistral",
        "phi4-mini"
    };

    public static readonly string[] OpenAiModels =
    {
        "gpt-4o-mini",
        "gpt-4.1-mini",
        "gpt-4.1"
    };

    public static readonly string[] CloudModels =
    {
        "deepseek-v4.1-flash",
        "qwen3:32b",
        "llama3.3:70b"
    };

    public static readonly AiProviderPreset[] OpenAiCompatiblePresets =
    {
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", "Modelos da OpenAI",
            "https://platform.openai.com/api-keys", "Criar chave na OpenAI"),
        new("groq", "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "Respostas rápidas e plano gratuito",
            "https://console.groq.com/keys", "Criar chave na Groq"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o-mini", "Vários modelos em uma só chave",
            "https://openrouter.ai/keys", "Criar chave no OpenRouter")
    };
}
