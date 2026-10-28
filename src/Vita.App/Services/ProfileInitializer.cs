using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

public static class ProfileInitializer
{
    public static async Task<AppUser> EnsureAsync(AppDbContext db, ProfileInfo profile, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        await RemoveLegacyDemoDataAsync(db, ct);

        var user = await db.Users.FirstOrDefaultAsync(ct);
        if (user is null)
        {
            user = new AppUser
            {
                FullName = profile.Name,
                Email = profile.Email,
                NormalizedEmail = profile.Email,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);

            var draft = new ResumeDocument();
            draft.Contact.FullName = profile.Name;
            db.Resumes.Add(new ResumeEntity
            {
                UserId = user.Id,
                Title = "Meu currículo",
                IsDefault = true,
                DataJson = Json.Serialize(draft)
            });

            await db.SaveChangesAsync(ct);
            return user;
        }

        if (!string.Equals(user.FullName, profile.Name, StringComparison.Ordinal))
        {
            user.FullName = profile.Name;
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    // Old versions shipped a demo account. Clean it from any legacy database
    // that is opened, keeping the IsDemo guard so a real profile is never hit.
    public static async Task RemoveLegacyDemoDataAsync(AppDbContext db, CancellationToken ct = default)
    {
        var demoUsers = await db.Users.Where(u => u.IsDemo).ToListAsync(ct);
        if (demoUsers.Count == 0) return;

        var userIds = demoUsers.Select(u => u.Id).ToList();
        var threadIds = await db.ChatThreads
            .Where(t => userIds.Contains(t.UserId))
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (threadIds.Count > 0)
            db.ChatMessages.RemoveRange(db.ChatMessages.Where(m => threadIds.Contains(m.ThreadId)));

        db.ChatThreads.RemoveRange(db.ChatThreads.Where(t => userIds.Contains(t.UserId)));
        db.CoverLetters.RemoveRange(db.CoverLetters.Where(c => userIds.Contains(c.UserId)));
        db.Applications.RemoveRange(db.Applications.Where(a => userIds.Contains(a.UserId)));
        db.SavedJobs.RemoveRange(db.SavedJobs.Where(j => userIds.Contains(j.UserId)));
        db.Resumes.RemoveRange(db.Resumes.Where(r => userIds.Contains(r.UserId)));
        db.AiSettings.RemoveRange(db.AiSettings.Where(s => userIds.Contains(s.UserId)));
        db.Users.RemoveRange(demoUsers);

        await db.SaveChangesAsync(ct);
    }
}
