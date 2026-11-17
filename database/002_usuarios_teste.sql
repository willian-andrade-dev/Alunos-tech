-- Contas fictícias usadas pela API local de desenvolvimento.
-- Pode executar novamente sem substituir usuários existentes.
BEGIN;
INSERT INTO public.Usuario (Id, Nome, Email, Perfil)
VALUES
    ('00000000-0000-0000-0000-000000000001', 'Funcionário de teste', 'funcionario@example.invalid', 0),
    ('00000000-0000-0000-0000-000000000002', 'Outro funcionário', 'outro.funcionario@example.invalid', 0),
    ('00000000-0000-0000-0000-000000000003', 'Técnico de teste', 'tecnico@example.invalid', 1),
    ('00000000-0000-0000-0000-000000000004', 'Outro técnico', 'outro.tecnico@example.invalid', 1)
ON CONFLICT (Id) DO NOTHING;
COMMIT;
