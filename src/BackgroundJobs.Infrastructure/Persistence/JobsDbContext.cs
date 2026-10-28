using BackgroundJobs.Core.Audit;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Infrastructure.Persistence;

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<JobRun> JobRuns => Set<JobRun>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobsDbContext).Assembly);
    }
}
