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

## API local com PostgreSQL

A API salva os chamados e os eventos no PostgreSQL. As rotas só funcionam no ambiente Development e aceitam chamadas locais com perfis fictícios. O login será implementado em uma próxima etapa.

Antes de iniciar, execute `database/001_chamados.sql` em um banco vazio e, depois, `database/002_usuarios_teste.sql`. Se as tabelas já foram criadas, execute somente o segundo script.

Na pasta do repositório, configure a conexão com o recurso user-secrets do .NET. No PowerShell:

```powershell
$senha = Read-Host "Senha do PostgreSQL" -AsSecureString
$senhaTexto = [System.Net.NetworkCredential]::new("", $senha).Password
$conexao = 'Host=localhost;Port=5432;Database=alunos_tech;Username=postgres;Password="' + $senhaTexto.Replace('"', '""') + '"'
dotnet user-secrets set "ConnectionStrings:Chamados" "$conexao" --project src/AlunosTech.Api
Remove-Variable senha, senhaTexto, conexao
```

O user-secrets salva a configuração fora da pasta do projeto, no perfil local do usuário. Ele serve para desenvolvimento e guarda os valores sem criptografia.

Inicie a API:

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

1. Implementar login e autenticação com permissões por perfil.
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

A API usa essas tabelas para persistência. Para testar localmente, execute também o script de usuários fictícios indicado acima.

## Escopo da entrega

- API em C#/.NET e banco PostgreSQL.
- Aplicativo Android para o funcionário, desenvolvido com Android Studio.
- Interface web para o técnico e módulo desktop de consulta.
- Orçamento de hospedagem de R$ 0.
- Prazo informado de 20/11/2026.

## Teste manual de persistência no PowerShell

Com a API em execução, abra outro terminal na pasta do projeto:

```powershell
$entrada = @{
    titulo = "Sem acesso à rede"
    categoria = "Rede"
    descricao = "O computador não conecta ao Wi-Fi."
} | ConvertTo-Json
$chamado = Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/chamados" -Method Post -ContentType "application/json; charset=utf-8" -Headers @{"X-Usuario-Teste" = "funcionario"} -Body ([System.Text.Encoding]::UTF8.GetBytes($entrada))
$chamado
```

Pare a API com Ctrl+C no primeiro terminal e inicie novamente com o mesmo comando.
No segundo terminal, mantendo a variável `$chamado`, consulte:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/chamados/$($chamado.id)" -Headers @{"X-Usuario-Teste" = "funcionario"}
```

O resultado deve manter o mesmo ID e o evento de abertura. A gravação do chamado e do histórico ocorre na mesma transação. Atualizações bloqueiam a linha do chamado até a confirmação, inclusive quando duas instâncias da API atendem pedidos ao mesmo tempo.
