using System.Runtime.CompilerServices;
using System.Text;

namespace Vita.App.Services;

public sealed record AiSuggestion(string Section, string Severity, string Title, string Detail, string Example);

public sealed record ResumeEditResult(ResumeDocument Document, List<ResumeDiffItem> Changes);

public sealed class AiAssistantService(AiClient ai, AiSettingsService settings)
{
    public async IAsyncEnumerable<string> ChatStreamAsync(
        Guid userId,
        ResumeDocument? resume,
        string? jobDescription,
        IReadOnlyList<AiMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new List<AiMessage> { new("system", Prompts.ChatSystem(resume, jobDescription)) };
        messages.AddRange(history.TakeLast(16));
        await foreach (var chunk in ai.StreamChatAsync(config, messages, json: false, ct))
            yield return chunk;
    }

    public async Task<List<AiSuggestion>> ReviewAsync(Guid userId, ResumeDocument resume, string? jobDescription, CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new[]
        {
            new AiMessage("system", Prompts.ReviewSystem),
            new AiMessage("user", Prompts.ReviewUser(resume, jobDescription))
        };
        var raw = await ai.CompleteAsync(config, messages, json: true, ct);
        var json = Json.ExtractJson(raw);
        if (json is null) throw new AiException("A IA não retornou sugestões em formato válido. Tente novamente.");
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<AiSuggestion>>(json, Json.Options) ?? new();
            return list.Where(s => !string.IsNullOrWhiteSpace(s.Title)).Take(8).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            throw new AiException("Não foi possível interpretar as sugestões da IA. Tente novamente.");
        }
    }

    public async Task<ResumeEditResult> EditAsync(Guid userId, ResumeDocument resume, string instruction,
        string? jobDescription = null, CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new[]
        {
            new AiMessage("system", Prompts.EditSystem(resume, jobDescription)),
            new AiMessage("user", instruction)
        };
        var raw = await ai.CompleteAsync(config, messages, json: true, ct);
        var json = Json.ExtractJson(raw);
        if (json is null) throw new AiException("A IA não retornou um currículo válido. Tente novamente com uma instrução mais específica.");
        ResumeDocument? updated;
        try { updated = System.Text.Json.JsonSerializer.Deserialize<ResumeDocument>(json, Json.Options); }
        catch (System.Text.Json.JsonException) { updated = null; }
        if (updated is null || updated.IsEmpty)
            throw new AiException("A IA retornou uma versão incompleta do currículo. Tente novamente.");

        ResumeNormalizer.Normalize(updated);
        var changes = ResumeDiff.Compare(resume, updated);
        if (changes.Count == 0) throw new AiException("A IA não propôs mudanças concretas. Tente descrever melhor o ajuste desejado.");
        return new ResumeEditResult(updated, changes);
    }

    public async Task<ResumeDocument?> ParseResumeAsync(Guid userId, string rawText, CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new[]
        {
            new AiMessage("system", Prompts.ParseSystem),
            new AiMessage("user", TextAnalysis.Truncate(rawText, 18000))
        };
        var raw = await ai.CompleteAsync(config, messages, json: true, ct);
        var json = Json.ExtractJson(raw);
        if (json is null) return null;
        var doc = Json.Deserialize<ResumeDocument>(json);
        if (doc is null) return null;
        ResumeNormalizer.Normalize(doc);
        return doc.IsEmpty ? null : doc;
    }

    public async Task<string?> CoverLetterAsync(Guid userId, ResumeDocument resume, string company, string role,
        string? jobDescription, string language, string tone, CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new[]
        {
            new AiMessage("system", Prompts.CoverLetterSystem),
            new AiMessage("user", Prompts.CoverLetterUser(resume, company, role, jobDescription, language, tone))
        };
        var raw = await ai.CompleteAsync(config, messages, json: false, ct);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    public async Task<string?> ImproveTextAsync(Guid userId, string text, string context, string? jobDescription, CancellationToken ct = default)
    {
        var config = await settings.GetConfigAsync(userId, ct);
        var messages = new[]
        {
            new AiMessage("system", Prompts.ImproveSystem),
            new AiMessage("user", $"Contexto: {context}\n\nDescrição da vaga (se houver):\n{TextAnalysis.Truncate(jobDescription, 2500)}\n\nTexto atual:\n{text}\n\nDevolva apenas o texto melhorado.")
        };
        var raw = await ai.CompleteAsync(config, messages, json: false, ct);
        return string.IsNullOrWhiteSpace(raw) ? null : TextAnalysis.Clean(raw);
    }
}

internal static class Prompts
{
    public const string ReviewSystem =
        "Você é um recrutador sênior e especialista em ATS. Analise o currículo e devolva SOMENTE um JSON válido " +
        "no formato: [{\"section\":\"Resumo|Experiência|Competências|Contato|Formação|Estrutura\",\"severity\":\"critical|warning|tip\"," +
        "\"title\":\"frase curta\",\"detail\":\"o que está errado e por quê\",\"example\":\"exemplo pronto de correção\"}]. " +
        "Máximo de 8 itens, priorizando o que mais impacta a triagem. Seja específico e nunca invente experiências. Responda em português do Brasil.";

    public static string ReviewUser(ResumeDocument resume, string? jobDescription) =>
        $"Currículo (JSON):\n{Json.Serialize(resume)}\n\n" +
        (string.IsNullOrWhiteSpace(jobDescription) ? "Sem vaga específica: avalie boas práticas gerais." : $"Vaga alvo:\n{TextAnalysis.Truncate(jobDescription, 5000)}");

    public static string ChatSystem(ResumeDocument? resume, string? jobDescription)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você é a Vita, uma assistente de carreira especialista em currículos, ATS e processos seletivos.");
        sb.AppendLine("Responda em português do Brasil, de forma direta e prática. Use no máximo 220 palavras, com bullets curtos quando ajudar.");
        sb.AppendLine("Nunca invente empresas, cargos, datas, formações ou resultados. Se faltar informação, pergunte.");
        sb.AppendLine("Você pode propor mudanças concretas no currículo. Quando fizer isso, termine a resposta com um bloco de ações EXATAMENTE neste formato:");
        sb.AppendLine("<vita-actions>[{\"type\":\"summary\",\"text\":\"novo resumo\"},{\"type\":\"headline\",\"text\":\"novo título\"},{\"type\":\"update_bullet\",\"experience\":0,\"bullet\":1,\"text\":\"bullet reescrito\"},{\"type\":\"add_bullet\",\"experience\":0,\"text\":\"novo bullet\"},{\"type\":\"add_skill\",\"group\":0,\"skill\":\"Figma\"}]</vita-actions>");
        sb.AppendLine("Use apenas as ações necessárias e só inclua o bloco se houver mudanças reais. Não explique o bloco na resposta.");
        sb.AppendLine("Não use tabelas em markdown.");
        sb.AppendLine();
        sb.AppendLine("Currículo atual do usuário (JSON):");
        sb.AppendLine(resume is null ? "Nenhum currículo selecionado." : Json.Serialize(resume));
        if (!string.IsNullOrWhiteSpace(jobDescription))
        {
            sb.AppendLine();
            sb.AppendLine("Vaga alvo (descrição):");
            sb.AppendLine(TextAnalysis.Truncate(jobDescription, 6000));
        }
        return sb.ToString();
    }

    public static string EditSystem(ResumeDocument resume, string? jobDescription)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você é um editor de currículos. Receberá um currículo em JSON e uma instrução. Devolva SOMENTE o JSON completo do currículo atualizado, sem comentários e sem markdown.");
        sb.AppendLine("Regras obrigatórias:");
        sb.AppendLine("1. Preserve todos os fatos: empresas, cargos, datas, formações e idiomas. Nunca invente experiências ou números.");
        sb.AppendLine("2. Você pode reescrever textos, adicionar/remover bullets, reorganizar competências e ajustar o título profissional.");
        sb.AppendLine("3. Se já existirem métricas nos textos, mantenha-as. Se a instrução pedir quantificar e não houver dados, use linguagem qualitativa em vez de inventar.");
        sb.AppendLine("4. Mantenha o mesmo schema e os mesmos idiomais do currículo. Não apague seções sem relação com a instrução.");
        sb.AppendLine("5. Datas no formato AAAA-MM; use \"\" quando desconhecido.");
        sb.AppendLine();
        sb.AppendLine("Schema exato:");
        sb.AppendLine(SchemaHint);
        sb.AppendLine();
        sb.AppendLine("Currículo atual:");
        sb.AppendLine(Json.Serialize(resume));
        if (!string.IsNullOrWhiteSpace(jobDescription))
        {
            sb.AppendLine();
            sb.AppendLine("Vaga alvo:");
            sb.AppendLine(TextAnalysis.Truncate(jobDescription, 6000));
        }
        return sb.ToString();
    }

    public const string ParseSystem =
        "Você converte currículos em texto para JSON estruturado. Devolva SOMENTE o JSON, sem markdown. " +
        "Não invente informações ausentes: deixe strings vazias e listas vazias. Preserve os textos originais (pode corrigir apenas espaçamento). " +
        "Use o schema: " + SchemaHint;

    public const string CoverLetterSystem =
        "Você escreve cartas de apresentação autênticas, específicas e sem clichês. " +
        "Nunca invente experiências, empresas, formações ou métricas. Use apenas fatos do currículo. " +
        "Devolva apenas o corpo da carta, sem assunto, sem markdown e sem explicações. Entre 180 e 280 palavras.";

    public static string CoverLetterUser(ResumeDocument resume, string company, string role, string? jd, string language, string tone)
    {
        var lang = language switch { "en" => "inglês", "es" => "espanhol", _ => "português do Brasil" };
        return $"Idioma da carta: {lang}\nTom: {tone}\nEmpresa: {company}\nVaga: {role}\n\n" +
               $"Currículo:\n{Json.Serialize(resume)}\n\n" +
               (string.IsNullOrWhiteSpace(jd) ? "" : $"Descrição da vaga:\n{TextAnalysis.Truncate(jd, 5000)}\n\n") +
               $"Escreva a carta começando com uma saudação adequada e terminando com o nome completo ({resume.Contact.FullName}).";
    }

    public const string ImproveSystem =
        "Você é um editor de currículos. Reescreva o texto recebido para maximizar clareza, impacto e aderência a ATS. " +
        "Use verbos de ação, frases curtas e resultados já presentes no texto. Nunca invente números, empresas ou experiências. " +
        "Devolva apenas o texto final, sem explicações, sem markdown, sem aspas.";

    public const string SchemaHint =
        "{\"contact\":{\"fullName\":\"\",\"headline\":\"\",\"email\":\"\",\"phone\":\"\",\"location\":\"\",\"website\":\"\",\"linkedin\":\"\",\"github\":\"\"}," +
        "\"summary\":\"\",\"experience\":[{\"role\":\"\",\"company\":\"\",\"location\":\"\",\"startDate\":\"AAAA-MM\",\"endDate\":\"AAAA-MM\",\"current\":false,\"bullets\":[\"\"]}]," +
        "\"education\":[{\"institution\":\"\",\"degree\":\"\",\"field\":\"\",\"location\":\"\",\"startDate\":\"\",\"endDate\":\"\",\"current\":false,\"details\":\"\"}]," +
        "\"skillGroups\":[{\"name\":\"\",\"items\":[\"\"]}],\"projects\":[{\"name\":\"\",\"link\":\"\",\"description\":\"\",\"bullets\":[\"\"]}]," +
        "\"certifications\":[{\"name\":\"\",\"issuer\":\"\",\"year\":\"\"}],\"languages\":[{\"name\":\"\",\"level\":\"\"}],\"customSections\":[{\"title\":\"\",\"items\":[\"\"]}]}";
}

public static class ResumeNormalizer
{
    public static void Normalize(ResumeDocument doc)
    {
        doc.Contact.FullName = TextAnalysis.Clean(doc.Contact.FullName);
        doc.Contact.Headline = TextAnalysis.Clean(doc.Contact.Headline);
        doc.Contact.Email = TextAnalysis.Clean(doc.Contact.Email);
        doc.Contact.Phone = TextAnalysis.Clean(doc.Contact.Phone);
        doc.Contact.Location = TextAnalysis.Clean(doc.Contact.Location);
        doc.Contact.Website = TextAnalysis.Clean(doc.Contact.Website);
        doc.Contact.LinkedIn = TextAnalysis.Clean(doc.Contact.LinkedIn);
        doc.Contact.Github = TextAnalysis.Clean(doc.Contact.Github);
        doc.Summary = TextAnalysis.Clean(doc.Summary);

        doc.Experience = doc.Experience
            .Where(e => !string.IsNullOrWhiteSpace(e.Role) || !string.IsNullOrWhiteSpace(e.Company))
            .Select(e =>
            {
                e.Role = TextAnalysis.Clean(e.Role);
                e.Company = TextAnalysis.Clean(e.Company);
                e.Location = TextAnalysis.Clean(e.Location);
                e.StartDate = CleanDate(e.StartDate);
                e.EndDate = CleanDate(e.EndDate);
                e.Bullets = e.Bullets.Where(b => !string.IsNullOrWhiteSpace(b)).Select(TextAnalysis.Clean).ToList();
                return e;
            }).ToList();

        doc.Education = doc.Education.Where(e => !string.IsNullOrWhiteSpace(e.Institution)).ToList();
        foreach (var e in doc.Education)
        {
            e.Institution = TextAnalysis.Clean(e.Institution);
            e.Degree = TextAnalysis.Clean(e.Degree);
            e.Field = TextAnalysis.Clean(e.Field);
            e.Details = TextAnalysis.Clean(e.Details);
        }

        doc.SkillGroups = doc.SkillGroups
            .Where(g => g.Items.Count > 0)
            .Select(g => new SkillGroup
            {
                Name = string.IsNullOrWhiteSpace(g.Name) ? "Competências" : TextAnalysis.Clean(g.Name),
                Items = g.Items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(TextAnalysis.Clean).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            })
            .Where(g => g.Items.Count > 0)
            .ToList();

        doc.Projects = doc.Projects.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        doc.Certifications = doc.Certifications.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        doc.Languages = doc.Languages.Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();
        doc.CustomSections = doc.CustomSections.Where(s => s.Items.Count > 0).ToList();
    }

    private static string CleanDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{4}-\d{2}$")) return v;
        var m = System.Text.RegularExpressions.Regex.Match(v, @"(\d{4})[-/.](\d{1,2})");
        if (m.Success && int.TryParse(m.Groups[2].Value, out var month) && month is >= 1 and <= 12)
            return $"{m.Groups[1].Value}-{month:00}";
        return v;
    }
}
