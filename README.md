# Alunos Tech | Chamados internos de TI

Projeto do PIM VIII. O funcionário abre e acompanha os próprios chamados. O técnico consulta a fila, assume um chamado, registra o atendimento e informa a solução.

## Fluxo

Aberto → Em atendimento → Resolvido.

O histórico de cada chamado registra a abertura, o início do atendimento, as atualizações e a resolução. A consulta de chamados resolvidos será uma tela separada para o técnico.

## Primeira entrega

- Biblioteca C# com as regras de atendimento e registro de histórico.
- Script inicial para SQL Server, com as tabelas e a procedure de atribuição do chamado. A procedure altera o status e grava o evento na mesma transação. Se dois técnicos tentarem assumir o mesmo chamado, o segundo não consegue alterar a linha depois que o primeiro confirmar.
- Verificação executável das regras do chamado, usando o SDK .NET 10.

```sh
dotnet run --project checks/Chamados.Checks.csproj
```

## Próximas entregas

1. Ligar a API ao SQL Server e implementar autenticação e permissões por perfil.
2. Construir a área do funcionário no mobile e a área do técnico na web.
3. Implementar a consulta de histórico no desktop.
4. Adicionar os demais itens de banco, além de Docker, CI/CD, Kubernetes e monitoramento.
