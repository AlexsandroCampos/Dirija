using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dirija.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrutoresEAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_slots_InstructorId_Start",
                table: "slots");

            migrationBuilder.DropIndex(
                name: "IX_instructors_IsVerified_Latitude_Longitude",
                table: "instructors");

            // IsVerified becomes Status (Approved / Pending). Instructors that existed before this migration are all
            // fictional demo instructors; the startup seed links two of them to demo logins.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "instructors",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.Sql("UPDATE instructors SET Status = 'Approved' WHERE IsVerified = 1;");

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "instructors",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "instructors");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "users",
                type: "TEXT",
                nullable: true);

            // Existing slots stay active (the model has no default; new rows always send the value).
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "slots",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "slots",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "instructors",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "instructors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedByUserId",
                table: "instructors",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "instructors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "instructors",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "bookings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            // Before this migration only students canceled; expired holds were canceled by the system.
            migrationBuilder.Sql("UPDATE bookings SET CanceledBy = 'Student' WHERE Status = 'Canceled' AND CancelReason = 'Cancelada pelo aluno';");
            migrationBuilder.Sql("UPDATE bookings SET CanceledBy = 'System' WHERE Status = 'Canceled' AND CanceledBy = 'None';");

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "bookings",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "time_offs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstructorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    End = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_offs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_time_offs_instructors_InstructorId",
                        column: x => x.InstructorId,
                        principalTable: "instructors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weekly_availability",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstructorId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinute = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekly_availability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weekly_availability_instructors_InstructorId",
                        column: x => x.InstructorId,
                        principalTable: "instructors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_slots_InstructorId_Start",
                table: "slots",
                columns: new[] { "InstructorId", "Start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instructors_Status_Latitude_Longitude",
                table: "instructors",
                columns: new[] { "Status", "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_instructors_UserId",
                table: "instructors",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_offs_InstructorId_Start_End",
                table: "time_offs",
                columns: new[] { "InstructorId", "Start", "End" });

            migrationBuilder.CreateIndex(
                name: "IX_weekly_availability_InstructorId",
                table: "weekly_availability",
                column: "InstructorId");

            migrationBuilder.AddForeignKey(
                name: "FK_instructors_users_UserId",
                table: "instructors",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_instructors_users_UserId",
                table: "instructors");

            migrationBuilder.DropTable(
                name: "time_offs");

            migrationBuilder.DropTable(
                name: "weekly_availability");

            migrationBuilder.DropIndex(
                name: "IX_slots_InstructorId_Start",
                table: "slots");

            migrationBuilder.DropIndex(
                name: "IX_instructors_Status_Latitude_Longitude",
                table: "instructors");

            migrationBuilder.DropIndex(
                name: "IX_instructors_UserId",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "slots");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "slots");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "instructors");

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "instructors",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE instructors SET IsVerified = 1 WHERE Status = 'Approved';");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "instructors");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "IX_slots_InstructorId_Start",
                table: "slots",
                columns: new[] { "InstructorId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_instructors_IsVerified_Latitude_Longitude",
                table: "instructors",
                columns: new[] { "IsVerified", "Latitude", "Longitude" });
        }
    }
}
