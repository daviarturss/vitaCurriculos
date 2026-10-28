using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

public sealed record JobPosting(
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string Url,
    string Description,
    string Source,
    string Salary,
    DateTime? PublishedAt,
    bool Remote,
    IReadOnlyList<string> Tags);

public sealed record JobResult(JobPosting Job, JobMatch Match);

public sealed record JobSearchResult(List<JobResult> Items, bool FromNetwork, string? Warning, int TotalFetched);

public sealed class JobBoardService(IHttpClientFactory httpFactory, ILogger<JobBoardService> logger, IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly ConcurrentDictionary<string, (DateTime FetchedAt, List<JobPosting> Jobs)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);

    public async Task<JobSearchResult> SearchAsync(string? query, string? location, bool remoteOnly,
        ResumeDocument? resume, CancellationToken ct = default)
    {
        query = query?.Trim() ?? string.Empty;
        location = location?.Trim() ?? string.Empty;

        var tasks = new List<Task<(List<JobPosting> Jobs, string? Error)>>
        {
            FetchArbeitnowAsync(ct)
        };
        tasks.Add(FetchRemotiveAsync(query, ct));

        var results = await Task.WhenAll(tasks);
        var all = results.SelectMany(r => r.Jobs).ToList();
        var errors = results.Where(r => !string.IsNullOrWhiteSpace(r.Error)).Select(r => r.Error!).ToList();

        if (all.Count == 0)
        {
            return new JobSearchResult(new List<JobResult>(), false,
                errors.FirstOrDefault() ?? "Não foi possível carregar vagas agora. Verifique sua conexão e tente novamente.", 0);
        }

        IEnumerable<JobPosting> filtered = all;
        if (query.Length > 0)
        {
            var terms = TextAnalysis.Tokenize(query).Where(t => t.Length > 2 && !StopWords.Contains(t)).ToArray();
            filtered = filtered.Where(j => terms.Length == 0 ||
                terms.Any(t => TextAnalysis.ContainsKeyword(j.Title, t) || TextAnalysis.ContainsKeyword(j.Description, t) ||
                               j.Tags.Any(tag => TextAnalysis.ContainsKeyword(tag, t))));
        }
        if (location.Length > 0)
        {
            filtered = filtered.Where(j => TextAnalysis.ContainsKeyword(j.Location, location) ||
                                           (remoteOnly && j.Remote));
        }
        if (remoteOnly)
            filtered = filtered.Where(j => j.Remote || TextAnalysis.ContainsKeyword(j.Location, "remote") || TextAnalysis.ContainsKeyword(j.Location, "remoto"));

        var list = filtered
            .GroupBy(j => (j.Title.ToLowerInvariant(), j.Company.ToLowerInvariant()))
            .Select(g => g.First())
            .Take(180)
            .ToList();

        var scored = list.Select(job =>
        {
            var match = resume is null
                ? new JobMatch(0, new List<string>(), new List<string>(), "Selecione um currículo para calcular a aderência.")
                : JobMatcher.Score(resume, job.Title, job.Description, job.Location);
            return new JobResult(job, match);
        })
            .OrderByDescending(r => r.Match.Score)
            .ThenByDescending(r => r.Job.PublishedAt)
            .Take(60)
            .ToList();

        var warning = errors.Count > 0 ? "Uma das fontes de vagas está indisponível; mostrando resultados da outra." : null;
        return new JobSearchResult(scored, true, warning, all.Count);
    }

    // ------------------------------------------------------------------ sources
    private async Task<(List<JobPosting> Jobs, string? Error)> FetchArbeitnowAsync(CancellationToken ct)
    {
        const string key = "arbeitnow";
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return (cached.Jobs, null);

        try
        {
            var http = httpFactory.CreateClient("jobs");
            var response = await http.GetAsync("https://www.arbeitnow.com/api/job-board-api", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var jobs = new List<JobPosting>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var description = TextAnalysis.StripHtml(GetString(item, "description"));
                    var tags = item.TryGetProperty("tags", out var tagArray)
                        ? tagArray.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList()
                        : new List<string>();
                    jobs.Add(new JobPosting(
                        ExternalId: GetString(item, "slug"),
                        Title: TextAnalysis.Clean(GetString(item, "title")),
                        Company: TextAnalysis.Clean(GetString(item, "company_name")),
                        Location: TextAnalysis.Clean(GetString(item, "location")),
                        Url: GetString(item, "url"),
                        Description: description,
                        Source: "Arbeitnow",
                        Salary: string.Empty,
                        PublishedAt: TryUnix(item, "created_at"),
                        Remote: item.TryGetProperty("remote", out var remote) && remote.ValueKind == JsonValueKind.True,
                        Tags: tags));
                }
            }
            Cache[key] = (DateTime.UtcNow, jobs);
            return (jobs, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao buscar vagas no Arbeitnow");
            return (new List<JobPosting>(), "Arbeitnow indisponível.");
        }
    }

    private async Task<(List<JobPosting> Jobs, string? Error)> FetchRemotiveAsync(string query, CancellationToken ct)
    {
        var key = "remotive:" + query.ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            return (cached.Jobs, null);

        try
        {
            var http = httpFactory.CreateClient("jobs");
            var url = "https://remotive.com/api/remote-jobs?limit=80";
            if (!string.IsNullOrWhiteSpace(query)) url += "&search=" + Uri.EscapeDataString(query);
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var jobs = new List<JobPosting>();
            if (doc.RootElement.TryGetProperty("jobs", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var location = TextAnalysis.Clean(GetString(item, "candidate_required_location"));
                    jobs.Add(new JobPosting(
                        ExternalId: "remotive-" + GetString(item, "id"),
                        Title: TextAnalysis.Clean(GetString(item, "title")),
                        Company: TextAnalysis.Clean(GetString(item, "company_name")),
                        Location: location,
                        Url: GetString(item, "url"),
                        Description: TextAnalysis.StripHtml(GetString(item, "description")),
                        Source: "Remotive",
                        Salary: TextAnalysis.Clean(GetString(item, "salary")),
                        PublishedAt: DateTime.TryParse(GetString(item, "publication_date"), out var dt) ? dt : null,
                        Remote: true,
                        Tags: TextAnalysis.Clean(GetString(item, "category")) is { Length: > 0 } cat ? new[] { cat } : Array.Empty<string>()));
                }
            }
            Cache[key] = (DateTime.UtcNow, jobs);
            return (jobs, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao buscar vagas no Remotive");
            return (new List<JobPosting>(), "Remotive indisponível.");
        }
    }

    // ------------------------------------------------------------------ saved jobs
    public async Task<bool> IsSavedAsync(Guid userId, string externalId, string url, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SavedJobs.AnyAsync(j => j.UserId == userId && (j.ExternalId == externalId || (url.Length > 0 && j.Url == url)), ct);
    }

    public async Task SaveJobAsync(Guid userId, JobPosting job, int score, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exists = await db.SavedJobs.AnyAsync(j => j.UserId == userId && (j.ExternalId == job.ExternalId || j.Url == job.Url), ct);
        if (exists) return;
        db.SavedJobs.Add(new SavedJob
        {
            UserId = userId,
            ExternalId = job.ExternalId,
            Title = job.Title,
            Company = job.Company,
            Location = job.Location,
            Url = job.Url,
            Description = TextAnalysis.Truncate(job.Description, 20000),
            Source = job.Source,
            MatchScore = score
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSavedJobAsync(Guid userId, string externalId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var saved = await db.SavedJobs.FirstOrDefaultAsync(j => j.UserId == userId && j.ExternalId == externalId, ct);
        if (saved is null) return;
        db.SavedJobs.Remove(saved);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<SavedJob>> ListSavedAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SavedJobs.AsNoTracking().Where(j => j.UserId == userId).OrderByDescending(j => j.SavedAt).ToListAsync(ct);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTime? TryUnix(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return null;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "vaga", "vagas", "job", "jobs", "remoto", "remote", "para", "com", "uma", "das", "dos", "and", "the"
    };
}
