using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackgroundJobs.Infrastructure.Persistence.Configurations;

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable(table =>
            table.HasCheckConstraint("ck_job_runs_progress_range", "progress BETWEEN 0 AND 100"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).UseIdentityAlwaysColumn();
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.DedupKey).HasMaxLength(JobRun.DedupKeyMaxLength);
        builder.Property(r => r.HangfireJobId).HasMaxLength(64);
        builder.Property(r => r.Error).HasMaxLength(JobRun.ErrorMaxLength);
        builder.Property(r => r.Result).HasColumnType("jsonb");
        builder.Ignore(r => r.IsActive);

        // The same work may not be queued, running or done twice; failed and cancelled runs don't count.
        builder.HasIndex(r => r.DedupKey)
            .IsUnique()
            .HasFilter("dedup_key IS NOT NULL AND status IN ('Queued', 'Running', 'Retrying', 'Succeeded')")
            .HasDatabaseName("ux_job_runs_dedup_key_active");

        builder.HasIndex(r => new { r.Status, r.Number });
        builder.HasIndex(r => new { r.Kind, r.Number });

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
