namespace Vita.Core;

public sealed record TailorChange(string Section, string Title, string Before, string After, string Reason);

public sealed record TailorResult(
    ResumeDocument Document,
    List<TailorChange> Changes,
    KeywordCoverage Coverage,
    List<string> AddedKeywords);

public static class ResumeTailor
{
    /// <summary>
    /// Deterministic, offline tailoring. Never invents employers or dates: it re-orders
    /// skills, highlights matching keywords and writes a targeted summary.
    /// </summary>
    public static TailorResult ApplyLocal(ResumeDocument source, string jobDescription, string jobTitle = "", string company = "")
    {
        var doc = source.Clone();
        var changes = new List<TailorChange>();
        var coverage = TextAnalysis.Coverage(doc.ToPlainText(), jobDescription);
        var resumeText = doc.ToPlainText();

        var missingSkills = coverage.Missing
            .Where(k => k.Length > 1 && !resumeText.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        // 1) Skills: put strong matches first and append missing relevant keywords in a review group.
        var groups = doc.SkillGroups;
        if (groups.Count == 0 && (coverage.Matched.Count > 0 || missingSkills.Count > 0))
        {
            groups.Add(new SkillGroup { Name = "Competências" });
            changes.Add(new("Competências", "Grupo criado", "", "Competências", "A vaga pede competências que ainda não estavam organizadas no currículo."));
        }

        if (groups.Count > 0)
        {
            var primary = groups[0];
            var before = string.Join(", ", primary.Items);
            var ordered = primary.Items
                .OrderByDescending(item => coverage.Matched.Any(m => TextAnalysis.ContainsKeyword(item, m)))
                .ToList();
            primary.Items = ordered;

            if (missingSkills.Count > 0)
            {
                var reviewGroup = groups.FirstOrDefault(g => g.Name.Equals("A revisar", StringComparison.OrdinalIgnoreCase));
                if (reviewGroup is null)
                {
                    reviewGroup = new SkillGroup { Name = "A revisar" };
                    groups.Add(reviewGroup);
                }
                foreach (var skill in missingSkills)
                {
                    if (!reviewGroup.Items.Contains(skill, StringComparer.OrdinalIgnoreCase))
                        reviewGroup.Items.Add(skill);
                }
                changes.Add(new("Competências", "Palavras-chave da vaga", before,
                    string.Join(", ", ordered) + (reviewGroup.Items.Count > 0 ? " | A revisar: " + string.Join(", ", reviewGroup.Items) : ""),
                    "Termos da vaga foram sugeridos no grupo “A revisar”. Confirme apenas o que você domina."));
            }
            else if (before != string.Join(", ", ordered))
            {
                changes.Add(new("Competências", "Ordem das competências", before, string.Join(", ", ordered), "Competências que a vaga destaca aparecem primeiro."));
            }
        }

        // 2) Summary: keep the factual core and make the target explicit.
        var targetRole = TextAnalysis.FirstNonEmpty(jobTitle, doc.Contact.Headline);
        var targetCompany = TextAnalysis.Clean(company);
        var focus = coverage.Matched.Take(4).ToList();
        if (focus.Count == 0) focus = doc.AllSkills.Take(4).ToList();

        var originalSummary = TextAnalysis.Clean(doc.Summary);
        var pieces = new List<string>();
        if (!string.IsNullOrWhiteSpace(originalSummary))
            pieces.Add(originalSummary.TrimEnd('.') + ".");
        else if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
            pieces.Add($"{doc.Contact.Headline} com foco em resultados mensuráveis.");

        if (!string.IsNullOrWhiteSpace(targetRole))
        {
            var direction = string.IsNullOrWhiteSpace(targetCompany)
                ? $"Perfil direcionado para a posição de {targetRole}."
                : $"Perfil direcionado para a posição de {targetRole} na {targetCompany}.";
            if (!pieces.Any(p => p.Contains(direction, StringComparison.OrdinalIgnoreCase))) pieces.Add(direction);
        }
        if (focus.Count > 0)
            pieces.Add($"Competências em destaque: {string.Join(", ", focus)}.");

        var newSummary = string.Join(" ", pieces);
        if (TextAnalysis.WordCount(newSummary) > 130) newSummary = TextAnalysis.Truncate(newSummary, 900);

        if (!string.Equals(originalSummary, newSummary, StringComparison.Ordinal))
        {
            doc.Summary = newSummary;
            changes.Add(new("Resumo", "Resumo direcionado", originalSummary, newSummary, "Resumo alinhado ao título e às competências da vaga."));
        }

        // 3) Highlight bullets that already contain the job keywords (re-order within each role).
        foreach (var exp in doc.Experience)
        {
            if (exp.Bullets.Count < 2) continue;
            var orderedBullets = exp.Bullets
                .OrderByDescending(b => coverage.Matched.Count(m => TextAnalysis.ContainsKeyword(b, m)))
                .ToList();
            if (!orderedBullets.SequenceEqual(exp.Bullets))
            {
                changes.Add(new("Experiência", $"Ordem dos bullets em {exp.Role}", "", "", "Realizações ligadas à vaga foram priorizadas no topo do cargo."));
                exp.Bullets = orderedBullets;
            }
        }

        return new TailorResult(doc, changes, coverage, missingSkills);
    }
}

public sealed record JobMatch(int Score, List<string> Matched, List<string> Missing, string Rationale);

public static class JobMatcher
{
    public static JobMatch Score(ResumeDocument resume, string title, string description, string? location = null)
    {
        var resumeText = resume.ToPlainText();
        var coverage = TextAnalysis.Coverage(resumeText, $"{title}\n{description}", 22);
        var resumeTitle = TextAnalysis.FirstNonEmpty(resume.Contact.Headline, resume.Experience.FirstOrDefault()?.Role);
        var jobTokens = TextAnalysis.Tokenize(title)
            .Where(t => t.Length > 2 && !TextAnalysis.IsLevelWord(t))
            .Distinct()
            .ToList();
        var resumeTokens = TextAnalysis.Tokenize(resumeTitle)
            .Where(t => t.Length > 2 && !TextAnalysis.IsLevelWord(t))
            .Distinct()
            .ToList();
        var titleHits = jobTokens.Count(t => resumeTokens.Any(r => TextAnalysis.TokenEquivalent(r, t)));
        var titleOverlap = jobTokens.Count == 0 ? 0 : Math.Min(1.0, (double)titleHits / jobTokens.Count);
        var titleScore = (int)Math.Round(34 * titleOverlap);
        if (TextAnalysis.SharesRoleNoun(title, resumeTitle))
            titleScore += 8;

        var skillScore = (int)Math.Round(48.0 * coverage.Percent / 100.0);
        var seniority = SeniorityBonus(resumeText, title);
        var locationScore = 0;
        if (!string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(resume.Contact.Location))
        {
            var city = TextAnalysis.Normalize(resume.Contact.Location).Split(',').FirstOrDefault()?.Trim() ?? "";
            if (city.Length > 2 && TextAnalysis.ContainsKeyword(location, city)) locationScore = 8;
            else if (TextAnalysis.ContainsKeyword(location, "remote") || TextAnalysis.ContainsKeyword(location, "remoto")) locationScore = 5;
        }
        var score = Math.Clamp(titleScore + skillScore + seniority + locationScore, 5, 99);

        var rationale = score switch
        {
            >= 85 => "Excelente aderência: cargo e competências principais batem com seu perfil.",
            >= 70 => "Boa aderência: você atende a maior parte dos requisitos.",
            >= 55 => "Aderência parcial: vale adaptar o currículo antes de aplicar.",
            _ => "Baixa aderência: a vaga exige competências que ainda não aparecem no currículo."
        };
        return new JobMatch(score, coverage.Matched, coverage.Missing.Take(8).ToList(), rationale);
    }

    private static int SeniorityBonus(string resumeText, string title)
    {
        var t = TextAnalysis.Normalize(title);
        var r = TextAnalysis.Normalize(resumeText);
        var levels = new (string Key, string[] Synonyms)[]
        {
            ("estagio", new[] { "estagio", "intern", "trainee" }),
            ("junior", new[] { "junior", "jr" }),
            ("pleno", new[] { "pleno", "mid-level", "mid level" }),
            ("senior", new[] { "senior", "sr" }),
            ("especialista", new[] { "especialista", "specialist", "staff", "principal", "lead", "lider" })
        };
        var jobLevel = Array.FindIndex(levels, l => l.Synonyms.Any(t.Contains));
        var resumeLevel = Array.FindIndex(levels, l => l.Synonyms.Any(r.Contains));
        if (jobLevel < 0 || resumeLevel < 0) return 6;
        var diff = Math.Abs(jobLevel - resumeLevel);
        return diff == 0 ? 12 : diff == 1 ? 8 : 2;
    }
}

public static class CoverLetterTemplates
{
    public static string Generate(ResumeDocument resume, string company, string role, string? jobDescription, string language = "pt", string tone = "profissional")
    {
        var isEn = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var isEs = language.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        var foco = TextAnalysis.ExtractKeywords(jobDescription, 8).Where(k => TextAnalysis.ContainsKeyword(resume.ToPlainText(), k)).Take(4).ToList();
        if (foco.Count == 0) foco = resume.AllSkills.Take(4).ToList();
        var last = resume.Experience.FirstOrDefault();
        var name = TextAnalysis.FirstNonEmpty(resume.Contact.FullName, isEn ? "Your name" : "Seu nome");
        var skills = string.Join(", ", foco.Take(foco.Count - 1));
        var lastSkill = foco.Count > 1 ? foco[^1] : "";
        var focusText = foco.Count switch
        {
            0 => isEn ? "delivering measurable results" : "entregar resultados mensuráveis",
            1 => foco[0],
            _ => $"{skills}{(isEn ? " and " : isEs ? " y " : " e ")}{lastSkill}"
        };

        if (isEn)
        {
            var expLine = last is null
                ? "I have built my career around solving real problems with focus on execution and measurable impact."
                : $"At {last.Company}, as {last.Role}, I worked on initiatives that strengthened my expertise in {focusText}.";
            return $@"Dear Hiring Team{(string.IsNullOrWhiteSpace(company) ? "" : $" at {company}")},

I am applying for the {role} position. {expLine}

What draws me to this role is the combination of scope and impact: the challenges described in the posting match the work I enjoy most — turning ambiguous problems into structured plans, collaborating across teams and delivering results that can be measured. My experience with {focusText} is directly relevant to what you are looking for.

I would welcome the opportunity to discuss how I can contribute to {company}. Thank you for your time and consideration.

Sincerely,
{name}";
        }

        if (isEs)
        {
            var expLine = last is null
                ? "He construido mi carrera resolviendo problemas reales, con foco en la ejecución y en resultados medibles."
                : $"En {last.Company}, como {last.Role}, participé en iniciativas que fortalecieron mi experiencia en {focusText}.";
            return $@"Estimado equipo de selección{(string.IsNullOrWhiteSpace(company) ? "" : $" de {company}")}:

Me postulo a la posición de {role}. {expLine}

Lo que me atrae de esta oportunidad es la combinación de alcance e impacto: los desafíos descritos coinciden con el trabajo que más disfruto — convertir problemas ambiguos en planes estructurados, colaborar entre equipos y entregar resultados medibles. Mi experiencia en {focusText} es directamente relevante para lo que buscan.

Quedo a disposición para conversar sobre cómo puedo contribuir con {company}. Gracias por su tiempo y consideración.

Atentamente,
{name}";
        }

        var expPt = last is null
            ? "Construí minha carreira resolvendo problemas reais, com foco em execução e em resultados mensuráveis."
            : $"Na {last.Company}, como {last.Role}, participei de iniciativas que fortaleceram minha experiência em {focusText}.";
        return $@"Prezado(a) time de recrutamento{(string.IsNullOrWhiteSpace(company) ? "" : $" da {company}")},

Candidato-me à vaga de {role}. {expPt}

O que me atrai nesta oportunidade é a combinação de escopo e impacto: os desafios descritos na vaga conversam diretamente com o trabalho que mais gosto de fazer — transformar problemas ambíguos em planos estruturados, colaborar com áreas diferentes e entregar resultados que podem ser medidos. Minha experiência com {focusText} é diretamente relevante para o que vocês buscam.

Fico à disposição para conversar sobre como posso contribuir com a {company}. Agradeço pelo tempo e pela consideração.

Atenciosamente,
{name}";
    }
}
