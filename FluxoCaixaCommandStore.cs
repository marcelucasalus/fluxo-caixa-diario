using FluxoCaixaDb.Models;

namespace Infrastructure.Database.CommandStore;

public class FluxoCaixaCommandStore : DbContext
{
    public FluxoCaixaCommandStore(DbContextOptions<FluxoCaixaCommandStore> options) : base(options)
    { }

    public DbSet<Lancamento> Lancamentos { get; set; }
    public DbSet<ConsolidadoDiario> ConsolidadosDiarios { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurando PK e relacionamento

        modelBuilder.Entity<ConsolidadoDiario>()
            .HasKey(c => c.DataConsolidacao);

        modelBuilder.Entity<ConsolidadoDiario>()
            .HasMany(c => c.Lancamentos)
            .WithOne(l => l.ConsolidadoDiario)
            .HasForeignKey(l => l.DataConsolidacao)
            .OnDelete(DeleteBehavior.Cascade); // opcional: deletar lançamentos se consolidado for deletado

        modelBuilder.Entity<Lancamento>()
            .HasKey(l => l.IdLancamento);



        base.OnModelCreating(modelBuilder);
    }
}