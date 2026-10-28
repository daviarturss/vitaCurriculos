namespace Vita.App.Data;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "pt";
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ResumeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "Meu currículo";
    public string TemplateId { get; set; } = "aurora";
    public string AccentColor { get; set; } = "#1F6F5C";
    public string Language { get; set; } = "pt";
    public string DataJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ApplicationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ResumeId { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = "Manual";
    public string Status { get; set; } = ApplicationStatuses.Saved;
    public int MatchScore { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public string SalaryRange { get; set; } = string.Empty;
    public DateTime? AppliedAt { get; set; }
    public DateTime? NextStepAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class ApplicationStatuses
{
    public const string Saved = "saved";
    public const string Applied = "applied";
    public const string Interview = "interview";
    public const string Offer = "offer";
    public const string Rejected = "rejected";

    public static readonly string[] All = { Saved, Applied, Interview, Offer, Rejected };

    public static string Label(string status) => status switch
    {
        Applied => "Aplicado",
        Interview => "Entrevista",
        Offer => "Proposta",
        Rejected => "Arquivado",
        _ => "Salvo"
    };
}

public class CoverLetterEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ResumeId { get; set; }
    public string Title { get; set; } = "Nova carta";
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Tone { get; set; } = "profissional";
    public string Language { get; set; } = "pt";
    public string Body { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "Nova conversa";
    public Guid? ResumeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessageEntity> Messages { get; set; } = new();
}

public class ChatMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AiSettings
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "ollama-cloud"; // ollama-cloud | ollama-local | openai-compatible
    public string BaseUrl { get; set; } = "https://ollama.com";
    public string Model { get; set; } = "deepseek-v4.1-flash";
    public string ApiKeyProtected { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.4;
    public bool JsonMode { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SavedJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int MatchScore { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
