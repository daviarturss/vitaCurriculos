namespace Vita.Core;

public sealed record ScoreIssue(string Section, string Severity, string Title, string Detail, string Fix);

public sealed record ScoreCategory(string Key, string Name, int Score, int Max, string Summary);

public sealed record ResumeScore(
    int Total,
    string Label,
    string Verdict,
    IReadOnlyList<ScoreCategory> Categories,
    IReadOnlyList<ScoreIssue> Issues,
    KeywordCoverage? Coverage)
{
    public string Tone => Total >= 80 ? "great" : Total >= 60 ? "good" : Total >= 45 ? "warn" : "bad";
}

public static class ResumeScorer
{
    public static ResumeScore Analyze(ResumeDocument doc, string? jobDescription = null)
    {
        var issues = new List<ScoreIssue>();
        var categories = new List<ScoreCategory>();
        var fullText = doc.ToPlainText();
        var coverage = string.IsNullOrWhiteSpace(jobDescription)
            ? null
            : TextAnalysis.Coverage(fullText, jobDescription);

        // ---------------- Contact ----------------
        var contact = doc.Contact;
        var contactPts = 0;
        if (!string.IsNullOrWhiteSpace(contact.FullName)) contactPts += 2;
        else issues.Add(new("Contato", "critical", "Nome ausente", "Recrutadores precisam identificar você imediatamente.", "Preencha seu nome completo no topo do currículo."));
        if (TextAnalysis.LooksLikeEmail(contact.Email)) contactPts += 2;
        else issues.Add(new("Contato", "critical", "E-mail ausente ou inválido", "Sem um e-mail válido o recrutador não consegue responder.", "Use um e-mail profissional, como nome.sobrenome@email.com."));
        if (TextAnalysis.LooksLikePhone(contact.Phone)) contactPts += 2;
        else issues.Add(new("Contato", "warning", "Telefone incompleto", "Um telefone com DDD acelera o contato.", "Informe o telefone com código do país e DDD."));
        if (!string.IsNullOrWhiteSpace(contact.Location)) contactPts += 2;
        else issues.Add(new("Contato", "warning", "Localização ausente", "Cidade e país ajudam o recrutador a avaliar logística.", "Adicione cidade, estado e país."));
        if (TextAnalysis.LooksLikeLink(contact.LinkedIn) || TextAnalysis.LooksLikeLink(contact.Website) || TextAnalysis.LooksLikeLink(contact.Github)) contactPts += 2;
        else issues.Add(new("Contato", "tip", "Nenhum link profissional", "LinkedIn ou portfólio aumentam a credibilidade.", "Adicione seu LinkedIn, GitHub ou portfólio."));
        categories.Add(new("contact", "Contato", contactPts, 10, contactPts >= 8 ? "Dados completos e fáceis de encontrar." : "Faltam dados para o recrutador falar com você."));

        // ---------------- Summary ----------------
        var summaryWords = TextAnalysis.WordCount(doc.Summary);
        var summaryPts = 0;
        if (summaryWords > 0) summaryPts += 5;
        else issues.Add(new("Resumo", "critical", "Sem resumo profissional", "O topo do currículo é a primeira coisa que o recrutador lê.", "Escreva de 3 a 5 linhas resumindo sua especialidade e principais resultados."));
        if (summaryWords is >= 30 and <= 120) summaryPts += 4;
        else if (summaryWords is > 0 and < 30) issues.Add(new("Resumo", "warning", "Resumo muito curto", "Menos de 30 palavras não comunica seu diferencial.", "Inclua especialidade, anos de experiência e um resultado concreto."));
        else if (summaryWords > 120) issues.Add(new("Resumo", "warning", "Resumo muito longo", "Textos longos são pulados na triagem.", "Reduza para no máximo 120 palavras e foque no que importa para a vaga."));
        if (summaryWords > 0 && !System.Text.RegularExpressions.Regex.IsMatch(doc.Summary, @"\b(eu sou|eu tenho|i am|i'm|my name is)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) summaryPts += 2;
        else if (summaryWords > 0) issues.Add(new("Resumo", "tip", "Evite falar em primeira pessoa", "Currículos funcionam melhor com frases diretas.", "Troque “Eu sou…” por “Profissional com…”."));
        var headline = doc.Contact.Headline;
        if (!string.IsNullOrWhiteSpace(headline)) summaryPts += 3;
        else issues.Add(new("Resumo", "warning", "Título profissional ausente", "O cargo-alvo logo abaixo do nome orienta a leitura.", "Adicione um título alinhado à vaga, por exemplo “Analista de Dados Sênior”."));
        categories.Add(new("summary", "Resumo", summaryPts, 14, summaryPts >= 11 ? "Síntese clara e direcionada." : "O resumo ainda não vende seu perfil."));

        // ---------------- Experience ----------------
        var expPts = 0;
        var bullets = doc.Experience.SelectMany(e => e.Bullets).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        if (doc.Experience.Count > 0) expPts += 8;
        else issues.Add(new("Experiência", "critical", "Nenhuma experiência cadastrada", "Esta é a seção mais importante para a triagem.", "Adicione ao menos uma experiência, projeto ou freelance com resultados."));
        var rolesWithThree = doc.Experience.Count(e => e.Bullets.Count(b => !string.IsNullOrWhiteSpace(b)) >= 3);
        if (doc.Experience.Count > 0)
        {
            if (rolesWithThree == doc.Experience.Count) expPts += 6;
            else if (rolesWithThree > 0) expPts += 4;
            else expPts += 1;
            if (rolesWithThree < doc.Experience.Count)
                issues.Add(new("Experiência", "warning", "Poucas realizações por cargo", "Três a cinco bullets por cargo mostram escopo e resultados.", "Descreva o que você fez, como fez e o impacto gerado em cada posição."));
        }
        var quantified = bullets.Count(TextAnalysis.HasMetric);
        var quantRatio = bullets.Count == 0 ? 0 : (double)quantified / bullets.Count;
        if (quantRatio >= 0.5) expPts += 8;
        else if (quantRatio >= 0.25) expPts += 5;
        else if (quantified > 0) expPts += 3;
        else if (bullets.Count > 0) issues.Add(new("Experiência", "warning", "Nenhum resultado quantificado", "Números dão credibilidade às suas conquistas.", "Adicione métricas: % de crescimento, economia, receita, volume ou prazo."));
        var withVerb = bullets.Count(TextAnalysis.HasActionVerb);
        if (bullets.Count > 0 && withVerb >= bullets.Count * 0.6) expPts += 4;
        else if (withVerb > 0) expPts += 2;
        else if (bullets.Count > 0) issues.Add(new("Experiência", "tip", "Bullets sem verbos de ação", "Comece cada frase com um verbo forte (“Liderei”, “Reduzi”, “Implementei”).", "Reescreva os bullets começando por verbos de impacto."));
        var dated = doc.Experience.Count(e => !string.IsNullOrWhiteSpace(e.StartDate));
        if (doc.Experience.Count > 0 && dated == doc.Experience.Count) expPts += 4;
        else if (dated > 0) expPts += 2;
        if (dated > 0 && dated < doc.Experience.Count)
            issues.Add(new("Experiência", "warning", "Datas incompletas", "Lacunas de tempo levantam dúvidas na triagem.", "Informe mês e ano de início e fim de cada experiência."));
        categories.Add(new("experience", "Experiência", expPts, 30, expPts >= 24 ? "Histórico forte, com escopo e impacto." : "Há espaço para tornar suas conquistas mais concretas."));

        // ---------------- Skills ----------------
        var skills = doc.AllSkills.ToList();
        var skillPts = 0;
        if (skills.Count >= 8) skillPts += 6;
        else if (skills.Count >= 4) skillPts += 3;
        else issues.Add(new("Competências", "warning", "Poucas competências listadas", "Sistemas de triagem buscam palavras-chave específicas.", "Liste de 8 a 15 competências técnicas e comportamentais relevantes."));
        if (doc.SkillGroups.Count > 1) skillPts += 3;
        else if (doc.SkillGroups.Count == 1) skillPts += 1;
        if (coverage is not null)
        {
            skillPts += (int)Math.Round(7.0 * coverage.Percent / 100.0);
            if (coverage.Percent < 60 && coverage.Missing.Count > 0)
                issues.Add(new("Competências", "warning", "Palavras-chave da vaga ausentes",
                    $"A vaga destaca termos que não aparecem no currículo: {string.Join(", ", coverage.Missing.Take(6))}.",
                    "Inclua apenas as palavras-chave que você realmente domina, de preferência dentro das experiências."));
        }
        else if (skills.Count >= 10) skillPts += 7;
        else skillPts += 4;
        categories.Add(new("skills", "Competências", Math.Min(skillPts, 16), 16, skillPts >= 12 ? "Boa cobertura de palavras-chave." : "Reforce termos que os sistemas de triagem procuram."));

        // ---------------- Education ----------------
        var eduPts = 0;
        if (doc.Education.Count > 0) eduPts += 5;
        else issues.Add(new("Formação", "tip", "Formação não informada", "Mesmo cursos livres contam como formação complementar.", "Adicione graduação, pós, curso técnico ou certificações relevantes."));
        if (doc.Education.Any(e => !string.IsNullOrWhiteSpace(e.EndDate) || e.Current)) eduPts += 3;
        else if (doc.Education.Count > 0) issues.Add(new("Formação", "tip", "Datas da formação ausentes", "O ano de conclusão contextualiza seu momento de carreira.", "Informe o período de cada formação."));
        categories.Add(new("education", "Formação", eduPts, 8, eduPts >= 6 ? "Formação clara e datada." : "Complete os dados de formação."));

        // ---------------- ATS / structure ----------------
        var atsPts = 0;
        var words = TextAnalysis.WordCount(fullText);
        if (words is >= 250 and <= 900) atsPts += 4;
        else if (words is >= 150 and < 250) { atsPts += 2; issues.Add(new("Estrutura", "tip", "Currículo enxuto demais", "Pouco conteúdo para a triagem avaliar.", "Inclua mais realizações e competências relevantes.")); }
        else if (words > 900) { atsPts += 2; issues.Add(new("Estrutura", "warning", "Currículo muito longo", "Recrutadores dedicam poucos segundos à primeira leitura.", "Priorize as experiências dos últimos 10 anos e corte repetições.")); }
        if (bullets.Count >= 6) atsPts += 3;
        var standardSections = 0;
        if (!string.IsNullOrWhiteSpace(doc.Summary)) standardSections++;
        if (doc.Experience.Count > 0) standardSections++;
        if (doc.Education.Count > 0) standardSections++;
        if (doc.SkillGroups.Count > 0) standardSections++;
        atsPts += Math.Min(standardSections, 4);
        if (!System.Text.RegularExpressions.Regex.IsMatch(fullText, @"[│┃▌▐■□●◦▪▫‣⁃]")) atsPts += 2;
        else issues.Add(new("Estrutura", "warning", "Caracteres decorativos no texto", "Símbolos gráficos podem confundir leitores de ATS.", "Use marcadores simples e padrão."));
        if (doc.Languages.Count > 0 || doc.Certifications.Count > 0 || doc.Projects.Count > 0) atsPts += 1;
        categories.Add(new("ats", "Estrutura ATS", Math.Min(atsPts, 14), 14, atsPts >= 11 ? "Formato legível por sistemas de triagem." : "A estrutura pode ser mais previsível para o ATS."));

        // ---------------- Impact ----------------
        var impactPts = 0;
        if (quantified > 0) impactPts += 3;
        if (summaryWords > 0 && TextAnalysis.HasMetric(doc.Summary)) impactPts += 2;
        var verbVariety = bullets.Select(b => TextAnalysis.Tokenize(b).FirstOrDefault() ?? "").Where(t => t.Length > 0).Distinct().Count();
        if (verbVariety >= 5) impactPts += 3;
        else if (verbVariety >= 3) impactPts += 2;
        else if (verbVariety >= 1) impactPts += 1;
        categories.Add(new("impact", "Impacto", Math.Min(impactPts, 8), 8, impactPts >= 6 ? "Conquistas com números e verbos fortes." : "Transforme tarefas em resultados mensuráveis."));

        var total = Math.Clamp(categories.Sum(c => c.Score), 0, 100);
        var label = total switch
        {
            >= 90 => "Excelente",
            >= 80 => "Forte",
            >= 65 => "Bom",
            >= 50 => "Precisa de atenção",
            _ => "Fraco"
        };
        var verdict = total switch
        {
            >= 90 => "Seu currículo está pronto para as vagas mais competitivas. Revise apenas o alinhamento com cada descrição.",
            >= 80 => "Um currículo forte. Pequenos ajustes de palavras-chave e impacto podem elevar ainda mais sua taxa de resposta.",
            >= 65 => "Base sólida, mas a triagem pode descartar detalhes importantes. Foque nos pontos abaixo.",
            >= 50 => "Há lacunas que reduzem suas chances. Corrija os itens críticos antes de se candidatar.",
            _ => "O currículo ainda não comunica seu valor. Comece pelos itens críticos e use o assistente de IA."
        };

        return new ResumeScore(total, label, verdict, categories, issues.OrderBy(i => i.Severity switch { "critical" => 0, "warning" => 1, _ => 2 }).ToList(), coverage);
    }

    /// <summary>Heuristic score for a pasted or imported resume without structured data.</summary>
    public static ResumeScore AnalyzePlainText(string text, string? jobDescription = null)
    {
        var issues = new List<ScoreIssue>();
        var categories = new List<ScoreCategory>();
        var normalized = TextAnalysis.Normalize(text);
        var words = TextAnalysis.WordCount(text);
        var coverage = TextAnalysis.Coverage(text, jobDescription);
        var bullets = text.Split('\n').Count(l => l.TrimStart().StartsWith("-") || l.TrimStart().StartsWith("•") || l.TrimStart().StartsWith("*"));
        var numbers = System.Text.RegularExpressions.Regex.Matches(text, @"\d+([.,]\d+)?\s*%|\b\d{2,}\b").Count;
        var hasEmail = TextAnalysis.LooksLikeEmail(System.Text.RegularExpressions.Regex.Match(text, @"[\w.+-]+@[\w-]+\.[\w.]+").Value);
        var hasPhone = System.Text.RegularExpressions.Regex.IsMatch(text, @"\+?\d[\d\s().-]{7,}\d");

        var structure = 0;
        var sectionHits = 0;
        foreach (var s in new[] { "resumo", "experiencia", "formacao", "competencias", "skills", "summary", "education", "idiomas", "languages", "projetos", "projects" })
            if (normalized.Contains(s)) sectionHits++;
        structure += Math.Min(10, sectionHits * 2);
        if (hasEmail) structure += 4; else issues.Add(new("Contato", "critical", "E-mail não encontrado", "O ATS não conseguirá registrar seu contato.", "Inclua um e-mail visível no topo, em texto simples."));
        if (hasPhone) structure += 3; else issues.Add(new("Contato", "warning", "Telefone não encontrado", "Facilite o contato do recrutador.", "Inclua telefone com DDD."));
        if (words is >= 250 and <= 900) structure += 5;
        else if (words > 0) issues.Add(new("Estrutura", "warning", "Tamanho fora do ideal", $"Seu currículo tem {words} palavras; o ideal fica entre 250 e 900.", "Ajuste o volume de conteúdo para 1 ou 2 páginas."));
        categories.Add(new("structure", "Estrutura", Math.Min(structure, 22), 22, structure >= 16 ? "Seções e contato bem identificados." : "O leitor precisa de uma estrutura mais previsível."));

        var keywordsPts = coverage.Keywords.Count == 0 ? 14 : (int)Math.Round(26.0 * coverage.Percent / 100.0);
        if (coverage.Keywords.Count > 0 && coverage.Percent < 60)
            issues.Add(new("Palavras-chave", "warning", "Baixa aderência à vaga", $"Termos ausentes: {string.Join(", ", coverage.Missing.Take(8))}.", "Inclua os termos que você domina, sem inventar experiência."));
        categories.Add(new("keywords", "Aderência à vaga", keywordsPts, 26, coverage.Keywords.Count == 0 ? "Cole a descrição da vaga para medir aderência." : $"{coverage.Percent}% das palavras-chave da vaga aparecem no texto."));

        var content = 0;
        if (bullets >= 6) content += 6;
        else issues.Add(new("Conteúdo", "warning", "Poucos bullets", "Blocos de texto longo são difíceis de escanear.", "Transforme responsabilidades em bullets curtos."));
        if (numbers >= 5) content += 8;
        else if (numbers > 0) content += 4;
        else issues.Add(new("Conteúdo", "warning", "Sem resultados quantificados", "Números tornam as conquistas verificáveis.", "Adicione métricas de impacto."));
        if (TextAnalysis.HasActionVerb(text)) content += 6;
        else issues.Add(new("Conteúdo", "tip", "Poucos verbos de ação", "Verbos fortes dão ritmo à leitura.", "Comece bullets com verbos como “Liderei”, “Reduzi”, “Implementei”."));
        categories.Add(new("content", "Conteúdo", Math.Min(content, 20), 20, content >= 15 ? "Conteúdo específico e orientado a resultados." : "Reforce conquistas com verbos e números."));

        var format = 0;
        var longLines = text.Split('\n').Count(l => l.Length > 180);
        if (longLines <= 2) format += 6; else issues.Add(new("Formatação", "warning", "Linhas muito longas", "Parágrafos extensos dificultam a leitura.", "Quebre o texto em bullets objetivos."));
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"[│┃▌▐■□●◦▪▫‣⁃]")) format += 5;
        else issues.Add(new("Formatação", "warning", "Símbolos decorativos detectados", "Elementos gráficos podem quebrar a leitura automática.", "Use marcadores simples."));
        if (normalized.Contains("curriculum") || normalized.Contains("curriculo")) issues.Add(new("Formatação", "tip", "Título desnecessário", "O título “Currículo” consome espaço sem agregar.", "Remova o título e comece pelo seu nome."));
        else format += 3;
        categories.Add(new("format", "Formatação", Math.Min(format, 14), 14, format >= 11 ? "Texto limpo, fácil de importar." : "Simplifique a diagramação para não perder conteúdo."));

        var impact = Math.Min(18, (numbers >= 3 ? 8 : numbers > 0 ? 4 : 0) + (TextAnalysis.HasActionVerb(text) ? 6 : 0) + (coverage.Percent >= 70 ? 4 : 2));
        categories.Add(new("impact", "Impacto", impact, 18, impact >= 13 ? "Conquistas mensuráveis e vocabulário alinhado." : "Inclua resultados concretos para se destacar."));

        var total = Math.Clamp(categories.Sum(c => c.Score), 0, 100);
        var label = total switch { >= 90 => "Excelente", >= 80 => "Forte", >= 65 => "Bom", >= 50 => "Precisa de atenção", _ => "Fraco" };
        var verdict = total >= 75
            ? "Seu currículo passa bem pelos filtros. Ajuste as palavras-chave para cada vaga e mantenha os resultados em destaque."
            : "Seu currículo pode estar sendo filtrado antes de chegar a um recrutador. Comece pelos itens críticos abaixo.";
        return new ResumeScore(total, label, verdict, categories, issues.OrderBy(i => i.Severity switch { "critical" => 0, "warning" => 1, _ => 2 }).ToList(), coverage);
    }
}
