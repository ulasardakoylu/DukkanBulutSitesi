using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Entity.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplaintTypes",
                columns: table => new
                {
                    ComplaintTypeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintTypes", x => x.ComplaintTypeID);
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    ComplaintID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemID = table.Column<int>(type: "int", nullable: true),
                    CommentID = table.Column<int>(type: "int", nullable: true),
                    ReporterUserID = table.Column<int>(type: "int", nullable: false),
                    TargetUserID = table.Column<int>(type: "int", nullable: true),
                    ComplaintTypeID = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ManagerNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.ComplaintID);
                    table.ForeignKey(
                        name: "FK_Complaints_ComplaintTypes_ComplaintTypeID",
                        column: x => x.ComplaintTypeID,
                        principalTable: "ComplaintTypes",
                        principalColumn: "ComplaintTypeID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Complaints_ItemComments_CommentID",
                        column: x => x.CommentID,
                        principalTable: "ItemComments",
                        principalColumn: "CommentID");
                    table.ForeignKey(
                        name: "FK_Complaints_Items_ItemID",
                        column: x => x.ItemID,
                        principalTable: "Items",
                        principalColumn: "ItemID");
                    table.ForeignKey(
                        name: "FK_Complaints_Users_ReporterUserID",
                        column: x => x.ReporterUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Complaints_Users_TargetUserID",
                        column: x => x.TargetUserID,
                        principalTable: "Users",
                        principalColumn: "UserID");
                });

            migrationBuilder.InsertData(
                table: "ComplaintTypes",
                columns: new[] { "ComplaintTypeID", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "Ürünle ilgili" },
                    { 2, true, "Yorum / Davranış" },
                    { 3, true, "Diğer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_CommentID",
                table: "Complaints",
                column: "CommentID");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ComplaintTypeID",
                table: "Complaints",
                column: "ComplaintTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ItemID",
                table: "Complaints",
                column: "ItemID");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ReporterUserID",
                table: "Complaints",
                column: "ReporterUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_TargetUserID",
                table: "Complaints",
                column: "TargetUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropTable(
                name: "ComplaintTypes");
        }
    }
}
