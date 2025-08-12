using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortUrl.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeNumberFaxMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fax",
                table: "VCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                table: "VCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficeNumber",
                table: "VCards",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fax",
                table: "VCards");

            migrationBuilder.DropColumn(
                name: "Mobile",
                table: "VCards");

            migrationBuilder.DropColumn(
                name: "OfficeNumber",
                table: "VCards");
        }
    }
}
