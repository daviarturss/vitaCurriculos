using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Vita.Core;

// ---------------------------------------------------------------------------
// Document model. Everything a resume contains lives here and is stored as
// JSON inside the ResumeEntity so the shape can evolve without migrations.
// ---------------------------------------------------------------------------

public sealed class ResumeDocument
{
    public ContactInfo Contact { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<ExperienceItem> Experience { get; set; } = new();
    public List<EducationItem> Education { get; set; } = new();
    public List<SkillGroup> SkillGroups { get; set; } = new();
    public List<ProjectItem> Projects { get; set; } = new();
    public List<CertificationItem> Certifications { get; set; } = new();
    public List<LanguageItem> Languages { get; set; } = new();
    public List<CustomSection> CustomSections { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Contact.FullName) &&
        string.IsNullOrWhiteSpace(Summary) &&
        Experience.Count == 0 && Education.Count == 0 && SkillGroups.Count == 0;

    public ResumeDocument Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, Json.Options);
        return System.Text.Json.JsonSerializer.Deserialize<ResumeDocument>(json, Json.Options) ?? new ResumeDocument();
    }

    public IEnumerable<string> AllBullets =>
        Experience.SelectMany(e => e.Bullets)
            .Concat(Projects.SelectMany(p => p.Bullets))
            .Concat(CustomSections.SelectMany(c => c.Items))
            .Where(b => !string.IsNullOrWhiteSpace(b));

    public IEnumerable<string> AllSkills =>
        SkillGroups.SelectMany(g => g.Items).Where(s => !string.IsNullOrWhiteSpace(s));

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Contact.FullName);
        sb.AppendLine(Contact.Headline);
        sb.AppendLine($"{Contact.Email} | {Contact.Phone} | {Contact.Location} | {Contact.Website} | {Contact.LinkedIn}");
        if (!string.IsNullOrWhiteSpace(Summary)) { sb.AppendLine(); sb.AppendLine("RESUMO"); sb.AppendLine(Summary); }
        if (Experience.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("EXPERIÊNCIA");
            foreach (var e in Experience)
            {
                sb.AppendLine($"{e.Role} | {e.Company} | {e.Location} | {e.PeriodLabel}");
                foreach (var b in e.Bullets) sb.AppendLine($"- {b}");
            }
        }
        if (Education.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("FORMAÇÃO");
            foreach (var e in Education)
                sb.AppendLine($"{e.Degree} {e.Field} | {e.Institution} | {e.PeriodLabel}");
        }
        if (SkillGroups.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("COMPETÊNCIAS");
            foreach (var g in SkillGroups) sb.AppendLine($"{g.Name}: {string.Join(", ", g.Items)}");
        }
        if (Projects.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("PROJETOS");
            foreach (var p in Projects)
            {
                sb.AppendLine($"{p.Name} | {p.Link}");
                if (!string.IsNullOrWhiteSpace(p.Description)) sb.AppendLine(p.Description);
                foreach (var b in p.Bullets) sb.AppendLine($"- {b}");
            }
        }
        if (Certifications.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("CERTIFICAÇÕES");
            foreach (var c in Certifications) sb.AppendLine($"{c.Name} | {c.Issuer} | {c.Year}");
        }
        if (Languages.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("IDIOMAS");
            foreach (var l in Languages) sb.AppendLine($"{l.Name} — {l.Level}");
        }
        foreach (var s in CustomSections)
        {
            sb.AppendLine(); sb.AppendLine(s.Title.ToUpperInvariant());
            foreach (var i in s.Items) sb.AppendLine($"- {i}");
        }
        return sb.ToString();
    }
}

public sealed class ContactInfo
{
    public string FullName { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string Github { get; set; } = string.Empty;
}

public sealed class ExperienceItem
{
    public string Role { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty; // yyyy-MM
    public string EndDate { get; set; } = string.Empty;   // yyyy-MM
    public bool Current { get; set; }
    public List<string> Bullets { get; set; } = new();

    [JsonIgnore]
    public string PeriodLabel => DateRange.Label(StartDate, EndDate, Current);
}

public sealed class EducationItem
{
    public string Institution { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public bool Current { get; set; }
    public string Details { get; set; } = string.Empty;

    [JsonIgnore]
    public string PeriodLabel => DateRange.Label(StartDate, EndDate, Current);
}

public sealed class SkillGroup
{
    public string Name { get; set; } = "Competências";
    public List<string> Items { get; set; } = new();
}

public sealed class ProjectItem
{
    public string Name { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Bullets { get; set; } = new();
}

public sealed class CertificationItem
{
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
}

public sealed class LanguageItem
{
    public string Name { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}

public sealed class CustomSection
{
    public string Title { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
}

public static class DateRange
{
    public static string Label(string start, string end, bool current)
    {
        var s = Format(start);
        var e = current || string.IsNullOrWhiteSpace(end) ? "Atual" : Format(end);
        if (string.IsNullOrWhiteSpace(s) && string.IsNullOrWhiteSpace(e)) return string.Empty;
        if (string.IsNullOrWhiteSpace(s)) return e;
        return $"{s} — {e}";
    }

    public static string Format(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (DateTime.TryParse(value + "-01", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("MMM yyyy", CultureInfo.GetCultureInfo("pt-BR")).Replace(".", "");
        return value;
    }
}

public static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, Options); }
        catch { return default; }
    }

    /// <summary>Extracts the first JSON object/array from a model answer that may contain prose or code fences.</summary>
    public static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = text.Trim();
        var fence = cleaned.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = cleaned.IndexOf('\n', fence);
            var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
                cleaned = cleaned[(start + 1)..end].Trim();
        }
        var firstBrace = cleaned.IndexOf('{');
        var firstBracket = cleaned.IndexOf('[');
        int startIdx;
        char open, close;
        if (firstBrace < 0 && firstBracket < 0) return null;
        if (firstBracket >= 0 && (firstBrace < 0 || firstBracket < firstBrace)) { startIdx = firstBracket; open = '['; close = ']'; }
        else { startIdx = firstBrace; open = '{'; close = '}'; }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = startIdx; i < cleaned.Length; i++)
        {
            var c = cleaned[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return cleaned[startIdx..(i + 1)];
            }
        }
        return null;
    }
}
