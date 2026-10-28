using BackgroundJobs.Core.Imports;
using BackgroundJobs.Core.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackgroundJobs.Infrastructure.Persistence.Configurations;

internal sealed class ImportFileConfiguration : IEntityTypeConfiguration<ImportFile>
{
    public void Configure(EntityTypeBuilder<ImportFile> builder)
    {
        builder.HasKey(f => f.JobRunId);
        builder.Property(f => f.OriginalName).HasMaxLength(ImportFile.NameMaxLength).IsRequired();
        builder.Property(f => f.StoredPath).HasMaxLength(300);
        builder.Property(f => f.ErrorFilePath).HasMaxLength(300);
        builder.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasOne<JobRun>().WithOne().HasForeignKey<ImportFile>(f => f.JobRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => f.UploadedAt);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_orders_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_orders_price_positive", "unit_price_minor > 0");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).UseIdentityAlwaysColumn();
        builder.Property(o => o.OrderNo).HasMaxLength(40).IsRequired();
        builder.Property(o => o.CustomerEmail).HasMaxLength(254).IsRequired();
        builder.Property(o => o.Sku).HasMaxLength(40).IsRequired();

        // Imports insert with ON CONFLICT (order_no) DO NOTHING, which needs this index.
        builder.HasIndex(o => o.OrderNo).IsUnique();
        builder.HasIndex(o => o.OrderedAt);
        builder.HasIndex(o => o.ImportFileId);

        builder.HasOne<ImportFile>().WithMany().HasForeignKey(o => o.ImportFileId).OnDelete(DeleteBehavior.Restrict);
    }
}
