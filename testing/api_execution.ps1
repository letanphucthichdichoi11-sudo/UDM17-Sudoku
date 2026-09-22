$ErrorActionPreference='Stop'
$base='http://127.0.0.1:5243/api/auth'
function Invoke-ActualPost([string]$path,$body){
  try {
    $json=if($null -eq $body){'null'}else{$body|ConvertTo-Json -Compress}
    $r=Invoke-WebRequest -Uri "$base/$path" -Method Post -ContentType 'application/json' -Body $json -UseBasicParsing
    [pscustomobject]@{Status=[int]$r.StatusCode;Body=($r.Content|ConvertFrom-Json)}
  } catch [System.Net.WebException] {
    $resp=$_.Exception.Response; $reader=[IO.StreamReader]::new($resp.GetResponseStream()); $text=$reader.ReadToEnd(); $reader.Dispose()
    $parsed=$null; try{$parsed=$text|ConvertFrom-Json}catch{$parsed=$text}
    [pscustomobject]@{Status=[int]$resp.StatusCode;Body=$parsed}
  }
}
$valid=Invoke-ActualPost 'login' @{UsernameOrEmail='test_player_a';Password='SudokuTest!2026'}
"[TC-001] VALID_LOGIN="+(@{Status=$valid.Status;Success=$valid.Body.Success;AccessTokenIssued=![string]::IsNullOrWhiteSpace($valid.Body.AccessToken);RefreshTokenIssued=![string]::IsNullOrWhiteSpace($valid.Body.RefreshToken)}|ConvertTo-Json -Compress)
$wrong=Invoke-ActualPost 'login' @{UsernameOrEmail='test_player_a';Password='wrong-password'}
"[TC-002] WRONG_PASSWORD="+(@{Status=$wrong.Status;Success=$wrong.Body.Success;Message=$wrong.Body.Message}|ConvertTo-Json -Compress)
$missing=Invoke-ActualPost 'login' @{UsernameOrEmail='';Password=''}
"[TC-002] EMPTY_LOGIN="+(@{Status=$missing.Status;Success=$missing.Body.Success;Message=$missing.Body.Message}|ConvertTo-Json -Compress)
$nullBody=Invoke-ActualPost 'login' $null
"[TC-088] NULL_LOGIN_BODY="+(@{Status=$nullBody.Status;Body=$nullBody.Body}|ConvertTo-Json -Compress -Depth 8)
$dupEmail=Invoke-ActualPost 'register' @{Username='execution-new-name';Email='test_player_a@sudoku.test';Password='SudokuTest!2026'}
"[TC-005] DUPLICATE_EMAIL="+(@{Status=$dupEmail.Status;Success=$dupEmail.Body.Success;Message=$dupEmail.Body.Message}|ConvertTo-Json -Compress)
$dupUser=Invoke-ActualPost 'register' @{Username='test_player_a';Email='execution-unique@sudoku.test';Password='SudokuTest!2026'}
"[TC-005] DUPLICATE_USERNAME="+(@{Status=$dupUser.Status;Success=$dupUser.Body.Success;Message=$dupUser.Body.Message}|ConvertTo-Json -Compress)
