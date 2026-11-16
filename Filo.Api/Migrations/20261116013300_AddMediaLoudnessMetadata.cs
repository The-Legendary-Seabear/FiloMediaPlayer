using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLoudnessMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "IntegratedLufs",
                table: "MediaFiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoudnessAnalyzedAt",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LoudnessRangeLu",
                table: "MediaFiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LoudnessThresholdLufs",
                table: "MediaFiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NormalizationGainDb",
                table: "MediaFiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TruePeakDbfs",
                table: "MediaFiles",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntegratedLufs",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "LoudnessAnalyzedAt",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "LoudnessRangeLu",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "LoudnessThresholdLufs",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "NormalizationGainDb",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "TruePeakDbfs",
                table: "MediaFiles");
        }
    }
}
