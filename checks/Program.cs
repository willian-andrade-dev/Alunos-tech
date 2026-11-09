using AlunosTech.Chamados;

var funcionario = Guid.NewGuid();
var tecnico = Guid.NewGuid();
var outroTecnico = Guid.NewGuid();
var agora = DateTimeOffset.UtcNow;
var chamado = new Chamado(funcionario, " Sem acesso ", "Rede", "O Wi-Fi não conecta.", agora);

Verificar(chamado.Status == StatusChamado.Aberto && chamado.Titulo == "Sem acesso");
DeveFalhar<InvalidOperationException>(() => chamado.Resolver(tecnico, "Resolvido", agora));

chamado.Assumir(tecnico, agora);
Verificar(chamado.TecnicoId == tecnico && chamado.Status == StatusChamado.EmAtendimento);
DeveFalhar<InvalidOperationException>(() => chamado.Assumir(outroTecnico, agora));
DeveFalhar<InvalidOperationException>(() => chamado.RegistrarAtualizacao(outroTecnico, "Teste", agora));
DeveFalhar<ArgumentException>(() => chamado.Resolver(tecnico, " ", agora));
Verificar(chamado.Status == StatusChamado.EmAtendimento && chamado.Historico.Count == 2);

chamado.RegistrarAtualizacao(tecnico, "Verificado o ponto de acesso.", agora.AddMinutes(1));
chamado.Resolver(tecnico, "Ponto de acesso reiniciado.", agora.AddMinutes(2));
Verificar(chamado.Status == StatusChamado.Resolvido);
Verificar(chamado.Historico.Select(e => e.Tipo).SequenceEqual(
    new[] { TipoEvento.Abertura, TipoEvento.Atendimento, TipoEvento.Atualizacao, TipoEvento.Resolucao }));
DeveFalhar<InvalidOperationException>(() => chamado.Resolver(tecnico, "Outra solução", agora));

Console.WriteLine("Verificações do fluxo de chamados passaram.");

static void Verificar(bool condicao)
{
    if (!condicao)
        throw new Exception("O resultado do fluxo divergiu do esperado.");
}

static void DeveFalhar<T>(Action acao) where T : Exception
{
    try { acao(); }
    catch (T) { return; }
    throw new Exception($"Era esperada uma exceção do tipo {typeof(T).Name}.");
}
