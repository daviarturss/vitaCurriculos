using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Vita.App.Services;

public sealed record AiConfig(string Provider, string BaseUrl, string Model, string ApiKey, double Temperature, bool JsonMode)
{
    public bool IsOllama => Provider.StartsWith("ollama", StringComparison.OrdinalIgnoreCase);
    public bool IsLocal => Provider == AiProviderCatalog.OllamaLocal;
    public bool IsDisabled => Provider == AiProviderCatalog.None;
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);
    public string DisplayName => Provider switch
    {
        AiProviderCatalog.None => "Sem IA",
        "ollama-cloud" => "Ollama Cloud",
        "ollama-local" => "Ollama local",
        "openai-compatible" => "Compatível com OpenAI",
        _ => Provider
    };
}

public sealed record AiMessage(string Role, string Content);

public sealed class AiException : Exception
{
    public AiException(string message, int? statusCode = null, string? hint = null, string? helpUrl = null, string? helpLabel = null)
        : base(message)
    {
        StatusCode = statusCode;
        Hint = hint;
        HelpUrl = helpUrl;
        HelpLabel = helpLabel;
    }

    public int? StatusCode { get; }
    public string? Hint { get; }
    public string? HelpUrl { get; }
    public string? HelpLabel { get; }
}

/// <summary>Small provider-agnostic client for Ollama and OpenAI-compatible chat APIs.</summary>
public sealed class AiClient(IHttpClientFactory httpFactory)
{
    private static readonly TimeSpan NonStreamTimeout = TimeSpan.FromMinutes(3);

    public async IAsyncEnumerable<string> StreamChatAsync(
        AiConfig config,
        IEnumerable<AiMessage> messages,
        bool json = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureEnabled(config);
        using var request = BuildRequest(config, messages, stream: true, json);
        using var http = httpFactory.CreateClient("ai");
        http.Timeout = Timeout.InfiniteTimeSpan;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ConnectionError(ex, config);
        }

        await using var stream = await ReadContentOrThrowAsync(response, ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (config.IsOllama)
            {
                using var doc = ParseJson(line);
                if (doc is null) continue;
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new AiException(err.GetString() ?? "Erro retornado pelo provedor de IA.", 400);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content) &&
                    content.GetString() is { Length: > 0 } chunk)
                    yield return chunk;
                if (doc.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True) break;
            }
            else
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]") break;
                using var doc = ParseJson(payload);
                if (doc is null) continue;
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new AiException(err.TryGetProperty("message", out var m) ? m.GetString() ?? err.GetString() ?? "Erro retornado pelo provedor de IA." : err.GetString() ?? "Erro retornado pelo provedor de IA.", 400);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content) &&
                        content.GetString() is { Length: > 0 } chunk)
                        yield return chunk;
                }
            }
        }
    }

    public async Task<string> CompleteAsync(AiConfig config, IEnumerable<AiMessage> messages, bool json = false, CancellationToken ct = default)
    {
        EnsureEnabled(config);
        using var request = BuildRequest(config, messages, stream: false, json);
        using var http = httpFactory.CreateClient("ai");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(NonStreamTimeout);
        http.Timeout = Timeout.InfiniteTimeSpan;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("O provedor de IA demorou demais para responder. Tente novamente ou use um modelo menor.");
        }
        catch (HttpRequestException ex)
        {
            throw ConnectionError(ex, config);
        }

        var body = await ReadBodyOrThrowAsync(response, cts.Token);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (config.IsOllama)
            {
                if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                    return content.GetString() ?? string.Empty;
                if (root.TryGetProperty("response", out var resp)) return resp.GetString() ?? string.Empty;
            }
            else
            {
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                        return content.GetString() ?? string.Empty;
                }
            }
            throw new AiException("O provedor retornou uma resposta em formato inesperado.");
        }
        catch (JsonException)
        {
            throw new AiException("O provedor retornou uma resposta inválida. Verifique a URL base e o modelo configurado.");
        }
    }

    public async Task<List<string>> ListModelsAsync(AiConfig config, CancellationToken ct = default)
    {
        EnsureEnabled(config);
        using var http = httpFactory.CreateClient("ai");
        http.Timeout = TimeSpan.FromSeconds(20);
        var url = config.IsOllama
            ? $"{config.BaseUrl.TrimEnd('/')}/api/tags"
            : $"{NormalizeOpenAiBase(config.BaseUrl)}/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (config.HasKey) request.Headers.Authorization = new("Bearer", config.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw HttpError(response.StatusCode, body);
            using var doc = JsonDocument.Parse(body);
            var list = new List<string>();
            if (config.IsOllama && doc.RootElement.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                    if (m.TryGetProperty("name", out var name) && name.GetString() is { } n) list.Add(n);
            if (!config.IsOllama && doc.RootElement.TryGetProperty("data", out var data))
                foreach (var m in data.EnumerateArray())
                    if (m.TryGetProperty("id", out var id) && id.GetString() is { } n) list.Add(n);
            return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (HttpRequestException)
        {
            throw new AiException("Não foi possível conectar ao provedor. Verifique a URL base e se o serviço está rodando.");
        }
        catch (TaskCanceledException)
        {
            throw new AiException("Tempo esgotado ao listar modelos do provedor.");
        }
    }

    private static HttpRequestMessage BuildRequest(AiConfig config, IEnumerable<AiMessage> messages, bool stream, bool json)
    {
        var url = config.IsOllama
            ? $"{config.BaseUrl.TrimEnd('/')}/api/chat"
            : $"{NormalizeOpenAiBase(config.BaseUrl)}/chat/completions";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["stream"] = stream
        };

        if (config.IsOllama)
        {
            payload["options"] = new { temperature = config.Temperature };
            if (json) payload["format"] = "json";
        }
        else
        {
            payload["temperature"] = config.Temperature;
            if (json) payload["response_format"] = new { type = "json_object" };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        if (config.HasKey) request.Headers.Authorization = new("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", "Vita/1.0");
        return request;
    }

    private static string NormalizeOpenAiBase(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) url = url[..^"/chat/completions".Length];
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url += "/v1";
        return url;
    }

    private async Task<Stream> ReadContentOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw HttpError(response.StatusCode, body);
        }
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private async Task<string> ReadBodyOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw HttpError(response.StatusCode, body);
        return body;
    }

    private static AiException HttpError(HttpStatusCode status, string body)
    {
        var detail = ExtractError(body);
        var (message, hint, helpUrl, helpLabel) = status switch
        {
            HttpStatusCode.Unauthorized => (
                "Chave de API inválida ou ausente.",
                "Cole uma chave válida em Configurações. Chaves costumam ser longas e só aparecem uma vez no painel do provedor.",
                null, null),
            HttpStatusCode.PaymentRequired => (
                "Sua conta no provedor está sem créditos.",
                "Adicione créditos no painel do Ollama Cloud ou troque para o Ollama local / outro provedor. O resto do app continua funcionando sem IA.",
                "https://ollama.com/pricing", "Ver planos do Ollama"),
            HttpStatusCode.Forbidden => (
                "O provedor recusou o acesso.",
                "Confira se a chave tem permissão para usar este modelo e se o modelo existe na sua conta.",
                null, null),
            HttpStatusCode.NotFound => (
                "Modelo ou endpoint não encontrado.",
                "Confira o nome exato do modelo e a URL base. No Ollama local, baixe o modelo antes de testar.",
                null, null),
            HttpStatusCode.TooManyRequests => (
                "Muitas requisições em pouco tempo.",
                "Aguarde alguns segundos e tente de novo, ou use um modelo menor.",
                null, null),
            HttpStatusCode.BadRequest => (
                "A requisição foi rejeitada pelo provedor.",
                string.IsNullOrWhiteSpace(detail) ? "Confira modelo, URL base e se o provedor aceita o modo JSON." : detail,
                null, null),
            _ when (int)status >= 500 => (
                "O provedor de IA está instável neste momento.",
                "Tente novamente em instantes. Se persistir, troque de provedor ou use o Ollama local.",
                null, null),
            _ => (
                $"Erro do provedor de IA ({(int)status}).",
                detail, null, null)
        };
        return new AiException(message, (int)status, hint, helpUrl, helpLabel);
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? "";
                if (err.TryGetProperty("message", out var msg)) return msg.GetString() ?? "";
            }
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? "";
        }
        catch
        {
            // not json
        }
        return TextAnalysis.Truncate(body.Replace('\n', ' '), 240);
    }

    private static AiException ConnectionError(Exception ex, AiConfig config)
    {
        if (config.IsLocal)
        {
            return new AiException(
                "Não foi possível conectar ao Ollama local.",
                hint: "O app pode instalar e iniciar o Ollama por você em Configurações → Inteligência artificial. Se preferir, rode “ollama serve” manualmente.",
                helpUrl: "https://ollama.com/download/windows",
                helpLabel: "Baixar Ollama");
        }

        var detail = ex.InnerException?.Message ?? ex.Message;
        return new AiException(
            "Não foi possível conectar ao provedor de IA.",
            hint: $"Verifique sua internet e a URL base configurada. Detalhe técnico: {TextAnalysis.Truncate(detail, 160)}");
    }

    private static void EnsureEnabled(AiConfig config)
    {
        if (!config.IsDisabled) return;
        throw new AiException(
            "A IA está desativada.",
            hint: "Você pode ativar quando quiser em Configurações → Inteligência artificial. Currículos, ATS, vagas e cartas continuam funcionando sem IA.",
            helpUrl: "/settings",
            helpLabel: "Abrir configurações");
    }

    private static JsonDocument? ParseJson(string text)
    {
        try { return JsonDocument.Parse(text); }
        catch { return null; }
    }
}
