param(
    [string]$Email = "funcionario@example.invalid",
    [string]$Api = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
$senha = Read-Host "Senha da conta $Email" -AsSecureString
try {
    $senhaTexto = [System.Net.NetworkCredential]::new("", $senha).Password
    $entrada = @{ email = $Email; senha = $senhaTexto } | ConvertTo-Json
    $login = Invoke-RestMethod -Uri "$Api/api/auth/login" -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($entrada))
}
finally {
    Remove-Variable senha, senhaTexto, entrada -ErrorAction SilentlyContinue
}

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Write-Output "Conta autenticada"
Invoke-RestMethod -Uri "$Api/api/auth/me" -Headers $headers |
    Select-Object nome, email, perfil

Write-Output "Chamados acessiveis"
Invoke-RestMethod -Uri "$Api/api/chamados" -Headers $headers |
    ForEach-Object { $_ } |
    Select-Object id, titulo, status
