using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vita.Core;

public sealed class AiAction
{
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public int? Experience { get; set; }
    public int? Bullet { get; set; }
    public int? Group { get; set; }
    public string? Skill { get; set; }

    [JsonIgnore]
    public string Label => Type switch
    {
        "summary" => "Resumo profissional",
        "headline" => "Título profissional",
        "update_bullet" => "Bullet reescrito",
        "add_bullet" => "Novo bullet",
        "add_skill" => $"Competência: {Skill}",
        "remove_skill" => $"Remover: {Skill}",
        _ => Type
    };
}

public static class ChatActions
{
    public const string OpenTag = "<vita-actions>";
    public const string CloseTag = "</vita-actions>";

    /// <summary>Splits the model answer into display text and machine-readable resume actions.</summary>
    public static (string Text, List<AiAction> Actions) Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (string.Empty, new List<AiAction>());
        var actions = new List<AiAction>();
        var text = raw;

        while (true)
        {
            var start = text.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            var end = text.IndexOf(CloseTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                text = text[..start];
                break;
            }
            var json = text[(start + OpenTag.Length)..end].Trim();
            foreach (var action in ParseList(json)) actions.Add(action);
            text = text[..start] + text[(end + CloseTag.Length)..];
        }

        // Models sometimes forget the tags but still append a bare JSON array of actions.
        if (actions.Count == 0)
        {
            var candidate = Json.ExtractJson(text);
            if (candidate is not null && candidate.StartsWith('[') &&
                candidate.Contains("\"type\"", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseList(candidate);
                if (parsed.Count > 0 && parsed.All(a => !string.IsNullOrWhiteSpace(a.Type)))
                {
                    actions.AddRange(parsed);
                    text = text.Replace(candidate, string.Empty);
                }
            }
        }

        return (text.Trim(), actions);
    }

    private static List<AiAction> ParseList(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<AiAction>>(json, Json.Options);
            if (list is null) return new List<AiAction>();
            return list.Where(a => !string.IsNullOrWhiteSpace(a.Type)).ToList();
        }
        catch
        {
            return new List<AiAction>();
        }
    }

    public static int Apply(ResumeDocument document, IEnumerable<AiAction> actions)
    {
        var applied = 0;
        foreach (var action in actions)
        {
            switch (action.Type.Trim().ToLowerInvariant())
            {
                case "summary" when !string.IsNullOrWhiteSpace(action.Text):
                    document.Summary = TextAnalysis.Clean(action.Text);
                    applied++;
                    break;
                case "headline" when !string.IsNullOrWhiteSpace(action.Text):
                    document.Contact.Headline = TextAnalysis.Clean(action.Text);
                    applied++;
                    break;
                case "update_bullet" when action.Experience is not null && action.Bullet is not null && !string.IsNullOrWhiteSpace(action.Text):
                    if (action.Experience >= 0 && action.Experience < document.Experience.Count)
                    {
                        var exp = document.Experience[action.Experience.Value];
                        if (action.Bullet >= 0 && action.Bullet < exp.Bullets.Count)
                        {
                            exp.Bullets[action.Bullet.Value] = TextAnalysis.Clean(action.Text);
                            applied++;
                        }
                    }
                    break;
                case "add_bullet" when action.Experience is not null && !string.IsNullOrWhiteSpace(action.Text):
                    if (action.Experience >= 0 && action.Experience < document.Experience.Count)
                    {
                        document.Experience[action.Experience.Value].Bullets.Add(TextAnalysis.Clean(action.Text));
                        applied++;
                    }
                    break;
                case "add_skill" when !string.IsNullOrWhiteSpace(action.Skill):
                    var group = document.SkillGroups.ElementAtOrDefault(action.Group ?? 0);
                    if (group is null)
                    {
                        group = new SkillGroup { Name = "Competências" };
                        document.SkillGroups.Add(group);
                    }
                    if (!group.Items.Contains(action.Skill, StringComparer.OrdinalIgnoreCase))
                    {
                        group.Items.Add(TextAnalysis.Clean(action.Skill));
                        applied++;
                    }
                    break;
                case "remove_skill" when !string.IsNullOrWhiteSpace(action.Skill):
                    var target = document.SkillGroups.ElementAtOrDefault(action.Group ?? 0);
                    if (target is not null && target.Items.RemoveAll(s => s.Equals(action.Skill, StringComparison.OrdinalIgnoreCase)) > 0)
                        applied++;
                    break;
            }
        }
        return applied;
    }
}

public sealed record ResumeDiffItem(string Section, string Label, string Before, string After, string Kind);

public static class ResumeDiff
{
    public static List<ResumeDiffItem> Compare(ResumeDocument before, ResumeDocument after)
    {
        var items = new List<ResumeDiffItem>();
        AddIfChanged("Contato", "Nome", before.Contact.FullName, after.Contact.FullName, items);
        AddIfChanged("Contato", "Título", before.Contact.Headline, after.Contact.Headline, items);
        AddIfChanged("Contato", "E-mail", before.Contact.Email, after.Contact.Email, items);
        AddIfChanged("Contato", "Telefone", before.Contact.Phone, after.Contact.Phone, items);
        AddIfChanged("Contato", "Localização", before.Contact.Location, after.Contact.Location, items);
        AddIfChanged("Resumo", "Resumo profissional", before.Summary, after.Summary, items);

        var maxExp = Math.Max(before.Experience.Count, after.Experience.Count);
        for (var i = 0; i < maxExp; i++)
        {
            var b = before.Experience.ElementAtOrDefault(i);
            var a = after.Experience.ElementAtOrDefault(i);
            if (b is null && a is not null)
            {
                items.Add(new("Experiência", $"{a.Role} · {a.Company}", "", "Novo cargo adicionado", "added"));
                continue;
            }
            if (b is not null && a is null)
            {
                items.Add(new("Experiência", $"{b.Role} · {b.Company}", "Cargo removido", "", "removed"));
                continue;
            }
            if (b is null || a is null) continue;
            AddIfChanged("Experiência", $"{a.Role} · Cargo", b.Role, a.Role, items);
            AddIfChanged("Experiência", $"{a.Role} · Empresa", b.Company, a.Company, items);
            AddIfChanged("Experiência", $"{a.Role} · Período", $"{b.StartDate}–{b.EndDate}", $"{a.StartDate}–{a.EndDate}", items);

            var maxBullets = Math.Max(b.Bullets.Count, a.Bullets.Count);
            for (var j = 0; j < maxBullets && items.Count < 28; j++)
            {
                var bb = b.Bullets.ElementAtOrDefault(j) ?? "";
                var ab = a.Bullets.ElementAtOrDefault(j) ?? "";
                if (string.Equals(bb, ab, StringComparison.Ordinal)) continue;
                var kind = string.IsNullOrWhiteSpace(bb) ? "added" : string.IsNullOrWhiteSpace(ab) ? "removed" : "changed";
                items.Add(new("Experiência", $"{a.Role} · bullet {j + 1}", bb, ab, kind));
            }
        }

        var beforeSkills = string.Join(", ", before.AllSkills);
        var afterSkills = string.Join(", ", after.AllSkills);
        AddIfChanged("Competências", "Lista de competências", beforeSkills, afterSkills, items);

        if (before.Education.Count != after.Education.Count)
            items.Add(new("Formação", "Itens de formação", before.Education.Count.ToString(), after.Education.Count.ToString(), "changed"));

        return items.Take(30).ToList();
    }

    private static void AddIfChanged(string section, string label, string? before, string? after, List<ResumeDiffItem> items)
    {
        var b = before?.Trim() ?? string.Empty;
        var a = after?.Trim() ?? string.Empty;
        if (string.Equals(b, a, StringComparison.Ordinal) || items.Count >= 30) return;
        items.Add(new(section, label, b, a, string.IsNullOrWhiteSpace(b) ? "added" : string.IsNullOrWhiteSpace(a) ? "removed" : "changed"));
    }
}
