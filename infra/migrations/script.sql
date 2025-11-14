/* ============================================================
   DATABASE: FluxoCaixaDb
   Autor: Marcel Sales
   Objetivo: Criação, carga inicial e limpeza do banco
   ============================================================ */

-- 1️⃣ Cria o banco de dados se não existir
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'FluxoCaixaDb')
BEGIN
    CREATE DATABASE FluxoCaixaDb;
END
GO

USE FluxoCaixaDb;
GO

/* ============================================================
   2️⃣ Dispose (DROP TABLES)
   ============================================================ */

IF OBJECT_ID('dbo.Lancamento', 'U') IS NOT NULL
    DROP TABLE dbo.Lancamento;
GO

IF OBJECT_ID('dbo.ConsolidadoDiario', 'U') IS NOT NULL
    DROP TABLE dbo.ConsolidadoDiario;
GO

/* ============================================================
   3️⃣ Criação das Tabelas
   ============================================================ */

-- Tabela Consolidado Diário
CREATE TABLE dbo.ConsolidadoDiario (
    DataConsolidacao DATE NOT NULL PRIMARY KEY,
    TotalCreditos DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    TotalDebitos DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    Saldo AS (TotalCreditos - TotalDebitos) PERSISTED
);
GO

-- Tabela de Lançamentos
CREATE TABLE dbo.Lancamento (
    IdLancamento INT IDENTITY(1,1) PRIMARY KEY,
    Tipo CHAR(1) NOT NULL CHECK (Tipo IN ('C','D')),   -- C = Crédito, D = Débito
    Valor DECIMAL(18,2) NOT NULL,
    DataLancamento DATETIME NOT NULL DEFAULT GETDATE(),
    Descricao VARCHAR(255) NULL,
    DataConsolidacao DATE NULL,
    CONSTRAINT FK_Lancamento_Consolidado
        FOREIGN KEY (DataConsolidacao)
        REFERENCES dbo.ConsolidadoDiario (DataConsolidacao)
);
GO

-- Índices para performance
CREATE INDEX IDX_Lancamento_Data ON dbo.Lancamento (DataLancamento);
CREATE INDEX IDX_Lancamento_Tipo ON dbo.Lancamento (Tipo);
GO

/* ============================================================
   4️⃣ Seed (Dados de Exemplo)
   ============================================================ */

-- Consolidado diário inicial
INSERT INTO dbo.ConsolidadoDiario (DataConsolidacao, TotalCreditos, TotalDebitos)
VALUES 
    ('2025-11-10', 0, 0),
    ('2025-11-11', 0, 0),
    ('2025-11-12', 0, 0);
GO

-- Lançamentos (exemplos)
INSERT INTO dbo.Lancamento (Tipo, Valor, DataLancamento, Descricao, DataConsolidacao)
VALUES
    ('C', 1000.00, '2025-11-10T09:00:00', 'Venda de produto A', '2025-11-10'),
    ('D', 200.00,  '2025-11-10T10:00:00', 'Compra de insumos', '2025-11-10'),
    ('C', 500.00,  '2025-11-10T15:30:00', 'Venda de produto B', '2025-11-10'),

    ('C', 1500.00, '2025-11-11T08:45:00', 'Venda de produto C', '2025-11-11'),
    ('D', 300.00,  '2025-11-11T11:00:00', 'Pagamento de fornecedor', '2025-11-11'),
    ('D', 150.00,  '2025-11-11T14:15:00', 'Despesa de transporte', '2025-11-11'),

    ('C', 800.00,  '2025-11-12T09:10:00', 'Venda de produto D', '2025-11-12'),
    ('C', 200.00,  '2025-11-12T10:00:00', 'Serviço de manutenção', '2025-11-12'),
    ('D', 100.00,  '2025-11-12T16:00:00', 'Compra de material de escritório', '2025-11-12');
GO

/* ============================================================
   5️⃣ Atualiza os valores consolidados
   ============================================================ */

UPDATE c
SET 
    c.TotalCreditos = (
        SELECT ISNULL(SUM(l.Valor),0)
        FROM dbo.Lancamento l
        WHERE l.DataConsolidacao = c.DataConsolidacao
          AND l.Tipo = 'C'
    ),
    c.TotalDebitos = (
        SELECT ISNULL(SUM(l.Valor),0)
        FROM dbo.Lancamento l
        WHERE l.DataConsolidacao = c.DataConsolidacao
          AND l.Tipo = 'D'
    )
FROM dbo.ConsolidadoDiario c;
GO

/* ============================================================
   6️⃣ Consultas de verificação
   ============================================================ */

PRINT '--- Lançamentos ---';
SELECT * FROM dbo.Lancamento ORDER BY DataLancamento;

PRINT '--- Consolidado Diário ---';
SELECT * FROM dbo.ConsolidadoDiario ORDER BY DataConsolidacao;
GO
