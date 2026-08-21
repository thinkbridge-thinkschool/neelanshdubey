using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <summary>
    /// Explicitly drops the index EF Core created by convention on the
    /// Quotes.AuthorId FK column. Day 11 Task 1 profiles the N+1 query
    /// pattern against a schema with zero index on that column, so this
    /// migration removes it right after InitialCreate adds it.
    /// </summary>
    public partial class DropQuoteAuthorIdIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_AuthorId",
                table: "Quotes");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Quotes_AuthorId",
                table: "Quotes",
                column: "AuthorId");
        }
    }
}
