using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Entity.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePersonelShiftForDayPilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Personels_PersonelID",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Personels_PersonelID1",
                table: "Conversations");

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
                name: "IX_PersonelHistories_PersonelID1",
                table: "PersonelHistories");

            migrationBuilder.DropIndex(
                name: "IX_PersonelEducations_PersonelID1",
                table: "PersonelEducations");

            migrationBuilder.DropIndex(
                name: "IX_PersonelCertificates_PersonelID1",
                table: "PersonelCertificates");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_PersonelID1",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelWarnings");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelShifts");

            migrationBuilder.DropColumn(
                name: "ShiftDay",
                table: "PersonelShifts");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelHistories");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelEducations");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "PersonelCertificates");

            migrationBuilder.DropColumn(
                name: "PersonelID1",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsFromManager",
                table: "ConversationMessages");

            migrationBuilder.RenameColumn(
                name: "ShiftDateStart",
                table: "PersonelShifts",
                newName: "StartTime");

            migrationBuilder.RenameColumn(
                name: "ShiftDateEnd",
                table: "PersonelShifts",
                newName: "ShiftDate");

            migrationBuilder.RenameColumn(
                name: "PersonelID",
                table: "Conversations",
                newName: "TargetUserID");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_PersonelID",
                table: "Conversations",
                newName: "IX_Conversations_TargetUserID");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "ConversationMessages",
                newName: "Text");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "PersonelShifts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndTime",
                table: "PersonelShifts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureLink",
                table: "Personels",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StarterUserID",
                table: "Conversations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SenderUserID",
                table: "ConversationMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_StarterUserID",
                table: "Conversations",
                column: "StarterUserID");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_SenderUserID",
                table: "ConversationMessages",
                column: "SenderUserID");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_Users_SenderUserID",
                table: "ConversationMessages",
                column: "SenderUserID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_StarterUserID",
                table: "Conversations",
                column: "StarterUserID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_TargetUserID",
                table: "Conversations",
                column: "TargetUserID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_Users_SenderUserID",
                table: "ConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Users_StarterUserID",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Users_TargetUserID",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_StarterUserID",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMessages_SenderUserID",
                table: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "PersonelShifts");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "PersonelShifts");

            migrationBuilder.DropColumn(
                name: "ProfilePictureLink",
                table: "Personels");

            migrationBuilder.DropColumn(
                name: "StarterUserID",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SenderUserID",
                table: "ConversationMessages");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "PersonelShifts",
                newName: "ShiftDateStart");

            migrationBuilder.RenameColumn(
                name: "ShiftDate",
                table: "PersonelShifts",
                newName: "ShiftDateEnd");

            migrationBuilder.RenameColumn(
                name: "TargetUserID",
                table: "Conversations",
                newName: "PersonelID");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_TargetUserID",
                table: "Conversations",
                newName: "IX_Conversations_PersonelID");

            migrationBuilder.RenameColumn(
                name: "Text",
                table: "ConversationMessages",
                newName: "Message");

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

            migrationBuilder.AddColumn<string>(
                name: "ShiftDay",
                table: "PersonelShifts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.AddColumn<int>(
                name: "PersonelID1",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFromManager",
                table: "ConversationMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PersonelWarnings_PersonelID1",
                table: "PersonelWarnings",
                column: "PersonelID1");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelShifts_PersonelID1",
                table: "PersonelShifts",
                column: "PersonelID1");

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

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_PersonelID1",
                table: "Conversations",
                column: "PersonelID1");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Personels_PersonelID",
                table: "Conversations",
                column: "PersonelID",
                principalTable: "Personels",
                principalColumn: "PersonelID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Personels_PersonelID1",
                table: "Conversations",
                column: "PersonelID1",
                principalTable: "Personels",
                principalColumn: "PersonelID");

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
    }
}
