using Microsoft.EntityFrameworkCore;

namespace Vita.App.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ResumeEntity> Resumes => Set<ResumeEntity>();
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    public DbSet<CoverLetterEntity> CoverLetters => Set<CoverLetterEntity>();
    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.NormalizedEmail).HasMaxLength(320);
        });

        b.Entity<ResumeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Title).HasMaxLength(200);
        });

        b.Entity<ApplicationEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Position).HasMaxLength(200);
        });

        b.Entity<CoverLetterEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<ChatThread>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.ThreadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChatMessageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ThreadId);
        });

        b.Entity<AiSettings>(e => e.HasKey(x => x.UserId));
        b.Entity<SavedJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
        });
    }
}
