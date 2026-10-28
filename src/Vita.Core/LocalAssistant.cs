using System.Text;
using System.Text.RegularExpressions;

namespace Vita.Core;

public sealed record LocalChatReply(string Text, List<AiAction> Actions);

/// <summary>
/// Deterministic assistant used when no AI provider is available. It only reorganizes
/// facts that already exist in the resume, and every change still needs user approval.
/// </summary>
public static class LocalAssistant
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public static LocalChatReply Suggest(ResumeDocument? resume, string prompt, string? jobDescription)
    {
        var p = TextAnalysis.Normalize(prompt);
        if (resume is null)
            return new("Selecione um currículo para eu montar uma proposta de mudança. Ainda dá para analisar texto colado na página de ATS.",
                new List<AiAction>());

        var actions = new List<AiAction>();
        var notes = new List<string>();
        var wantsKeywords = Has(p, "palavra", "keyword", "ats", "vaga", "descri", "aderencia", "aderência");
        var wantsMetrics = Has(p, "metrica", "métrica", "numero", "número", "impacto", "resultado");
        var wantsConcise = Has(p, "conciso", "curto", "resumid", "enxut");
        var wantsProfessional = Has(p, "profissional", "tom", "formal", "serio", "sério");
        var wantsSummary = Has(p, "resumo", "summary", "direcion", "posicion");

        if (wantsKeywords)
        {
            if (string.IsNullOrWhiteSpace(jobDescription))
            {
                notes.Add("Para comparar palavras-chave, cole a descrição da vaga no campo **Vaga alvo** do painel ao lado. Sem isso, eu não invento requisitos.");
            }
            else
            {
                var coverage = TextAnalysis.Coverage(resume.ToPlainText(), jobDescription, 24);
                var additions = coverage.Missing
                    .Where(k => k.Length > 2 && !k.All(char.IsDigit))
                    .Take(3)
                    .ToList();
                foreach (var keyword in additions)
                    actions.Add(new AiAction { Type = "add_skill", Skill = keyword, Group = 0 });

                if (additions.Count > 0)
                    notes.Add($"Encontrei {coverage.Missing.Count} termo(s) da vaga que ainda não aparecem no currículo. Coloquei os mais relevantes como competências para você confirmar: {string.Join(", ", additions)}.");
                else if (coverage.Matched.Count > 0)
                    notes.Add($"As palavras-chave principais da vaga já aparecem no currículo ({string.Join(", ", coverage.Matched.Take(6))}). O ganho agora é de posicionamento, não de cobertura.");
                else
                    notes.Add("Não encontrei termos confiáveis para comparar. Revise a descrição da vaga e tente novamente.");
            }
        }

        if (wantsMetrics)
        {
            var withoutNumbers = resume.Experience
                .SelectMany((exp, index) => exp.Bullets.Select((bullet, bulletIndex) => new { exp, index, bullet, bulletIndex }))
                .Where(item => !TextAnalysis.HasMetric(item.bullet))
                .Take(3)
                .ToList();
            if (withoutNumbers.Count == 0)
            {
                notes.Add("Todos os bullets visíveis já têm algum número ou percentual. Não vou inventar métricas novas; revise os resultados para garantir que sejam reais.");
            }
            else
            {
                var list = string.Join("; ", withoutNumbers.Select(item => $"{item.exp.Role}: “{TextAnalysis.Truncate(item.bullet, 90)}”"));
                notes.Add($"Não vou inventar números. Estes bullets ainda não mostram resultado mensurável: {list}. Abra o editor e acrescente o dado real (tempo, percentual, volume ou valor).");
            }
        }

        var rewriteSummary = wantsSummary || wantsConcise || wantsProfessional || (!wantsKeywords && !wantsMetrics);
        if (rewriteSummary)
        {
            var before = TextAnalysis.Clean(resume.Summary);
            var after = BuildSummary(resume, wantsConcise, wantsProfessional);
            if (!string.Equals(before, after, StringComparison.Ordinal) && after.Length > 20)
            {
                actions.Add(new AiAction { Type = "summary", Text = after });
                notes.Add(wantsConcise
                    ? "Preparei uma versão mais curta do resumo, mantendo apenas o que já estava escrito."
                    : wantsProfessional
                        ? "Ajustei o resumo para um tom mais direto e profissional, sem criar experiências novas."
                        : "Reescrevi o resumo com o título e as competências que já existem no documento.");
            }
            else if (!string.IsNullOrWhiteSpace(before))
            {
                notes.Add("Seu resumo já está próximo do formato que eu proporia. Se quiser, peça um recorte específico (por exemplo: “mais curto” ou “mais focado em liderança”).");
            }
        }

        if (actions.Count == 0 && notes.Count == 0)
            notes.Add("Entendi o pedido, mas não encontrei uma mudança segura para propor sem inventar informação. Tente pedir algo como “deixe o resumo mais curto” ou “compare com a vaga alvo”.");

        var text = new StringBuilder();
        text.AppendLine("**Modo local (sem IA).** Trabalhei apenas com o conteúdo que já está no currículo.");
        foreach (var note in notes)
            text.AppendLine().AppendLine(note);
        if (actions.Count > 0)
            text.AppendLine().AppendLine("Revise as mudanças abaixo e aplique só o que fizer sentido.");

        return new LocalChatReply(text.ToString().Trim(), actions);
    }

    private static string BuildSummary(ResumeDocument resume, bool concise, bool professional)
    {
        var original = TextAnalysis.Clean(resume.Summary);
        var skills = resume.AllSkills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(original))
        {
            var sentences = SentenceSplit.Split(original)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            var keep = concise ? 1 : Math.Min(2, sentences.Count);
            var core = string.Join(" ", sentences.Take(keep)).Trim();
            if (core.Length > 0) parts.Add(core);
        }

        if (parts.Count == 0)
        {
            var headline = TextAnalysis.FirstNonEmpty(resume.Contact.Headline, resume.Experience.FirstOrDefault()?.Role);
            if (!string.IsNullOrWhiteSpace(headline))
                parts.Add(headline);
        }

        if (!concise && skills.Count > 0)
            parts.Add($"Competências em destaque: {string.Join(", ", skills)}.");

        if (professional && parts.Count > 0)
        {
            parts[0] = parts[0]
                .Replace("!", ".", StringComparison.Ordinal)
                .Replace("  ", " ", StringComparison.Ordinal)
                .Trim();
        }

        var summary = TextAnalysis.Clean(string.Join(" ", parts));
        if (summary.Length > 0 && !summary.EndsWith('.') && !summary.EndsWith('!') && !summary.EndsWith('?'))
            summary += ".";
        if (summary.Length > 900) summary = TextAnalysis.Truncate(summary, 900);
        return summary;
    }

    private static bool Has(string normalized, params string[] terms) =>
        terms.Any(term => normalized.Contains(TextAnalysis.Normalize(term), StringComparison.Ordinal));
}
