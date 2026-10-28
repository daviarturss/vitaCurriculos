using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

public sealed record AiSettingsInput(
    string Provider,
    string BaseUrl,
    string Model,
    string? ApiKey,
    double Temperature,
    bool JsonMode,
    bool ClearApiKey = false);

public sealed class AiSettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IConfiguration configuration)
{
    private IDataProtector Protector => dataProtection.CreateProtector("vita.ai.v1");

    public async Task<AiConfig> GetConfigAsync(Guid userId, CancellationToken ct = default)
    {
        var entity = await GetOrCreateAsync(userId, ct);
        var key = string.IsNullOrEmpty(entity.ApiKeyProtected) ? string.Empty : TryUnprotect(entity.ApiKeyProtected) ?? string.Empty;
        return new AiConfig(entity.Provider, entity.BaseUrl, entity.Model, key, entity.Temperature, entity.JsonMode);
    }

    public async Task<AiSettings> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.AiSettings.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (entity is not null) return entity;

        var defaults = configuration.GetSection("Ai");
        entity = new AiSettings
        {
            UserId = userId,
            Provider = defaults["DefaultProvider"] ?? AiProviderCatalog.None,
            BaseUrl = defaults["DefaultBaseUrl"] ?? string.Empty,
            Model = defaults["DefaultModel"] ?? string.Empty,
            Temperature = 0.4,
            JsonMode = true
        };
        var defaultKey = defaults["DefaultApiKey"];
        if (!string.IsNullOrWhiteSpace(defaultKey)) entity.ApiKeyProtected = Protector.Protect(defaultKey.Trim());
        db.AiSettings.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task SaveAsync(Guid userId, AiSettingsInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.AiSettings.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (entity is null)
        {
            entity = new AiSettings { UserId = userId };
            db.AiSettings.Add(entity);
        }
        entity.Provider = string.IsNullOrWhiteSpace(input.Provider) ? AiProviderCatalog.None : input.Provider;
        entity.BaseUrl = NormalizeBaseUrl(entity.Provider, input.BaseUrl);
        entity.Model = string.IsNullOrWhiteSpace(input.Model) ? DefaultModel(entity.Provider) : input.Model.Trim();
        entity.Temperature = Math.Clamp(input.Temperature, 0, 1);
        entity.JsonMode = input.JsonMode;
        entity.UpdatedAt = DateTime.UtcNow;
        if (input.ClearApiKey) entity.ApiKeyProtected = string.Empty;
        else if (!string.IsNullOrWhiteSpace(input.ApiKey)) entity.ApiKeyProtected = Protector.Protect(input.ApiKey.Trim());
        await db.SaveChangesAsync(ct);
    }

    public static string NormalizeBaseUrl(string provider, string? baseUrl) => provider switch
    {
        AiProviderCatalog.None => string.Empty,
        "ollama-local" => string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/'),
        "ollama-cloud" => string.IsNullOrWhiteSpace(baseUrl) ? "https://ollama.com" : baseUrl.TrimEnd('/'),
        _ => string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl.TrimEnd('/')
    };

    public static string DefaultModel(string provider) => provider switch
    {
        AiProviderCatalog.None => string.Empty,
        "ollama-local" => "llama3.1",
        "ollama-cloud" => "deepseek-v4.1-flash",
        _ => "gpt-4o-mini"
    };

    public string? TryUnprotect(string protectedValue)
    {
        try { return Protector.Unprotect(protectedValue); }
        catch { return null; }
    }
}
