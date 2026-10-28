using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackgroundJobs.Infrastructure.Persistence.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(r => r.JobRunId);
        builder.Property(r => r.StoredPath).HasMaxLength(300);
        builder.HasOne<JobRun>().WithOne().HasForeignKey<Report>(r => r.JobRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.From, r.To });
    }
}
