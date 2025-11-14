using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Store.Identity;

namespace CommandStore.FluxoCaixa
{
    public class FluxoCaixaContext : IdentityDbContext<ApplicationUser>
    {
        public FluxoCaixaContext(DbContextOptions<FluxoCaixaContext> options)
            : base(options)
        { }

        public DbSet<Lancamento> Lancamentos { get; set; }
        public DbSet<ConsolidadoDiario> ConsolidadosDiarios { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ======= ENTIDADE ConsolidadoDiario =======
            modelBuilder.Entity<ConsolidadoDiario>(entity =>
            {
                // Chave primária
                entity.HasKey(cd => cd.DataConsolidacao);

                entity.Property(cd => cd.DataConsolidacao)
                      .IsRequired();

                entity.Property(cd => cd.TotalCreditos)
                      .HasColumnType("decimal(18,2)")
                      .IsRequired();

                entity.Property(cd => cd.TotalDebitos)
                      .HasColumnType("decimal(18,2)")
                      .IsRequired();

                // Relação 1:N com Lancamento
                entity.HasMany(cd => cd.Lancamentos)
                      .WithOne() // sem propriedade de navegação em Lancamento
                      .HasForeignKey(l => l.DataConsolidacao)
                      .OnDelete(DeleteBehavior.Cascade); // ao excluir o consolidado, remove os lançamentos
            });

            // ======= ENTIDADE Lancamento =======
            modelBuilder.Entity<Lancamento>(entity =>
            {
                // Chave primária
                entity.HasKey(l => l.IdLancamento);

                entity.Property(l => l.Tipo)
                      .IsRequired()
                      .HasColumnType("char(1)");

                entity.Property(l => l.Valor)
                      .HasColumnType("decimal(18,2)")
                      .IsRequired();

                entity.Property(l => l.DataLancamento)
                      .IsRequired();

                entity.Property(l => l.Descricao)
                      .HasMaxLength(200)
                      .IsUnicode(false);

                entity.Property(l => l.Status)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(l => l.DataConsolidacao)
                      .IsRequired();
            });
        }



    }
}
