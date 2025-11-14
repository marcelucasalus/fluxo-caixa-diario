using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Store.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidadosDiarios",
                columns: table => new
                {
                    DataConsolidacao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalCreditos = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDebitos = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidadosDiarios", x => x.DataConsolidacao);
                });

            migrationBuilder.CreateTable(
                name: "Lancamentos",
                columns: table => new
                {
                    IdLancamento = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "char(1)", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataLancamento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Descricao = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataConsolidacao = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lancamentos", x => x.IdLancamento);
                    table.ForeignKey(
                        name: "FK_Lancamentos_ConsolidadosDiarios_DataConsolidacao",
                        column: x => x.DataConsolidacao,
                        principalTable: "ConsolidadosDiarios",
                        principalColumn: "DataConsolidacao",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_DataConsolidacao",
                table: "Lancamentos",
                column: "DataConsolidacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lancamentos");

            migrationBuilder.DropTable(
                name: "ConsolidadosDiarios");
        }
    }
}
