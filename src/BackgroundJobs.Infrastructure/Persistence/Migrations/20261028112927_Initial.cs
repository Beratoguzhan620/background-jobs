using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackgroundJobs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    hangfire_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                    table.CheckConstraint("ck_job_runs_progress_range", "progress BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_job_runs_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id",
                table: "audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_target_type_target_id",
                table: "audit_log",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_kind_number",
                table: "job_runs",
                columns: new[] { "kind", "number" });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_number",
                table: "job_runs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_requested_by",
                table: "job_runs",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_status_number",
                table: "job_runs",
                columns: new[] { "status", "number" });

            migrationBuilder.CreateIndex(
                name: "ux_job_runs_dedup_key_active",
                table: "job_runs",
                column: "dedup_key",
                unique: true,
                filter: "dedup_key IS NOT NULL AND status IN ('Queued', 'Running', 'Retrying', 'Succeeded')");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
