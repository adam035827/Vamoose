using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vamoose.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignWithUpdatedRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Url",
                table: "PlanOptions",
                newName: "SourceUrl");

            migrationBuilder.RenameColumn(
                name: "Price",
                table: "PlanOptions",
                newName: "EstimatedCost");

            migrationBuilder.AddColumn<int>(
                name: "ClosedVersion",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResponseDeadline",
                table: "Trips",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessToken",
                table: "TripMembers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LinkSentAt",
                table: "TripMembers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnergyLevel",
                table: "PlanOptions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriceFetchedAt",
                table: "PlanOptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "PlanItems",
                type: "date",
                nullable: true);

            // Existing members need their own magic-link secret before the unique index is created.
            migrationBuilder.Sql(
                "UPDATE TripMembers SET AccessToken = REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '') + REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '') WHERE AccessToken = ''");

            // Categories now match the agent's DraftItem (requirements 3.4).
            migrationBuilder.Sql("UPDATE PlanItems SET Category = 'Lodging' WHERE Category = 'Hotel'");
            migrationBuilder.Sql("UPDATE PlanItems SET Category = 'Dining' WHERE Category = 'Restaurant'");

            migrationBuilder.CreateIndex(
                name: "IX_TripMembers_AccessToken",
                table: "TripMembers",
                column: "AccessToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE PlanItems SET Category = 'Hotel' WHERE Category = 'Lodging'");
            migrationBuilder.Sql("UPDATE PlanItems SET Category = 'Restaurant' WHERE Category = 'Dining'");

            migrationBuilder.DropIndex(
                name: "IX_TripMembers_AccessToken",
                table: "TripMembers");

            migrationBuilder.DropColumn(
                name: "ClosedVersion",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ResponseDeadline",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "AccessToken",
                table: "TripMembers");

            migrationBuilder.DropColumn(
                name: "LinkSentAt",
                table: "TripMembers");

            migrationBuilder.DropColumn(
                name: "EnergyLevel",
                table: "PlanOptions");

            migrationBuilder.DropColumn(
                name: "PriceFetchedAt",
                table: "PlanOptions");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "PlanItems");

            migrationBuilder.RenameColumn(
                name: "SourceUrl",
                table: "PlanOptions",
                newName: "Url");

            migrationBuilder.RenameColumn(
                name: "EstimatedCost",
                table: "PlanOptions",
                newName: "Price");
        }
    }
}
