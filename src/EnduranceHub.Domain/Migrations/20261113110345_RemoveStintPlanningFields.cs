using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnduranceHub.Domain.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStintPlanningFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Stints");

            migrationBuilder.DropColumn(
                name: "PlannedFuelLiters",
                table: "Stints");

            migrationBuilder.DropColumn(
                name: "PlannedLaps",
                table: "Stints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Stints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlannedFuelLiters",
                table: "Stints",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedLaps",
                table: "Stints",
                type: "integer",
                nullable: true);
        }
    }
}
