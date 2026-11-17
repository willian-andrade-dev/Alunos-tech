-- PostgreSQL. Execute uma vez em um banco vazio dedicado ao projeto.
BEGIN;

CREATE TABLE public.Usuario (
    Id uuid NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    Nome varchar(120) NOT NULL,
    Email varchar(254) NOT NULL CONSTRAINT UQ_Usuario_Email UNIQUE,
    Perfil smallint NOT NULL CONSTRAINT CK_Usuario_Perfil CHECK (Perfil IN (0, 1))
);

-- Perfil: 0 = funcionário, 1 = técnico.
-- Status: 0 = aberto, 1 = em atendimento, 2 = resolvido.
CREATE TABLE public.Chamado (
    Id uuid NOT NULL CONSTRAINT PK_Chamado PRIMARY KEY,
    SolicitanteId uuid NOT NULL REFERENCES public.Usuario(Id),
    TecnicoId uuid NULL REFERENCES public.Usuario(Id),
    Titulo varchar(120) NOT NULL,
    Categoria varchar(60) NOT NULL,
    Descricao varchar(2000) NOT NULL,
    Status smallint NOT NULL CONSTRAINT DF_Chamado_Status DEFAULT 0,
    CriadoEm timestamptz(3) NOT NULL,
    CONSTRAINT CK_Chamado_StatusTecnico CHECK (
        (Status = 0 AND TecnicoId IS NULL) OR
        (Status IN (1, 2) AND TecnicoId IS NOT NULL)
    )
);

CREATE TABLE public.EventoChamado (
    Id bigint GENERATED ALWAYS AS IDENTITY NOT NULL CONSTRAINT PK_EventoChamado PRIMARY KEY,
    ChamadoId uuid NOT NULL REFERENCES public.Chamado(Id),
    AutorId uuid NOT NULL REFERENCES public.Usuario(Id),
    Tipo smallint NOT NULL CONSTRAINT CK_EventoChamado_Tipo CHECK (Tipo BETWEEN 0 AND 3),
    Data timestamptz(3) NOT NULL,
    Descricao varchar(2000) NULL
);

CREATE INDEX IX_Chamado_Solicitante ON public.Chamado (SolicitanteId, CriadoEm DESC);
CREATE INDEX IX_Chamado_Fila ON public.Chamado (Status, CriadoEm);
CREATE INDEX IX_EventoChamado_LinhaDoTempo ON public.EventoChamado (ChamadoId, Id);

CREATE OR REPLACE PROCEDURE public.AssumirChamado(
    p_chamado_id uuid,
    p_tecnico_id uuid
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM public.Usuario WHERE Id = p_tecnico_id AND Perfil = 1
    ) THEN
        RAISE EXCEPTION 'O usuário precisa ter perfil técnico.';
    END IF;

    UPDATE public.Chamado
    SET TecnicoId = p_tecnico_id, Status = 1
    WHERE Id = p_chamado_id AND Status = 0;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'O chamado não existe ou já saiu da fila de abertos.';
    END IF;

    INSERT INTO public.EventoChamado (ChamadoId, AutorId, Tipo, Data)
    VALUES (p_chamado_id, p_tecnico_id, 1, CURRENT_TIMESTAMP);
END;
$$;

COMMIT;
