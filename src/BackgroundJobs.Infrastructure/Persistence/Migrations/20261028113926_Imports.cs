using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackgroundJobs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_files",
                columns: table => new
                {
                    job_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stored_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    rows_read = table.Column<int>(type: "integer", nullable: false),
                    rows_imported = table.Column<int>(type: "integer", nullable: false),
                    rows_duplicate = table.Column<int>(type: "integer", nullable: false),
                    rows_rejected = table.Column<int>(type: "integer", nullable: false),
                    error_file_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_files", x => x.job_run_id);
                    table.ForeignKey(
                        name: "fk_import_files_job_runs_job_run_id",
                        column: x => x.job_run_id,
                        principalTable: "job_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    order_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    import_file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_price_positive", "unit_price_minor > 0");
                    table.CheckConstraint("ck_orders_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_orders_import_files_import_file_id",
                        column: x => x.import_file_id,
                        principalTable: "import_files",
                        principalColumn: "job_run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_files_uploaded_at",
                table: "import_files",
                column: "uploaded_at");

            migrationBuilder.CreateIndex(
                name: "ix_orders_import_file_id",
                table: "orders",
                column: "import_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_no",
                table: "orders",
                column: "order_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_ordered_at",
                table: "orders",
                column: "ordered_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "import_files");
        }
    }
}
