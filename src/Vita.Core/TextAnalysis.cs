using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vita.Core;

public static class TextAnalysis
{
    private static readonly HashSet<string> PtStop = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","o","as","os","um","uma","uns","umas","de","da","do","das","dos","em","no","na","nos","nas","por","para","com","sem",
        "e","ou","que","se","ao","aos","à","às","como","mais","menos","muito","muita","muitos","muitas","já","não","sim","seu","sua",
        "seus","suas","este","esta","estes","estas","isso","isto","aquele","aquela","foi","são","ser","estar","está","ter","tem","têm",
        "há","pelo","pela","pelos","pelas","entre","sobre","até","após","antes","durante","também","bem","ainda","só","cada","todo","toda",
        "todos","todas","outro","outra","outros","outras","você","vocês","nós","eu","ele","ela","eles","elas","meu","minha","nosso","nossa",
        "na","nas","num","numa","the","and","or","of","to","in","on","with","for","as","at","by","from","is","are","be","been","being",
        "we","you","your","our","their","this","that","these","those","will","would","can","could","should","may","might","must","have",
        "has","had","do","does","did","not","but","if","then","than","so","such","also","more","most","other","into","about","across",
        "using","use","used","work","working","role","team","teams","company","job","position","candidate","candidates","experience",
        "experiência","experiencia","profissional","profissionais","trabalho","trabalhar","time","área","area","buscamos","procuramos",
        "requisitos","responsabilidades","benefícios","beneficios","sobre","vaga","empresa","pessoa","pessoas","dia","dias","ano","anos",
        "conhecimento","conhecimentos","desejável","desejavel","necessário","necessario","requer","será","serao","att","etc","pra","pro",
        "sua","suas","ser","será","onde","quem","quando","qual","quais",
        "what","who","why","when","where","which","while","how","whether","within","without","over","under","between","per","via",
        "their","them","they","its","it","we","us","our","you","your","i","my","me","he","she","his","her",
        "here","there","now","new","one","two","three","first","last","next","well","just","only","very","much","many","some","any",
        "all","each","both","few","own","same","than","then","too","again","further","once","during","before","after","above","below",
        "up","down","out","off","on","in","at","by","for","with","about","against","between","into","through","during","before","after"
    };

    private static readonly string[] ActionVerbs =
    {
        "liderei","liderança","liderou","desenvolvi","desenvolveu","implementei","implementou","criei","criou","gerenciei","gerenciou",
        "reduzi","reduziu","aumentei","aumentou","otimizei","otimizou","automatizei","automatizou","implantei","implantou","coordenei",
        "conduzi","conduziu","estruturei","estruturou","migrei","migrou","modernizei","projetei","projetou","entreguei","entregou",
        "melhorei","melhorou","acelerei","escalabilidade","negociei","analisei","analisou","construí","construiu","defini","definiu",
        "monitorei","documentei","treinei","contratei","recrutei","planejei","executei","apoiei","reorganizei","padronizei",
        "led","lead","built","developed","implemented","created","managed","reduced","increased","improved","optimized","automated",
        "designed","launched","delivered","migrated","scaled","drove","established","streamlined","negotiated","analyzed","coached",
        "mentored","owned","architected","refactored","integrated","deployed","maintained","partnered","coordinated","spearheaded"
    };

    private static readonly string[] SkillLexicon =
    {
        "c#",".net","asp.net","java","javascript","typescript","python","go","golang","rust","php","ruby","kotlin","swift","scala","r",
        "react","react native","angular","vue","next.js","node.js","express","django","flask","spring boot","laravel",".net core",
        "sql","mysql","postgresql","sql server","oracle","mongodb","redis","elasticsearch","dynamodb","firebase","supabase",
        "aws","azure","gcp","google cloud","docker","kubernetes","terraform","ansible","jenkins","gitlab ci","github actions","ci/cd",
        "devops","microservices","microsserviços","rest","graphql","grpc","kafka","rabbitmq","rabbit mq","etl","data warehouse",
        "power bi","tableau","looker","qlik","excel","google sheets","airflow","dbt","spark","hadoop","databricks","snowflake",
        "machine learning","deep learning","nlp","computer vision","pytorch","tensorflow","scikit-learn","pandas","numpy","llm",
        "generative ai","mlops","statistics","estatística","a/b testing","experimentação","analytics","data analysis","análise de dados",
        "product management","gestão de produto","product design","ux design","ui design","figma","sketch","adobe xd","photoshop",
        "illustrator","prototyping","design system","user research","pesquisa com usuários","wireframing","accessibility","acessibilidade",
        "seo","sem","google ads","meta ads","marketing digital","growth","crm","salesforce","hubspot","pipedrive","email marketing",
        "content marketing","copywriting","social media","branding","gestão de projetos","project management","scrum","kanban","agile","ágil",
        "jira","confluence","notion","trello","asana","monday.com","okr","kpi","lean","six sigma","itil","pmp","prince2",
        "gestão de pessoas","recrutamento","rh","people operations","business partner","folha de pagamento","dp","departamento pessoal",
        "contabilidade","financial analysis","fp&a","orçamento","budgeting","forecast","auditoria","compliance","lgpd","gdpr",
        "vendas","sales","account management","customer success","atendimento ao cliente","suporte","negociação","negociation",
        "inglês","english","espanhol","spanish","alemão","german","francês","french","mandarim","italiano",
        "comunicação","communication","liderança","leadership","pensamento analítico","resolução de problemas","problem solving",
        "colaboração","collaboration","autonomia","proatividade","organização","adaptabilidade","criatividade","pensamento crítico",
        "law","direito","contratos","contracts","jurídico","legal","regulatório","regulatory","saúde","healthcare","enfermagem",
        "logística","logistics","supply chain","estoque","inventory","compras","procurement","operações","operations","processos",
        "melhoria contínua","continuous improvement","qualidade","quality assurance","qa","testes","testing","automação de testes",
        "cybersecurity","segurança da informação","pentest","soc","siem","iso 27001","soc 2","nist","scrum master",
        "arquitetura de software","software architecture","ddd","clean architecture","tdd","bdd","solid","design patterns"
    };

    /// <summary>
    /// Bilingual equivalences for the terms that show up most often in international job posts.
    /// Keys and values are compared after normalization, so accents do not matter.
    /// </summary>
    private static readonly Dictionary<string, string[]> TermAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user research"] = new[] { "pesquisa com usuarios", "pesquisa de usuarios", "entrevistas com usuarios", "user interviews" },
        ["pesquisa com usuários"] = new[] { "user research", "user interviews", "pesquisa de usuarios" },
        ["accessibility"] = new[] { "acessibilidade" },
        ["acessibilidade"] = new[] { "accessibility" },
        ["prototyping"] = new[] { "prototipagem", "prototipos", "prototypes", "protótipos" },
        ["prototipagem"] = new[] { "prototyping", "prototypes" },
        ["design system"] = new[] { "design systems", "sistema de design", "sistemas de design" },
        ["product design"] = new[] { "design de produto", "product designer", "product design" },
        ["testing"] = new[] { "testes", "teste", "tested", "quality assurance" },
        ["testes"] = new[] { "testing", "tests", "tested" },
        ["english"] = new[] { "ingles", "inglês" },
        ["ingles"] = new[] { "english" },
        ["spanish"] = new[] { "espanhol" },
        ["leadership"] = new[] { "lideranca", "liderança", "led", "lead" },
        ["liderança"] = new[] { "leadership" },
        ["communication"] = new[] { "comunicacao", "comunicação" },
        ["comunicação"] = new[] { "communication" },
        ["problem solving"] = new[] { "resolucao de problemas", "resolução de problemas" },
        ["collaboration"] = new[] { "colaboracao", "colaboração" },
        ["colaboração"] = new[] { "collaboration" },
        ["healthcare"] = new[] { "saude", "saúde" },
        ["saúde"] = new[] { "healthcare", "health" },
        ["compliance"] = new[] { "conformidade", "lgpd", "gdpr" },
        ["wireframing"] = new[] { "wireframe", "wireframes", "wireframes" },
        ["experimentation"] = new[] { "experimentacao", "experimentação", "a/b testing", "ab testing" },
        ["analytics"] = new[] { "analise de dados", "análise de dados", "data analysis" },
        ["stakeholders"] = new[] { "partes interessadas", "stakeholder" },
        ["roadmap"] = new[] { "roadmaps", "roteiro", "roteiros" },
        ["metrics"] = new[] { "metricas", "métricas", "kpi", "kpis" },
        ["discovery"] = new[] { "descoberta", "descobrir" },
        ["agile"] = new[] { "agil", "ágil", "scrum", "kanban" },
        ["customer success"] = new[] { "sucesso do cliente" },
        ["project management"] = new[] { "gestao de projetos", "gestão de projetos" },
        ["gestão de projetos"] = new[] { "project management" },
        ["operations"] = new[] { "operacoes", "operações" },
        ["sales"] = new[] { "vendas" },
        ["vendas"] = new[] { "sales" }
    };

    private static readonly HashSet<string> LevelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "senior", "sr", "junior", "jr", "pleno", "mid", "middle", "staff", "lead", "leader", "principal", "especialista",
        "specialist", "intern", "internship", "estagio", "estágio", "trainee", "head", "chief", "vp", "director", "diretor"
    };

    private static readonly HashSet<string> RoleNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "designer", "manager", "engineer", "developer", "analyst", "architect", "scientist", "researcher", "consultant",
        "coordinator", "administrator", "accountant", "lawyer", "nurse", "teacher", "marketer", "writer", "editor",
        "producer", "owner", "recruiter", "specialist", "strategist", "planner", "controller", "auditor", "director"
    };

    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(@"[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"(\+?\d{1,3}[\s.-]?)?(\(?\d{2,3}\)?[\s.-]?)?\d{4,5}[\s.-]?\d{4}", RegexOptions.Compiled);
    private static readonly Regex LinkRx = new(@"https?://\S+|www\.\S+|linkedin\.com/\S+|github\.com/\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string[] Tokenize(string input) =>
        Normalize(input).Split(new[] { ' ', '\n', '\r', '\t', ',', ';', '/', '|', '(', ')', '[', ']', ':', '.', '!', '?', '"', '\'' },
            StringSplitOptions.RemoveEmptyEntries);

    public static bool ContainsKeyword(string haystack, string keyword)
    {
        var h = Normalize(haystack);
        var k = Normalize(keyword);
        if (k.Length < 2) return false;
        var idx = h.IndexOf(k, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(h[idx - 1]);
            var after = idx + k.Length;
            var afterOk = after >= h.Length || !char.IsLetterOrDigit(h[after]);
            if (beforeOk && afterOk) return true;
            idx = h.IndexOf(k, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// Keyword match that understands bilingual equivalences and simple plurals.
    /// Used for job matching, where resumes and postings often mix Portuguese and English.
    /// </summary>
    public static bool MatchesTerm(string haystack, string keyword)
    {
        if (ContainsKeyword(haystack, keyword)) return true;
        var normalized = Normalize(keyword);
        if (TermAliases.TryGetValue(keyword, out var aliases) || TermAliases.TryGetValue(normalized, out aliases))
        {
            foreach (var alias in aliases)
                if (ContainsKeyword(haystack, alias)) return true;
        }

        if (normalized.Length > 3 && normalized.EndsWith('s') && ContainsKeyword(haystack, normalized[..^1])) return true;
        if (normalized.Length > 2 && ContainsKeyword(haystack, normalized + "s")) return true;
        return false;
    }

    /// <summary>True when both strings share a meaningful role noun (designer, engineer, manager…).</summary>
    public static bool SharesRoleNoun(string first, string second)
    {
        var a = Tokenize(first);
        var b = Tokenize(second);
        return a.Any(t => RoleNouns.Contains(t) && b.Any(t2 => RoleNouns.Contains(t2) && TokenEquivalent(t, t2)));
    }

    public static bool IsLevelWord(string token) => LevelWords.Contains(Normalize(token));

    public static bool TokenEquivalent(string first, string second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        if (a.Length < 3 || b.Length < 3) return a == b;
        if (a == b) return true;
        if (a.Length >= 4 && b.StartsWith(a, StringComparison.Ordinal)) return true;
        if (b.Length >= 4 && a.StartsWith(b, StringComparison.Ordinal)) return true;
        if (a.EndsWith('s') && a[..^1] == b) return true;
        if (b.EndsWith('s') && b[..^1] == a) return true;
        return false;
    }

    /// <summary>Extracts the most relevant terms from a job description, keeping known multi-word skills together.</summary>
    public static List<string> ExtractKeywords(string? jobDescription, int max = 30)
    {
        if (string.IsNullOrWhiteSpace(jobDescription)) return new List<string>();
        var normalized = Normalize(jobDescription);
        var found = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) known skills present in the text, ranked by raw frequency in the posting
        foreach (var skill in SkillLexicon)
        {
            if (!ContainsKeyword(normalized, skill)) continue;
            found.Add(skill);
            used.Add(skill);
        }

        // 2) frequent meaningful tokens
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(jobDescription))
        {
            if (token.Length < 4 || PtStop.Contains(token) || double.TryParse(token, out _)) continue;
            if (token.All(char.IsDigit)) continue;
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
        {
            if (found.Count >= max) break;
            if (kv.Value < 2) continue;
            if (found.Any(f => f.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) || kv.Key.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            found.Add(kv.Key);
        }

        return found.Take(max).ToList();
    }

    public static KeywordCoverage Coverage(string resumeText, string? jobDescription, int max = 30)
    {
        var keywords = ExtractKeywords(jobDescription, max);
        var matched = keywords.Where(k => MatchesTerm(resumeText, k)).ToList();
        var missing = keywords.Where(k => !MatchesTerm(resumeText, k)).ToList();
        var pct = keywords.Count == 0 ? 0 : (int)Math.Round(100.0 * matched.Count / keywords.Count);
        return new KeywordCoverage(keywords, matched, missing, pct);
    }

    public static bool HasActionVerb(string text) =>
        ActionVerbs.Any(v => ContainsKeyword(text, v));

    public static bool HasMetric(string text) =>
        Regex.IsMatch(text, @"\d+\s*[%€$£]|\d+[.,]?\d*\s*(%|mil|k\b|m\b|milh|milhão|milhões|million|bn\b)|\b\d{2,}\b", RegexOptions.IgnoreCase);

    public static int WordCount(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : MultiSpace.Split(text.Trim()).Length;

    public static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    public static string Clean(string? value) => MultiSpace.Replace(value?.Trim() ?? string.Empty, " ");

    public static bool LooksLikeEmail(string? value) => !string.IsNullOrWhiteSpace(value) && EmailRx.IsMatch(value);
    public static bool LooksLikePhone(string? value) => !string.IsNullOrWhiteSpace(value) && PhoneRx.IsMatch(value);
    public static bool LooksLikeLink(string? value) => !string.IsNullOrWhiteSpace(value) && LinkRx.IsMatch(value);

    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
        return value[..(max - 1)].TrimEnd() + "…";
    }

    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = System.Net.WebUtility.HtmlDecode(html);
        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</p>|</li>|</h[1-6]>|</div>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}

public sealed record KeywordCoverage(List<string> Keywords, List<string> Matched, List<string> Missing, int Percent);
