using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Vita.App.Services;

public sealed record ExtractedText(string Text, string Kind, int Pages);

public sealed partial class ImportService
{
    private static readonly string[] SectionWords =
    {
        "resumo", "summary", "perfil", "profile", "objetivo", "objective", "sobre mim", "about",
        "experiencia", "experiência", "experience", "historico profissional", "histórico profissional",
        "professional experience", "employment", "carreira", "career",
        "formacao", "formação", "education", "academico", "acadêmico", "academic",
        "competencias", "competências", "skills", "habilidades", "technologies", "tecnologias",
        "idiomas", "languages", "projetos", "projects", "certificacoes", "certificações", "certifications",
        "cursos", "courses", "voluntariado", "volunteering", "publicacoes", "publicações", "publications"
    };

    private static readonly string[] RoleWords =
    {
        "analista", "designer", "desenvolvedor", "developer", "engenheiro", "engineer", "gerente", "manager",
        "diretor", "director", "coordenador", "coordinator", "especialista", "specialist", "assistente",
        "assistant", "estagiario", "estagiário", "intern", "consultor", "consultant", "professor", "teacher",
        "product", "produto", "lead", "lider", "líder", "head", "arquiteto", "architect", "cientista", "scientist",
        "administrador", "administrator", "supervisor", "tecnico", "técnico", "vendedor", "sales", "customer",
        "suporte", "support", "redator", "writer", "social media", "marketing", "growth", "financeiro", "finance",
        "advogado", "lawyer", "enfermeiro", "nurse", "medico", "médico", "farmaceutico", "farmacêutico"
    };

    private static readonly string[] DegreeWords =
    {
        "bacharelado", "licenciatura", "mba", "mestrado", "doutorado", "pos-graduacao", "pós-graduação",
        "tecnologo", "tecnólogo", "tecnico", "técnico", "graduacao", "graduação", "bachelor", "master", "phd",
        "mba", "degree", "ensino medio", "ensino médio", "curso"
    };

    public async Task<ExtractedText> ExtractAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        using var memory = new MemoryStream();
        // BrowserFileStream (Blazor InputFile) only supports asynchronous reads.
        await stream.CopyToAsync(memory, ct);
        memory.Position = 0;

        switch (extension)
        {
            case ".pdf":
                return ExtractPdf(memory);
            case ".docx":
                return ExtractDocx(memory);
            case ".txt":
            case ".md":
            case ".rtf":
                using (var reader = new StreamReader(memory, Encoding.UTF8, true))
                    return new ExtractedText(reader.ReadToEnd(), "Texto", 1);
            case ".doc":
                throw new InvalidOperationException("O formato .doc antigo não é suportado. Abra o arquivo no Word e salve como .docx.");
            default:
                throw new InvalidOperationException("Formato não suportado. Envie um PDF, DOCX ou TXT.");
        }
    }

    private static ExtractedText ExtractPdf(MemoryStream memory)
    {
        var sb = new StringBuilder();
        var pages = 0;
        try
        {
            using var pdf = PdfDocument.Open(memory);
            foreach (var page in pdf.GetPages())
            {
                pages++;
                var text = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(text)) text = page.Text;
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Não foi possível ler este PDF. Ele pode estar protegido ou corrompido. Tente exportar como DOCX ou colar o texto.", ex);
        }

        var content = sb.ToString().Trim();
        if (content.Length < 40)
            throw new InvalidOperationException("Este PDF parece ser uma imagem escaneada. A extração de texto não funciona nesse caso — cole o conteúdo manualmente ou envie o arquivo original em DOCX.");
        return new ExtractedText(content, "PDF", Math.Max(1, pages));
    }

    private static ExtractedText ExtractDocx(MemoryStream memory)
    {
        var sb = new StringBuilder();
        try
        {
            using var doc = WordprocessingDocument.Open(memory, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) throw new InvalidOperationException("DOCX sem conteúdo.");
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                var text = paragraph.InnerText;
                if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Não foi possível ler este DOCX. Verifique se o arquivo está íntegro.", ex);
        }
        var content = sb.ToString().Trim();
        if (content.Length < 20) throw new InvalidOperationException("O DOCX não contém texto suficiente para importar.");
        return new ExtractedText(content, "DOCX", 1);
    }

    /// <summary>Deterministic parser that works without any AI provider.</summary>
    public ResumeDocument ParseHeuristic(string text)
    {
        var clean = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var rawLines = clean.Split('\n').Select(l => Regex.Replace(l.Trim(), @"\s{2,}", " ")).ToList();
        var lines = rawLines.Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return new ResumeDocument();

        var doc = new ResumeDocument();
        doc.Contact.Email = FirstMatch(clean, @"[\w.+-]+@[\w-]+\.[\w.]+");
        doc.Contact.Phone = FirstMatch(clean, @"(\+?\d{1,3}[\s.-]?)?(\(?\d{2,3}\)?[\s.-]?)?\d{4,5}[\s.-]?\d{4}");
        doc.Contact.LinkedIn = FirstMatch(clean, @"(?:https?://)?(?:www\.)?linkedin\.com/[^\s|,;]+");
        doc.Contact.Github = FirstMatch(clean, @"(?:https?://)?(?:www\.)?github\.com/[^\s|,;]+");
        doc.Contact.Website = FirstMatch(clean, @"(?:https?://)(?!(?:www\.)?(?:linkedin|github)\.com)[^\s|,;]+");

        // Name and headline: first lines that are not contact data or headings.
        var headerLines = new List<string>();
        foreach (var line in lines.Take(10))
        {
            if (line.Contains('@') || TextAnalysis.LooksLikePhone(line) || TextAnalysis.LooksLikeLink(line)) continue;
            if (IsSectionHeading(line) || IsNoiseLine(line)) continue;
            headerLines.Add(line);
            if (headerLines.Count >= 5) break;
        }

        var nameIndex = headerLines.FindIndex(LooksLikePersonName);
        if (nameIndex >= 0)
        {
            doc.Contact.FullName = CleanName(headerLines[nameIndex]);
            if (nameIndex + 1 < headerLines.Count)
            {
                var headline = headerLines[nameIndex + 1];
                if (headline.Length <= 90 && !TextAnalysis.LooksLikeLink(headline) && !LooksLikePersonName(headline))
                    doc.Contact.Headline = headline;
            }
        }
        else if (headerLines.Count > 0 && headerLines[0].Length is >= 2 and <= 60 && headerLines[0].Split(' ').Length <= 6)
        {
            doc.Contact.FullName = CleanName(headerLines[0]);
            if (headerLines.Count > 1 && headerLines[1].Length <= 90 && !TextAnalysis.LooksLikeLink(headerLines[1]))
                doc.Contact.Headline = headerLines[1];
        }

        if (!string.IsNullOrWhiteSpace(doc.Contact.Phone))
            doc.Contact.Phone = doc.Contact.Phone.Trim();

        var sections = SplitSections(lines);
        if (sections.TryGetValue("summary", out var summaryLines))
            doc.Summary = string.Join(" ", summaryLines);

        if (sections.TryGetValue("experience", out var expLines))
            ParseExperience(expLines, doc);

        if (sections.TryGetValue("education", out var eduLines))
            ParseEducation(eduLines, doc);

        if (sections.TryGetValue("skills", out var skillLines))
            ParseSkills(skillLines, doc);

        if (sections.TryGetValue("languages", out var langLines))
            ParseLanguages(langLines, doc);

        if (sections.TryGetValue("certifications", out var certLines))
            foreach (var line in certLines.Where(l => l.Length > 2))
                doc.Certifications.Add(new CertificationItem { Name = Regex.Replace(line, @"^[•\-*]\s*", "") });

        if (sections.TryGetValue("projects", out var projectLines))
        {
            var current = new ProjectItem();
            foreach (var line in projectLines)
            {
                if (line.StartsWith('•') || line.StartsWith('-') || line.StartsWith('*'))
                {
                    current.Bullets.Add(Regex.Replace(line, @"^[•\-*]\s*", ""));
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(current.Name)) doc.Projects.Add(current);
                    current = new ProjectItem { Name = line };
                }
            }
            if (!string.IsNullOrWhiteSpace(current.Name)) doc.Projects.Add(current);
        }

        // If no structured experience was detected, keep the whole resume readable.
        if (doc.Experience.Count == 0 && sections.TryGetValue("other", out var other) && other.Count > 0 && doc.Summary.Length == 0)
            doc.Summary = string.Join(" ", other.Take(6));

        if (doc.SkillGroups.Count == 0)
        {
            var known = new[] { "Python", "SQL", "Excel", "Figma", "Java", "JavaScript", "C#", "React", "AWS", "Azure", "Power BI", "Tableau" }
                .Where(skill => TextAnalysis.ContainsKeyword(clean, skill)).ToList();
            if (known.Count > 0) doc.SkillGroups.Add(new SkillGroup { Name = "Competências", Items = known });
        }

        return doc;
    }

    // ------------------------------------------------------------------ sections
    private static Dictionary<string, List<string>> SplitSections(List<string> lines)
    {
        var result = new Dictionary<string, List<string>>();
        var current = "other";
        result[current] = new List<string>();
        foreach (var line in lines)
        {
            var key = SectionKey(line);
            if (key is not null)
            {
                current = key;
                if (!result.ContainsKey(current)) result[current] = new List<string>();
                continue;
            }
            result[current].Add(line);
        }
        return result;
    }

    private static string? SectionKey(string line)
    {
        if (line.Length > 55) return null;
        var normalized = TextAnalysis.Normalize(line.Trim().TrimEnd(':')).Trim(' ', '-', '–', '_');
        if (normalized.Length == 0) return null;
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4) return null;

        if (normalized is "resumo" or "resumo profissional" or "summary" or "perfil" or "profile" or "sobre" or "sobre mim" or "objetivo" or "objective" or "about me" or "about") return "summary";
        if (normalized.StartsWith("experiencia") || normalized.StartsWith("experience") || normalized.Contains("historico profissional") || normalized.Contains("professional experience") || normalized == "carreira" || normalized == "career" || normalized == "employment") return "experience";
        if (normalized.StartsWith("formacao") || normalized.StartsWith("education") || normalized.Contains("academico") || normalized.Contains("academic")) return "education";
        if (normalized.StartsWith("competencia") || normalized.StartsWith("skill") || normalized == "habilidades" || normalized == "technologies" || normalized == "tecnologias") return "skills";
        if (normalized.StartsWith("idioma") || normalized.StartsWith("language")) return "languages";
        if (normalized.StartsWith("certifica") || normalized.StartsWith("certification") || normalized == "cursos" || normalized == "courses") return "certifications";
        if (normalized.StartsWith("projeto") || normalized.StartsWith("project")) return "projects";
        return null;
    }

    private static bool IsSectionHeading(string line) => SectionKey(line) is not null;

    // Page numbers and other layout noise sometimes come first when a PDF is
    // extracted in content order, so they must not become the resume name.
    private static bool IsNoiseLine(string line) =>
        Regex.IsMatch(line, @"^\d{1,3}(\s*/\s*\d{1,3})?$") ||
        Regex.IsMatch(line, @"^(p[áa]gina|page)\s*\d+", RegexOptions.IgnoreCase);

    private static bool LooksLikePersonName(string line)
    {
        if (line.Length is < 2 or > 70) return false;
        if (line.Contains('|') || line.Contains('@') || line.Any(char.IsDigit)) return false;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 6) return false;
        if (words.Any(w => w.Any(ch => !char.IsLetter(ch) && ch != '.' && ch != '\'' && ch != '-'))) return false;

        var normalized = TextAnalysis.Normalize(line);
        if (SectionKey(line) is not null) return false;
        if (RoleWords.Any(w => normalized.Contains(w))) return false;
        if (DegreeWords.Any(d => normalized.Contains(d))) return false;

        var allCaps = line == line.ToUpperInvariant();
        if (!allCaps && !words.All(w => char.IsUpper(w[0]))) return false;

        return true;
    }

    private static string CleanName(string line)
    {
        var clean = Regex.Replace(line, @"^[#•*\-]+\s*", "").Replace("  ", " ").Trim();
        if (clean.Length > 2 && clean == clean.ToUpperInvariant() && clean.Any(char.IsLetter))
            clean = TitleCaseName(clean);
        return clean;
    }

    private static string TitleCaseName(string value)
    {
        var connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "da", "do", "das", "dos", "e", "di", "du", "van", "von"
        };

        var words = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((word, index) =>
            index > 0 && connectors.Contains(word)
                ? word
                : char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word[1..] : string.Empty)));
    }

    // ------------------------------------------------------------------ experience
    private void ParseExperience(List<string> lines, ResumeDocument doc)
    {
        ExperienceItem? current = null;
        var pendingHeader = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var isBullet = line.StartsWith('•') || line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('–');
            var (start, end, currentJob, headerWithoutDate) = ParseDates(line);

            if (!isBullet && headerWithoutDate.Length > 0 && (start.Length > 0 || currentJob || LooksLikeRoleHeader(headerWithoutDate)))
            {
                if (current is not null && (!string.IsNullOrWhiteSpace(current.Role) || !string.IsNullOrWhiteSpace(current.Company)))
                    doc.Experience.Add(current);

                var (role, company) = SplitRoleCompany(headerWithoutDate);
                current = new ExperienceItem
                {
                    Role = role,
                    Company = company,
                    StartDate = start,
                    EndDate = end,
                    Current = currentJob
                };
                pendingHeader = string.IsNullOrWhiteSpace(company);
                continue;
            }

            if (current is null) continue;

            if (pendingHeader)
            {
                current.Company = Regex.Replace(line, @"^[•\-*]\s*", "").Trim();
                pendingHeader = false;
                continue;
            }

            var bullet = Regex.Replace(line, @"^[•\-*–]\s*", "").Trim();
            if (bullet.Length > 0) current.Bullets.Add(bullet);
        }

        if (current is not null && (!string.IsNullOrWhiteSpace(current.Role) || !string.IsNullOrWhiteSpace(current.Company)))
            doc.Experience.Add(current);

        // Reject accidental section text parsed as a job.
        doc.Experience = doc.Experience
            .Where(e => e.Role.Length + e.Company.Length > 2 && e.Bullets.Count + (e.StartDate.Length > 0 ? 1 : 0) > 0)
            .Take(12)
            .ToList();
    }

    private static bool LooksLikeRoleHeader(string line)
    {
        if (line.Length > 120) return false;
        var normalized = TextAnalysis.Normalize(line);
        return RoleWords.Any(w => normalized.Contains(w));
    }

    private static (string Role, string Company) SplitRoleCompany(string header)
    {
        var cleaned = Regex.Replace(header, @"\s*[|·•]\s*", " | ").Trim();
        var parts = Regex.Split(cleaned, @"\s+(?:\||—|–|@|at|na|em)\s+|\s*\|\s*|\s+-\s+")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !Regex.IsMatch(p, @"^(19|20)\d{2}$"))
            .ToList();

        if (parts.Count == 0) return (header, string.Empty);
        if (parts.Count == 1)
        {
            var single = parts[0];
            var comma = single.Split(',');
            if (comma.Length >= 2 && LooksLikeRoleHeader(comma[0]) && comma[1].Trim().Length is > 1 and < 60)
                return (comma[0].Trim(), comma[1].Trim());
            return (single, string.Empty);
        }

        var firstIsRole = LooksLikeRoleHeader(parts[0]);
        var secondIsRole = LooksLikeRoleHeader(parts[1]);
        if (firstIsRole && !secondIsRole) return (parts[0], parts[1]);
        if (secondIsRole && !firstIsRole) return (parts[1], parts[0]);
        return (parts[0], parts[1]);
    }

    private static (string Start, string End, bool Current, string HeaderWithoutDate) ParseDates(string line)
    {
        var current = Regex.IsMatch(line, @"\b(atual|present|presente|o momento|now|hoje)\b", RegexOptions.IgnoreCase);
        var years = Regex.Matches(line, @"\b(19|20)\d{2}\b").Select(m => m.Value).ToList();
        var monthStart = Regex.Match(line, @"\b(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez|january|february|march|april|may|june|july|august|september|october|november|december)[a-z]*\.?\s*[/\-\s]?\s*((19|20)\d{2})\b", RegexOptions.IgnoreCase);
        var monthEnd = Regex.Matches(line, @"\b(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez|january|february|march|april|may|june|july|august|september|october|november|december)[a-z]*\.?\s*[/\-\s]?\s*((19|20)\d{2})\b", RegexOptions.IgnoreCase);

        string start = string.Empty, end = string.Empty;
        if (monthEnd.Count >= 2)
        {
            start = ToYearMonth(monthEnd[0].Value);
            end = current ? string.Empty : ToYearMonth(monthEnd[^1].Value);
        }
        else if (years.Count >= 2)
        {
            start = years[0];
            end = current ? string.Empty : years[^1];
        }
        else if (years.Count == 1)
        {
            if (current) start = years[0];
            else start = years[0];
        }
        else if (monthStart.Success)
        {
            start = ToYearMonth(monthStart.Value);
        }

        var withoutDate = Regex.Replace(line, @"\b(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez)[a-z]*\.?\s*[/\-\s]?\s*(19|20)\d{2}\b", "", RegexOptions.IgnoreCase);
        withoutDate = Regex.Replace(withoutDate, @"\b(19|20)\d{2}\b", "");
        withoutDate = Regex.Replace(withoutDate, @"(?i)\b(atual|present|presente|o momento|now)\b", "");
        withoutDate = Regex.Replace(withoutDate, @"\(\s*[-–—|·•,\s]*\s*\)", "");
        withoutDate = Regex.Replace(withoutDate, @"\[\s*[-–—|·•,\s]*\s*\]", "");
        withoutDate = Regex.Replace(withoutDate, @"[\s\-–—|·•]+$", "").Trim(' ', '-', '–', '—', '|', '·', '•', ',');
        withoutDate = Regex.Replace(withoutDate, @"^[\s\-–—|·•]+", "").Trim();
        return (start, end, current, withoutDate);
    }

    private static string ToYearMonth(string value)
    {
        var m = Regex.Match(value, @"^([a-zA-Zçãéêíóú]+)\.?\s*[/\-\s]?\s*((19|20)\d{2})");
        if (!m.Success) return value;
        var monthNames = new[] { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };
        var name = TextAnalysis.Normalize(m.Groups[1].Value)[..3];
        var index = Array.IndexOf(monthNames, name) + 1;
        if (index == 0)
        {
            var english = new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
            index = Array.IndexOf(english, name) + 1;
        }
        return index > 0 ? $"{m.Groups[2].Value}-{index:00}" : m.Groups[2].Value;
    }

    // ------------------------------------------------------------------ education
    private static void ParseEducation(List<string> lines, ResumeDocument doc)
    {
        var buffer = new List<string>();
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw.Trim(), @"^[•\-*]\s*", "");
            if (line.Length == 0) continue;
            buffer.Add(line);
            var isInstitution = Regex.IsMatch(line, @"universidade|university|faculdade|college|instituto|institute|escola|school|senai|senac|usp|unicamp|fiap|puc", RegexOptions.IgnoreCase);
            var isDegree = DegreeWords.Any(d => TextAnalysis.Normalize(line).Contains(d));
            if (!isInstitution && !isDegree) continue;
            if (buffer.Count >= 4) { buffer.Clear(); continue; }

            var (start, end, current, _) = ParseDates(line);
            var item = new EducationItem { StartDate = start, EndDate = end, Current = current };
            var institutionLine = buffer.FirstOrDefault(l => Regex.IsMatch(l, @"universidade|university|faculdade|college|instituto|institute|escola|school|senai|senac|usp|unicamp|fiap|puc", RegexOptions.IgnoreCase));
            var degreeLine = buffer.FirstOrDefault(l => DegreeWords.Any(d => TextAnalysis.Normalize(l).Contains(d)));
            item.Institution = Regex.Replace(institutionLine ?? buffer[0], @"\s*[|—–].*$", "").Trim();
            var degreeText = Regex.Replace(degreeLine ?? (buffer.Count > 1 ? buffer[1] : line), @"\s*[|—–].*$", "").Trim();
            item.Degree = degreeText;
            item.Details = string.Join(" ", buffer.Where(l => l != institutionLine && l != degreeLine));
            if (item.Institution.Length > 0 || item.Degree.Length > 0)
                doc.Education.Add(item);
            buffer.Clear();
        }

        if (doc.Education.Count == 0)
        {
            foreach (var line in lines.Where(l => l.Length > 5).Take(4))
                doc.Education.Add(new EducationItem { Institution = line });
        }
    }

    // ------------------------------------------------------------------ skills/languages
    private static void ParseSkills(List<string> lines, ResumeDocument doc)
    {
        var general = new List<string>();
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw.Trim(), @"^[•\-*]\s*", "");
            if (line.Length == 0) continue;
            var split = line.Split(':', 2);
            if (split.Length == 2 && split[0].Trim().Length <= 40)
            {
                var items = Regex.Split(split[1], @"[,;|·]|\s{2,}").Select(i => i.Trim(' ', '•', '-')).Where(i => i.Length > 0).ToList();
                if (items.Count > 0) doc.SkillGroups.Add(new SkillGroup { Name = split[0].Trim(), Items = items });
            }
            else
            {
                general.AddRange(Regex.Split(line, @"[,;|·]").Select(i => i.Trim(' ', '•', '-')).Where(i => i.Length is > 0 and < 60));
            }
        }
        if (general.Count > 0)
            doc.SkillGroups.Insert(0, new SkillGroup { Name = "Competências", Items = general.Distinct(StringComparer.OrdinalIgnoreCase).ToList() });
    }

    private static void ParseLanguages(List<string> lines, ResumeDocument doc)
    {
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw.Trim(), @"^[•\-*]\s*", "");
            if (line.Length == 0) continue;
            foreach (var piece in Regex.Split(line, @"[,;|·]| {2,}"))
            {
                var part = piece.Trim(' ', '•', '-');
                if (part.Length == 0) continue;
                var split = Regex.Split(part, @"\s*[—–:\-]\s*");
                if (split.Length >= 2 && split[0].Length <= 30)
                    doc.Languages.Add(new LanguageItem { Name = split[0].Trim(), Level = split[1].Trim() });
                else if (part.Length <= 30)
                    doc.Languages.Add(new LanguageItem { Name = part });
            }
        }
    }

    private static string FirstMatch(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : string.Empty;
    }
}
