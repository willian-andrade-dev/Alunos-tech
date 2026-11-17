-- Execute no banco alunos_tech depois dos scripts 001 e 002.
-- Mantém os chamados e usuários que já existem.
BEGIN;
ALTER TABLE public.Usuario ADD COLUMN IF NOT EXISTS SenhaHash text;
ALTER TABLE public.Usuario ADD COLUMN IF NOT EXISTS Ativo boolean NOT NULL DEFAULT true;
ALTER TABLE public.Usuario ADD COLUMN IF NOT EXISTS VersaoCredencial uuid NOT NULL DEFAULT gen_random_uuid();
CREATE UNIQUE INDEX IF NOT EXISTS IX_Usuario_EmailNormalizado
    ON public.Usuario (lower(Email));
COMMIT;
