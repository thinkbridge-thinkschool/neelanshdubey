using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <inheritdoc />
    public partial class FixCollectionItemQuoteIdType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "QuoteId",
                table: "CollectionItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_QuoteId",
                table: "CollectionItems",
                column: "QuoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionItems_Quotes_QuoteId",
                table: "CollectionItems",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionItems_Quotes_QuoteId",
                table: "CollectionItems");

            migrationBuilder.DropIndex(
                name: "IX_CollectionItems_QuoteId",
                table: "CollectionItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuoteId",
                table: "CollectionItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
