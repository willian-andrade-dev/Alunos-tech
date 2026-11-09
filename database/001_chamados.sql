-- Execute em um banco vazio dedicado ao projeto.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.Usuario (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    Nome nvarchar(120) NOT NULL,
    Email nvarchar(254) NOT NULL CONSTRAINT UQ_Usuario_Email UNIQUE,
    Perfil tinyint NOT NULL CONSTRAINT CK_Usuario_Perfil CHECK (Perfil IN (0, 1))
);

-- Perfil: 0 = funcionário, 1 = técnico.
-- Status: 0 = aberto, 1 = em atendimento, 2 = resolvido.
CREATE TABLE dbo.Chamado (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_Chamado PRIMARY KEY,
    SolicitanteId uniqueidentifier NOT NULL REFERENCES dbo.Usuario(Id),
    TecnicoId uniqueidentifier NULL REFERENCES dbo.Usuario(Id),
    Titulo nvarchar(120) NOT NULL,
    Categoria nvarchar(60) NOT NULL,
    Descricao nvarchar(2000) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_Chamado_Status DEFAULT (0),
    CriadoEm datetimeoffset(3) NOT NULL,
    CONSTRAINT CK_Chamado_StatusTecnico CHECK (
        (Status = 0 AND TecnicoId IS NULL) OR
        (Status IN (1, 2) AND TecnicoId IS NOT NULL)
    )
);

CREATE TABLE dbo.EventoChamado (
    Id bigint IDENTITY NOT NULL CONSTRAINT PK_EventoChamado PRIMARY KEY,
    ChamadoId uniqueidentifier NOT NULL REFERENCES dbo.Chamado(Id),
    AutorId uniqueidentifier NOT NULL REFERENCES dbo.Usuario(Id),
    Tipo tinyint NOT NULL CONSTRAINT CK_EventoChamado_Tipo CHECK (Tipo BETWEEN 0 AND 3),
    Data datetimeoffset(3) NOT NULL,
    Descricao nvarchar(2000) NULL
);

CREATE INDEX IX_Chamado_Solicitante ON dbo.Chamado (SolicitanteId, CriadoEm DESC);
CREATE INDEX IX_Chamado_Fila ON dbo.Chamado (Status, CriadoEm);
CREATE INDEX IX_EventoChamado_LinhaDoTempo ON dbo.EventoChamado (ChamadoId, Id);

COMMIT TRANSACTION;
GO

CREATE OR ALTER PROCEDURE dbo.AssumirChamado
    @ChamadoId uniqueidentifier,
    @TecnicoId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE Id = @TecnicoId AND Perfil = 1)
            THROW 50001, 'O usuário precisa ter perfil técnico.', 1;

        UPDATE dbo.Chamado
        SET TecnicoId = @TecnicoId, Status = 1
        WHERE Id = @ChamadoId AND Status = 0;

        IF @@ROWCOUNT = 0
            THROW 50002, 'O chamado não existe ou já saiu da fila de abertos.', 1;

        INSERT dbo.EventoChamado (ChamadoId, AutorId, Tipo, Data)
        VALUES (@ChamadoId, @TecnicoId, 1, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
