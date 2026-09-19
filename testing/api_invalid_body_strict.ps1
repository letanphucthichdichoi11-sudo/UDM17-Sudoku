$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [timespan]::FromSeconds(10)
$base = 'http://127.0.0.1:5243/api/auth'
$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
"RUN TC-086 at=$(Get-Date -Format o)"
try {
    foreach ($path in @('login','register','verify-otp','forgot-password','reset-password')) {
        $variants = @(
            @{ Name='null'; Body='null' },
            @{ Name='empty'; Body='{}' }
        )
        switch ($path) {
            'login' { $variants += @{ Name='missing-password'; Body='{"UsernameOrEmail":"nobody"}' }; $variants += @{ Name='long-username'; Body=(@{UsernameOrEmail=('a'*300);Password='not-real'}|ConvertTo-Json -Compress) } }
            'register' { $variants += @{ Name='missing-username'; Body='{"Email":"nobody@example.invalid","Password":"not-real"}' }; $variants += @{ Name='long-username'; Body=(@{Username=('b'*101);Email="strict-$tag@example.invalid";Password='SudokuBoundary!2026'}|ConvertTo-Json -Compress) } }
            'verify-otp' { $variants += @{ Name='missing-otp'; Body='{"Email":"nobody@example.invalid"}' }; $variants += @{ Name='long-email'; Body=(@{Email=('c'*300);Otp='000000'}|ConvertTo-Json -Compress) } }
            'forgot-password' { $variants += @{ Name='missing-email'; Body='{}' }; $variants += @{ Name='long-email'; Body=(@{Email=('d'*300)}|ConvertTo-Json -Compress) } }
            'reset-password' { $variants += @{ Name='missing-password'; Body='{"Email":"nobody@example.invalid","Otp":"000000"}' }; $variants += @{ Name='long-email'; Body=(@{Email=('e'*300);Otp='000000';NewPassword='not-real'}|ConvertTo-Json -Compress) } }
        }
        foreach ($item in $variants) {
            try {
                $content = [System.Net.Http.StringContent]::new($item.Body, [Text.Encoding]::UTF8, 'application/json')
                $response = $client.PostAsync("$base/$path", $content).GetAwaiter().GetResult()
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                [pscustomobject]@{ TestCase='TC-086'; Path=$path; Variant=$item.Name; Status=[int]$response.StatusCode; Body=$body.Substring(0,[Math]::Min(350,$body.Length)) } | ConvertTo-Json -Compress -Depth 3
                $content.Dispose(); $response.Dispose()
            }
            catch {
                [pscustomobject]@{ TestCase='TC-086'; Path=$path; Variant=$item.Name; Exception=$_.Exception.GetType().Name; Message=$_.Exception.Message } | ConvertTo-Json -Compress
            }
        }
    }
}
finally { $client.Dispose() }
"END TC-086 at=$(Get-Date -Format o)"
