$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Net.Http
$cfg=Get-Content 'UDM17-Sudoku/Sudoku.Api/appsettings.json' -Raw -Encoding utf8|ConvertFrom-Json
$db=[Data.SqlClient.SqlConnection]::new([string]$cfg.ConnectionStrings.DefaultConnection)
$http=[Net.Http.HttpClient]::new()
function Sessions($db){$cmd=$db.CreateCommand();$cmd.CommandText='SELECT COUNT(*) FROM dbo.UserSessions';return [int]$cmd.ExecuteScalar()}
function Probe($http,[string]$name,[string]$body){$content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json');try{$r=$http.PostAsync('http://127.0.0.1:5243/api/auth/login',$content).GetAwaiter().GetResult();$raw=$r.Content.ReadAsStringAsync().GetAwaiter().GetResult();$obj=$raw|ConvertFrom-Json;return [pscustomobject]@{Variant=$name;HttpStatus=[int]$r.StatusCode;Success=$obj.Success;TokenIssued=(![string]::IsNullOrWhiteSpace($obj.AccessToken));GenericCredentialError=($obj.Message -match 'tài khoản hoặc mật khẩu')}}finally{$content.Dispose()}}
try{
    $db.Open();$before=Sessions $db
    $variants=@(
        @{Name='wrong-password';Body=(@{UsernameOrEmail='test_player_a';Password='invalid-qa-only'}|ConvertTo-Json -Compress)},
        @{Name='unknown-user';Body=(@{UsernameOrEmail='nonexistent-udm17-qa';Password='invalid-qa-only'}|ConvertTo-Json -Compress)},
        @{Name='unverified';Body=(@{UsernameOrEmail='test_qa_61420';Password='invalid-qa-only'}|ConvertTo-Json -Compress)},
        @{Name='empty-credentials';Body='{"UsernameOrEmail":"","Password":""}'},
        @{Name='missing-credentials';Body='{}'}
    )
    foreach($v in $variants){$result=Probe $http $v.Name $v.Body;$result|ConvertTo-Json -Compress}
    $after=Sessions $db
    [pscustomobject]@{TestCase='TC-002';Step='session-audit';SessionsBefore=$before;SessionsAfter=$after;NoSessionCreated=($before -eq $after);Timestamp=(Get-Date -Format o)}|ConvertTo-Json -Compress
}finally{$http.Dispose();$db.Dispose()}
