$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Net.Http
$cfg=Get-Content 'UDM17-Sudoku/Sudoku.Api/appsettings.json' -Raw -Encoding utf8|ConvertFrom-Json
$db=[System.Data.SqlClient.SqlConnection]::new([string]$cfg.ConnectionStrings.DefaultConnection)
$http=[System.Net.Http.HttpClient]::new();$http.Timeout=[timespan]::FromSeconds(10)
function Post-Auth([string]$path,$body){$content=[System.Net.Http.StringContent]::new(($body|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json');try{$response=$http.PostAsync("http://127.0.0.1:5243/api/auth/$path",$content).GetAwaiter().GetResult();$text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();return [pscustomobject]@{Status=[int]$response.StatusCode;Body=($text|ConvertFrom-Json)}}finally{$content.Dispose()}}
function Get-User([string]$email){$cmd=$db.CreateCommand();$cmd.CommandText='SELECT TOP 1 Id,Status,PasswordHash FROM dbo.Users WHERE Email=@email';$null=$cmd.Parameters.AddWithValue('@email',$email);$r=$cmd.ExecuteReader();try{if($r.Read()){return [pscustomobject]@{Id=$r.GetInt32(0);Status=$r.GetString(1);Hash=$r.GetString(2)}}}finally{$r.Close()}}
function Get-Otp([int]$userId,[string]$type){$cmd=$db.CreateCommand();$cmd.CommandText='SELECT TOP 1 Id,Code,IsUsed,ExpiryDate FROM dbo.OtpCodes WHERE UserId=@uid AND Type=@type ORDER BY Id DESC';$null=$cmd.Parameters.AddWithValue('@uid',$userId);$null=$cmd.Parameters.AddWithValue('@type',$type);$r=$cmd.ExecuteReader();try{if($r.Read()){return [pscustomobject]@{Id=$r.GetInt32(0);Code=$r.GetString(1);Used=$r.GetBoolean(2);Expiry=$r.GetDateTime(3)}}}finally{$r.Close()}}
function Expire-Otp([int]$id){$cmd=$db.CreateCommand();$cmd.CommandText='UPDATE dbo.OtpCodes SET ExpiryDate=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id';$null=$cmd.Parameters.AddWithValue('@id',$id);$null=$cmd.ExecuteNonQuery()}
$tag=[guid]::NewGuid().ToString('N').Substring(0,10)
$name="otpqa_$tag";$email="otpqa_$tag@example.invalid";$password='Qa!'+[guid]::NewGuid().ToString('N')+'9';$newPassword='Qa!'+[guid]::NewGuid().ToString('N')+'8'
$expiredName="otp_exp_$tag";$expiredEmail="otp_exp_$tag@example.invalid"
"RUN TC-004/005 tag=$tag at=$(Get-Date -Format o)"
try{
    $db.Open()
    $register=Post-Auth 'register' @{Username=$name;Email=$email;Password=$password}
    $userBefore=Get-User $email;$otp=Get-Otp $userBefore.Id 'Register'
    $duplicateEmail=Post-Auth 'register' @{Username="other_$tag";Email=$email;Password=$password}
    $duplicateName=Post-Auth 'register' @{Username=$name;Email="other_$tag@example.invalid";Password=$password}
    $wrong=Post-Auth 'verify-otp' @{Email=$email;Otp='000000'}
    $correct=Post-Auth 'verify-otp' @{Email=$email;Otp=$otp.Code}
    $reuse=Post-Auth 'verify-otp' @{Email=$email;Otp=$otp.Code}
    $userAfter=Get-User $email;$otpAfter=Get-Otp $userAfter.Id 'Register'
    $login=Post-Auth 'login' @{UsernameOrEmail=$name;Password=$password}
    $expiredReg=Post-Auth 'register' @{Username=$expiredName;Email=$expiredEmail;Password=$password}
    $expiredUser=Get-User $expiredEmail;$expiredOtp=Get-Otp $expiredUser.Id 'Register';Expire-Otp $expiredOtp.Id
    $expiredVerify=Post-Auth 'verify-otp' @{Email=$expiredEmail;Otp=$expiredOtp.Code}
    $expiredAfter=Get-User $expiredEmail
    [pscustomobject]@{TestCase='TC-004';RegisterSuccess=$register.Body.Success;InitialUnverified=($userBefore.Status -eq 'Unverified');HashStoredNotPlain=($userBefore.Hash -ne $password -and $userBefore.Hash.StartsWith('$2'));OtpCreated=($null -ne $otp);DuplicateEmailRejected=(!$duplicateEmail.Body.Success);DuplicateUsernameRejected=(!$duplicateName.Body.Success);WrongOtpRejected=(!$wrong.Body.Success);CorrectOtpAccepted=$correct.Body.Success;ActiveAfterCorrect=($userAfter.Status -eq 'Active');OtpUsed=$otpAfter.Used;ReusedOtpRejected=(!$reuse.Body.Success);LoginAfterVerify=($login.Status -eq 200 -and $login.Body.Success);ExpiredOtpRejected=(!$expiredVerify.Body.Success);ExpiredUserRemainsUnverified=($expiredAfter.Status -eq 'Unverified');OtpValueLogged=$false;PasswordValueLogged=$false}|ConvertTo-Json -Compress
    $forgot=Post-Auth 'forgot-password' @{Email=$email};$resetOtp=Get-Otp $userAfter.Id 'ForgotPassword'
    $reset=Post-Auth 'reset-password' @{Email=$email;Otp=$resetOtp.Code;NewPassword=$newPassword}
    $resetOtpAfter=Get-Otp $userAfter.Id 'ForgotPassword';$hashAfter=(Get-User $email).Hash
    $reuseReset=Post-Auth 'reset-password' @{Email=$email;Otp=$resetOtp.Code;NewPassword=$password}
    $loginNew=Post-Auth 'login' @{UsernameOrEmail=$name;Password=$newPassword}
    $loginOld=Post-Auth 'login' @{UsernameOrEmail=$name;Password=$password}
    [pscustomobject]@{TestCase='TC-005';ForgotSuccess=$forgot.Body.Success;ResetOtpCreated=($null -ne $resetOtp);ResetSuccess=$reset.Body.Success;HashChanged=($hashAfter -ne $userAfter.Hash);OtpUsed=$resetOtpAfter.Used;ReuseRejected=(!$reuseReset.Body.Success);NewPasswordLogin=($loginNew.Status -eq 200 -and $loginNew.Body.Success);OldPasswordRejected=($loginOld.Status -eq 401 -and !$loginOld.Body.Success);OtpValueLogged=$false;PasswordValueLogged=$false}|ConvertTo-Json -Compress
}
finally{$db.Dispose();$http.Dispose()}
"END TC-004/005 at=$(Get-Date -Format o)"
