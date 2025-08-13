using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortUrl.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToVCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "VCards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "VCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "VCards");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "VCards");
        }
    }
}
