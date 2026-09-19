$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
Add-Type -AssemblyName System.Net.Http

$attachment = 'C:\Users\ngong\.codex\attachments\c4399575-7f9d-4283-a3ce-bc569f1e6a35\pasted-text.txt'
$raw = Get-Content $attachment -Raw -Encoding utf8
$testPassword = [regex]::Match($raw, 'Password:\s*\r?\n([^\r\n]+)').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($testPassword)) { throw 'Test password missing from user attachment' }

$config = Get-Content 'UDM17-Sudoku/Sudoku.Api/appsettings.json' -Raw -Encoding utf8 | ConvertFrom-Json
$connection = [System.Data.SqlClient.SqlConnection]::new([string]$config.ConnectionStrings.DefaultConnection)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [timespan]::FromSeconds(10)

function Get-SessionCount($connection, [string]$username) {
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT COUNT(*) FROM dbo.UserSessions s INNER JOIN dbo.Users u ON s.UserId=u.Id WHERE u.Username=@username'
    $null = $command.Parameters.AddWithValue('@username', $username)
    return [int]$command.ExecuteScalar()
}

"RUN TC-006 HTTP-session at=$(Get-Date -Format o)"
try {
    $connection.Open()
    foreach ($username in @('test_player_a', 'test_player_b')) {
        $before = Get-SessionCount $connection $username
        $responses = @()
        for ($i = 0; $i -lt 2; $i++) {
            $body = @{ UsernameOrEmail = $username; Password = $testPassword } | ConvertTo-Json -Compress
            $content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
            try {
                $response = $client.PostAsync('http://127.0.0.1:5243/api/auth/login', $content).GetAwaiter().GetResult()
                $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                $responses += ,[pscustomobject]@{ Status = [int]$response.StatusCode; Success = $payload.Success; AccessToken = $payload.AccessToken; RefreshToken = $payload.RefreshToken }
                $response.Dispose()
            }
            finally { $content.Dispose() }
        }
        $after = Get-SessionCount $connection $username
        [pscustomobject]@{
            TestCase = 'TC-006'
            Account = $username
            HttpStatuses = @($responses | ForEach-Object { $_.Status })
            BothSucceeded = (@($responses | Where-Object { $_.Success }).Count -eq 2)
            DistinctAccessTokens = ($responses[0].AccessToken -ne $responses[1].AccessToken)
            DistinctRefreshTokens = ($responses[0].RefreshToken -ne $responses[1].RefreshToken)
            SessionCountBefore = $before
            SessionCountAfter = $after
            SessionCountDelta = $after - $before
            SecretsLogged = $false
        } | ConvertTo-Json -Compress -Depth 5
    }
}
finally {
    $client.Dispose()
    $connection.Dispose()
}
"END TC-006 HTTP-session at=$(Get-Date -Format o)"
