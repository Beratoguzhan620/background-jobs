using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackgroundJobs.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobsDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=background_jobs_design");
        return new JobsDbContext(options.Options);
    }
}
