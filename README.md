# Alunos Tech | Chamados internos de TI

Projeto do PIM VIII. O funcionário abre e acompanha os próprios chamados. O técnico consulta a fila, assume um chamado, registra o atendimento e informa a solução.

## Fluxo

Aberto → Em atendimento → Resolvido.

O histórico de cada chamado registra a abertura, o início do atendimento, as atualizações e a resolução. A consulta de chamados resolvidos será uma tela separada para o técnico.

## Primeira entrega

- Biblioteca C# com as regras de atendimento e registro de histórico.
- Script inicial para PostgreSQL, com as tabelas e a procedure de atribuição do chamado. A procedure altera o status e grava o evento na mesma transação. Se dois técnicos tentarem assumir o mesmo chamado, o segundo não consegue alterar a linha depois que o primeiro confirmar.
- Verificação executável das regras do chamado, usando o SDK .NET 10.

```sh
dotnet run --project checks/Chamados.Checks.csproj
```

## API local para testar o fluxo

A API de demonstração usa armazenamento em memória e perfis de teste. As rotas de chamados só funcionam no ambiente Development e aceitam chamadas locais. Reiniciar a API limpa os dados.

```sh
dotnet run --project src/AlunosTech.Api --no-launch-profile -- --environment Development --urls http://127.0.0.1:5080
```

Envie o cabeçalho `X-Usuario-Teste` com `funcionario`, `outro-funcionario`, `tecnico` ou `outro-tecnico`. Por exemplo:

```sh
curl http://127.0.0.1:5080/api/chamados -H 'X-Usuario-Teste: funcionario' \
  -H 'Content-Type: application/json' \
  -d '{"titulo":"Sem acesso","categoria":"Rede","descricao":"O Wi-Fi não conecta."}'
```

| Método e rota | Uso |
|---|---|
| GET /api/chamados | Lista os chamados visíveis para o perfil. Aceita `?status=Resolvido` para consultar o histórico de resolvidos. |
| GET /api/chamados/{id} | Mostra os detalhes e a linha do tempo. |
| POST /api/chamados | Abre um chamado como funcionário. |
| POST /api/chamados/{id}/assumir | Atribui o chamado ao técnico. |
| POST /api/chamados/{id}/atualizacoes | Registra uma atualização do técnico responsável. Recebe `{"texto":"..."}`. |
| POST /api/chamados/{id}/resolver | Resolve o chamado. Recebe `{"texto":"..."}` com a solução. |

## Próximas entregas

1. Ligar a API ao PostgreSQL e implementar autenticação e permissões por perfil.
2. Construir a área do funcionário no mobile e a área do técnico na web.
3. Implementar a consulta de histórico no desktop.
4. Adicionar os demais itens de banco, além de Docker, CI/CD, Kubernetes e monitoramento.

## Banco local no VS Code

Instale e inicie o servidor PostgreSQL. A extensão do VS Code é usada para conectar e executar SQL.
Crie um banco vazio chamado `alunos_tech`, conecte a extensão a ele e execute
`database/001_chamados.sql` uma vez. O script cria tabelas, índices e a procedure
`AssumirChamado`. Uma falha na gravação do evento também desfaz a atribuição do chamado.

Exemplo de parâmetros locais para a conexão da extensão:

- Host `localhost`
- Porta `5432`
- Banco `alunos_tech`
- Usuário definido na instalação do PostgreSQL
- Senha definida na instalação, mantida no computador

A API ainda usa dados em memória. A conexão persistente com PostgreSQL será implementada na próxima etapa.

## Escopo da entrega

- API em C#/.NET e banco PostgreSQL.
- Aplicativo Android para o funcionário, desenvolvido com Android Studio.
- Interface web para o técnico e módulo desktop de consulta.
- Orçamento de hospedagem de R$ 0.
- Prazo informado de 20/11/2026.
