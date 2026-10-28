using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

public sealed class ResumeStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<ResumeEntity>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Resumes.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.IsDefault).ThenByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<ResumeEntity?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
    }

    public async Task<ResumeDocument?> GetDocumentAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await GetAsync(id, userId, ct);
        return entity is null ? null : Json.Deserialize<ResumeDocument>(entity.DataJson);
    }

    public async Task<ResumeEntity> CreateAsync(Guid userId, ResumeDocument document, string title,
        string template = "aurora", string accent = "#1F6F5C", string language = "pt", CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hasAny = await db.Resumes.AnyAsync(r => r.UserId == userId, ct);
        var entity = new ResumeEntity
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Meu currículo" : title.Trim(),
            TemplateId = template,
            AccentColor = accent,
            Language = language,
            DataJson = Json.Serialize(document),
            IsDefault = !hasAny,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Resumes.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task SaveAsync(Guid id, Guid userId, ResumeDocument document, string? title = null,
        string? template = null, string? accent = null, string? language = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (entity is null) return;
        entity.DataJson = Json.Serialize(document);
        entity.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(title)) entity.Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(template)) entity.TemplateId = template;
        if (!string.IsNullOrWhiteSpace(accent)) entity.AccentColor = accent;
        if (!string.IsNullOrWhiteSpace(language)) entity.Language = language;
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(Guid id, Guid userId, string title, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (entity is null) return;
        entity.Title = string.IsNullOrWhiteSpace(title) ? entity.Title : title.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ResumeEntity?> DuplicateAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var original = await GetAsync(id, userId, ct);
        if (original is null) return null;
        var doc = Json.Deserialize<ResumeDocument>(original.DataJson) ?? new ResumeDocument();
        return await CreateAsync(userId, doc, original.Title + " (cópia)", original.TemplateId, original.AccentColor, original.Language, ct);
    }

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (entity is null) return;
        var wasDefault = entity.IsDefault;
        db.Resumes.Remove(entity);
        await db.SaveChangesAsync(ct);
        if (!wasDefault) return;
        var next = await db.Resumes.Where(r => r.UserId == userId).OrderByDescending(r => r.UpdatedAt).FirstOrDefaultAsync(ct);
        if (next is not null)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetDefaultAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var all = await db.Resumes.Where(r => r.UserId == userId).ToListAsync(ct);
        foreach (var r in all) r.IsDefault = r.Id == id;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ResumeEntity>> RecentAsync(Guid userId, int take = 3, CancellationToken ct = default) =>
        (await ListAsync(userId, ct)).Take(take).ToList();
}

public sealed class ApplicationStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<ApplicationEntity>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<ApplicationEntity?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    public async Task SaveAsync(ApplicationEntity entity, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        entity.UpdatedAt = DateTime.UtcNow;
        var exists = await db.Applications.AnyAsync(a => a.Id == entity.Id && a.UserId == entity.UserId, ct);
        if (exists) db.Applications.Update(entity);
        else db.Applications.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationEntity?> AddFromJobAsync(Guid userId, string company, string position, string location,
        string url, string source, string description, int matchScore, string status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Applications.FirstOrDefaultAsync(a => a.UserId == userId && a.Url == url && a.Position == position, ct);
        if (existing is not null) return existing;

        var entity = new ApplicationEntity
        {
            UserId = userId,
            Company = company,
            Position = position,
            Location = location,
            Url = url,
            Source = source,
            JobDescription = description,
            MatchScore = matchScore,
            Status = status,
            AppliedAt = status == ApplicationStatuses.Applied ? DateTime.UtcNow : null
        };
        db.Applications.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Applications.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (entity is null) return;
        db.Applications.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class CoverLetterStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<CoverLetterEntity>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CoverLetters.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.UpdatedAt).ToListAsync(ct);
    }

    public async Task<CoverLetterEntity?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CoverLetters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
    }

    public async Task SaveAsync(CoverLetterEntity entity, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        entity.UpdatedAt = DateTime.UtcNow;
        var exists = await db.CoverLetters.AnyAsync(c => c.Id == entity.Id && c.UserId == entity.UserId, ct);
        if (exists) db.CoverLetters.Update(entity);
        else db.CoverLetters.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.CoverLetters.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (entity is null) return;
        db.CoverLetters.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ChatStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<ChatThread>> ThreadsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatThreads.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.UpdatedAt).Take(30).ToListAsync(ct);
    }

    public async Task<ChatThread?> GetThreadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChatThreads.AsNoTracking()
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
    }

    public async Task<ChatThread> CreateThreadAsync(Guid userId, Guid? resumeId, string title, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var thread = new ChatThread { UserId = userId, ResumeId = resumeId, Title = TextAnalysis.Truncate(title, 80) };
        db.ChatThreads.Add(thread);
        await db.SaveChangesAsync(ct);
        return thread;
    }

    public async Task AddMessageAsync(Guid threadId, string role, string content, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ChatMessages.Add(new ChatMessageEntity { ThreadId = threadId, Role = role, Content = content });
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread is not null) thread.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteThreadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (thread is null) return;
        db.ChatThreads.Remove(thread);
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameThreadAsync(Guid id, Guid userId, string title, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (thread is null) return;
        thread.Title = TextAnalysis.Truncate(title, 80);
        await db.SaveChangesAsync(ct);
    }
}
