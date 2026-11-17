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

A API salva os chamados e os eventos no PostgreSQL. As rotas funcionam no ambiente Development e aceitam chamadas locais. O acesso exige e-mail e senha. O login fornece um token Bearer com duração de uma hora. Ao expirar, faça login novamente. Cada requisição confere se a conta continua ativa e se suas credenciais continuam válidas.

As senhas são armazenadas com o PasswordHasher do ASP.NET Core, usando PBKDF2 com salt individual e 210.000 iterações. Os tokens usam a proteção de dados do ASP.NET Core. As chaves ficam no perfil local do usuário e precisam ser preservadas quando o sistema for colocado em contêineres. O login aceita até dez tentativas por minuto por IP em cada instância da API.

Antes de iniciar, execute `database/001_chamados.sql` em um banco vazio e, depois, `database/002_usuarios_teste.sql`. Se as tabelas já foram criadas, execute somente o segundo script. Em seguida, execute `database/003_autenticacao.sql`. O script 003 adiciona os campos de autenticação sem apagar usuários ou chamados. Usuários existentes precisam receber uma senha antes do primeiro login.

Na pasta do repositório, configure a conexão com o recurso user-secrets do .NET. No PowerShell:

```powershell
$senha = Read-Host "Senha do PostgreSQL" -AsSecureString
$senhaTexto = [System.Net.NetworkCredential]::new("", $senha).Password
$conexao = 'Host=localhost;Port=5432;Database=alunos_tech;Username=postgres;Password="' + $senhaTexto.Replace('"', '""') + '"'
dotnet user-secrets set "ConnectionStrings:Chamados" "$conexao" --project src/AlunosTech.Api
Remove-Variable senha, senhaTexto, conexao
```

O user-secrets salva a configuração fora da pasta do projeto, no perfil local do usuário. Ele serve para desenvolvimento e guarda os valores sem criptografia.

Com a API parada, defina duas senhas de teste, cada uma com 12 a 128 caracteres. O comando pede a senha no terminal e usa uma para os dois funcionários e outra para os dois técnicos:

```powershell
dotnet run --project src/AlunosTech.Api --no-launch-profile -- --environment Development --criar-usuarios-teste true
```

| Conta | Perfil |
|---|---|
| funcionario@example.invalid | Funcionário |
| outro.funcionario@example.invalid | Funcionário |
| tecnico@example.invalid | Técnico |
| outro.tecnico@example.invalid | Técnico |

Executar esse comando novamente troca as senhas de todas as quatro contas e invalida seus tokens anteriores.

Inicie a API:

```sh
dotnet run --project src/AlunosTech.Api --no-launch-profile -- --environment Development --urls http://127.0.0.1:5080
```

Em outro terminal PowerShell, na pasta do repositório, teste o login:

```powershell
& ./scripts/Testar-Login.ps1
```

Para testar o técnico:

```powershell
& ./scripts/Testar-Login.ps1 -Email "tecnico@example.invalid"
```

O script solicita a senha e mostra os dados da conta e os chamados acessíveis.

O endpoint `POST /api/auth/login` recebe `{"email":"...","senha":"..."}` e responde com `accessToken`, `tokenType`, `expiresIn` e `refreshToken`. Use o accessToken no cabeçalho `Authorization: Bearer <token>`. A renovação nesta versão ocorre por novo login. O endpoint `GET /api/auth/me` retorna o nome, e-mail e perfil. As rotas de chamados exigem esse mesmo cabeçalho.

| Método e rota | Uso |
|---|---|
| GET /api/chamados | Lista os chamados visíveis para o perfil. Aceita `?status=Resolvido` para consultar o histórico de resolvidos. |
| GET /api/chamados/{id} | Mostra os detalhes e a linha do tempo. |
| POST /api/chamados | Abre um chamado como funcionário. |
| POST /api/chamados/{id}/assumir | Atribui o chamado ao técnico. |
| POST /api/chamados/{id}/atualizacoes | Registra uma atualização do técnico responsável. Recebe `{"texto":"..."}`. |
| POST /api/chamados/{id}/resolver | Resolve o chamado. Recebe `{"texto":"..."}` com a solução. |

## Próximas entregas

1. Construir o aplicativo Android do funcionário usando a mesma API.
2. Implementar a consulta de histórico no desktop.
3. Adicionar os demais itens de banco, além de Docker, CI/CD, Kubernetes e monitoramento.

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

## Verificação de persistência

Abra um chamado com uma conta de funcionário. Pare a API com Ctrl+C e inicie novamente usando a mesma conexão. Entre com a mesma conta e consulte o chamado pelo ID. O chamado e o histórico devem continuar disponíveis.

A gravação do chamado e do histórico ocorre na mesma transação. Atualizações bloqueiam a linha do chamado até a confirmação, inclusive quando duas instâncias da API atendem pedidos ao mesmo tempo.

## Validação do login

- Uma senha incorreta ou uma conta sem senha retorna 401.
- Uma requisição sem token, com token adulterado ou expirado retorna 401.
- Funcionários consultam somente os próprios chamados. A tentativa de assumir um chamado retorna 403.
- Técnicos consultam a fila e assumem chamados, mas somente o responsável pode atualizar ou resolver.
- Trocar as senhas pelo comando local ou desativar a conta no banco impede o uso de tokens anteriores.
- Para expor a API fora do computador, a etapa de publicação precisa configurar HTTPS e a persistência das chaves de proteção de dados.

## Interface web

Com a API iniciada em Development, abra http://127.0.0.1:5080 no navegador.

- Entre com uma conta de funcionário para abrir chamados e consultar os próprios registros.
- Entre com uma conta de técnico para consultar a fila, assumir o atendimento, registrar atualizações e resolver chamados.
- Use a busca, o filtro de status e o histórico para encontrar os registros.
- O botão Atualizar recarrega a lista. Os dados vêm do PostgreSQL pela API.
- O botão Sair encerra a sessão no navegador. O token é mantido somente na memória da página, então atualizar ou fechar a página exige novo login.
- A interface adapta o layout a telas menores. O aplicativo Android é uma entrega separada.

Os arquivos HTML, CSS e JavaScript ficam em src/AlunosTech.Api/wwwroot e são servidos pela aplicação C#/.NET, na mesma origem da API.
