using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Entity.Migrations
{
    /// <inheritdoc />
    public partial class Testing1212 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonelPasswordHash",
                table: "Personels");

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "PersonelWarnings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "PersonelShifts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserID",
                table: "Personels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "PersonelHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "PersonelEducations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "PersonelCertificates",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonelWarnings_PersonelID1",
                table: "PersonelWarnings",
                column: "PersonelID1");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelShifts_PersonelID1",
                table: "PersonelShifts",
                column: "PersonelID1");

            migrationBuilder.CreateIndex(
                name: "IX_Personels_UserID",
                table: "Personels",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelHistories_PersonelID1",
                table: "PersonelHistories",
                column: "PersonelID1");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelEducations_PersonelID1",
                table: "PersonelEducations",
                column: "PersonelID1");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelCertificates_PersonelID1",
                table: "PersonelCertificates",
                column: "PersonelID1");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonelCertificates_Personels_PersonelID1",
                table: "PersonelCertificates",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonelEducations_Personels_PersonelID1",
                table: "PersonelEducations",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonelHistories_Personels_PersonelID1",
                table: "PersonelHistories",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");

            migrationBuilder.AddForeignKey(
                name: "FK_Personels_Users_UserID",
                table: "Personels",
                column: "UserID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonelShifts_Personels_PersonelID1",
                table: "PersonelShifts",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonelWarnings_Personels_PersonelID1",
                table: "PersonelWarnings",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PersonelCertificates_Personels_PersonelID1",
                table: "PersonelCertificates");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonelEducations_Personels_PersonelID1",
                table: "PersonelEducations");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonelHistories_Personels_PersonelID1",
                table: "PersonelHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_Personels_Users_UserID",
                table: "Personels");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonelShifts_Personels_PersonelID1",
                table: "PersonelShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonelWarnings_Personels_PersonelID1",
                table: "PersonelWarnings");

            migrationBuilder.DropIndex(
                name: "IX_PersonelWarnings_PersonelID1",
                table: "PersonelWarnings");

            migrationBuilder.DropIndex(
                name: "IX_PersonelShifts_PersonelID1",
                table: "PersonelShifts");

            migrationBuilder.DropIndex(
                name: "IX_Personels_UserID",
                table: "Personels");

            migrationBuilder.DropIndex(
                name: "IX_PersonelHistories_PersonelID1",
                table: "PersonelHistories");

            migrationBuilder.DropIndex(
                name: "IX_PersonelEducations_PersonelID1",
                table: "PersonelEducations");

            migrationBuilder.DropIndex(
                name: "IX_PersonelCertificates_PersonelID1",
                table: "PersonelCertificates");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelWarnings");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelShifts");

            migrationBuilder.DropColumn(
                name: "UserID",
                table: "Personels");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelHistories");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelEducations");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelCertificates");

            migrationBuilder.AddColumn<string>(
                name: "PersonelPasswordHash",
                table: "Personels",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
