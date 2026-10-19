using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Thumbnails",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Thumbnails");
        }
    }
}
