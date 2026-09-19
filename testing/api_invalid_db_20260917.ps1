$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
Add-Type -AssemblyName System.Net.Http

$config = Get-Content 'UDM17-Sudoku/Sudoku.Api/appsettings.json' -Raw -Encoding utf8 | ConvertFrom-Json
$connection = [System.Data.SqlClient.SqlConnection]::new([string]$config.ConnectionStrings.DefaultConnection)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [timespan]::FromSeconds(10)

function Get-DatabaseCounts($connection) {
    $counts = [ordered]@{}
    foreach ($table in @('Users', 'UserSessions', 'OtpCodes')) {
        $command = $connection.CreateCommand()
        $command.CommandText = "SELECT COUNT(*) FROM dbo.[$table]"
        $counts[$table] = [int]$command.ExecuteScalar()
    }
    return [pscustomobject]$counts
}

"RUN TC-086 DB-validated-invalid-bodies at=$(Get-Date -Format o)"
try {
    $connection.Open()
    $before = Get-DatabaseCounts $connection
    $variants = @(
        @{ Path = 'login'; Name = 'null'; Body = 'null' },
        @{ Path = 'login'; Name = 'empty'; Body = '{}' },
        @{ Path = 'login'; Name = 'missing-password'; Body = '{"UsernameOrEmail":"nobody"}' },
        @{ Path = 'login'; Name = 'long-username'; Body = (@{ UsernameOrEmail = ('a' * 300); Password = 'not-real' } | ConvertTo-Json -Compress) },
        @{ Path = 'register'; Name = 'null'; Body = 'null' },
        @{ Path = 'register'; Name = 'empty'; Body = '{}' },
        @{ Path = 'register'; Name = 'missing-username'; Body = '{"Email":"nobody@example.invalid","Password":"not-real"}' },
        @{ Path = 'verify-otp'; Name = 'null'; Body = 'null' },
        @{ Path = 'verify-otp'; Name = 'empty'; Body = '{}' },
        @{ Path = 'verify-otp'; Name = 'missing-otp'; Body = '{"Email":"nobody@example.invalid"}' },
        @{ Path = 'verify-otp'; Name = 'long-email'; Body = (@{ Email = ('c' * 300); Otp = '000000' } | ConvertTo-Json -Compress) },
        @{ Path = 'forgot-password'; Name = 'null'; Body = 'null' },
        @{ Path = 'forgot-password'; Name = 'empty'; Body = '{}' },
        @{ Path = 'forgot-password'; Name = 'missing-email'; Body = '{}' },
        @{ Path = 'forgot-password'; Name = 'long-email'; Body = (@{ Email = ('d' * 300) } | ConvertTo-Json -Compress) },
        @{ Path = 'reset-password'; Name = 'null'; Body = 'null' },
        @{ Path = 'reset-password'; Name = 'empty'; Body = '{}' },
        @{ Path = 'reset-password'; Name = 'missing-password'; Body = '{"Email":"nobody@example.invalid","Otp":"000000"}' },
        @{ Path = 'reset-password'; Name = 'long-email'; Body = (@{ Email = ('e' * 300); Otp = '000000'; NewPassword = 'not-real' } | ConvertTo-Json -Compress) }
    )
    $serverErrors = 0
    $secretLeaks = 0
    foreach ($variant in $variants) {
        $content = [System.Net.Http.StringContent]::new($variant.Body, [Text.Encoding]::UTF8, 'application/json')
        try {
            $response = $client.PostAsync("http://127.0.0.1:5243/api/auth/$($variant.Path)", $content).GetAwaiter().GetResult()
            $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $status = [int]$response.StatusCode
            $parsed = $null
            try { $parsed = $responseBody | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
            $leak = ($responseBody -match '(?i)passwordhash|sessiontoken|stacktrace|system\.exception') -or
                ($parsed -and (![string]::IsNullOrWhiteSpace([string]$parsed.AccessToken) -or ![string]::IsNullOrWhiteSpace([string]$parsed.RefreshToken)))
            if ($status -ge 500) { $serverErrors++ }
            if ($leak) { $secretLeaks++ }
            [pscustomobject]@{ TestCase = 'TC-086'; Path = $variant.Path; Variant = $variant.Name; Status = $status; ApiSuccess = if ($parsed) { $parsed.Success } else { $null }; SecretMarkerLeak = $leak } | ConvertTo-Json -Compress
            $response.Dispose()
        }
        finally { $content.Dispose() }
    }
    $after = Get-DatabaseCounts $connection
    [pscustomobject]@{ TestCase = 'TC-086'; Step = 'database-audit'; Before = $before; After = $after; CountsUnchanged = ($before.Users -eq $after.Users -and $before.UserSessions -eq $after.UserSessions -and $before.OtpCodes -eq $after.OtpCodes); ServerErrors = $serverErrors; SecretMarkerLeaks = $secretLeaks; Requests = $variants.Count } | ConvertTo-Json -Compress -Depth 6
}
finally {
    $client.Dispose()
    $connection.Dispose()
}
"END TC-086 at=$(Get-Date -Format o)"
