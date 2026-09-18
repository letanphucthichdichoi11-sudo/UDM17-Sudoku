# UDM17 Sudoku

UDM17 Sudoku là ứng dụng Sudoku đối kháng 1v1. Người chơi đăng nhập qua HTTP API, vào Lobby, tạo hoặc tham gia phòng và chơi qua TCP Game Server. Server quản lý phòng, đề Sudoku, nước đi, thời gian và kết quả trận đấu.

## Trạng thái hiện tại

- Login hoạt động đầy đủ từ `LoginPage` tới `LobbyPage`.
- Create Room hỗ trợ `Easy`, `Medium`, `Hard`.
- Mỗi trận có hai đề Sudoku khác nhau nhưng cùng độ khó.
- Mỗi bảng là Sudoku `9 × 9`, gồm 81 ô và chín vùng `3 × 3`.
- Bảng của bản thân cho phép nhập/xóa ở ô không phải đề bài; bảng đối thủ chỉ đọc.
- Server xác thực nước đi, đồng bộ tiến độ và quyết định người thắng.
- Có màn hình Victory/Defeat, lưu kết quả và quay lại Lobby.
- Bộ kiểm thử hiện tại: **60/60 test PASS**.

## Thành phần dự án

| Project | Chức năng | Framework |
|---|---|---|
| `Sudoku.Api` | Đăng ký, đăng nhập, JWT và tài khoản | ASP.NET Core, .NET 10 |
| `Sudoku.Mobile` | Giao diện client | .NET MAUI, .NET 10 |
| `Sudoku.Server` | TCP server, phòng và trận đấu | Windows Forms, .NET Framework 4.7.2 |
| `Sudoku.Shared` | Model và giao thức dùng chung | .NET Framework 4.7.2 |
| `Sudoku.Server.Tests` | Automated tests | MSTest, .NET Framework 4.7.2 |

```text
Login → Lobby → Create Room / Quick Match → Sudoku Duel → Victory / Defeat
```

| Dịch vụ | Địa chỉ mặc định |
|---|---|
| Authentication API | `http://127.0.0.1:5243` |
| TCP Game Server | `127.0.0.1:5000` |

## Yêu cầu môi trường

- Windows 10 hoặc Windows 11.
- Git, .NET SDK 10 và Visual Studio 2022/Build Tools tương thích.
- Workload `.NET Multi-platform App UI development`.
- .NET Framework 4.7.2 Developer Pack.
- SQL Server LocalDB, hoặc SQL Server được cấu hình trong `Sudoku.Api/appsettings.json`.
- Android SDK và emulator nếu chạy Android.

Kiểm tra môi trường:

```powershell
dotnet --info
dotnet workload list
```

Nếu chưa có MAUI workload:

```powershell
dotnet workload install maui
```

## Clone và restore

```powershell
git clone https://github.com/letanphucthichdichoi11-sudo/UDM17-Sudoku.git
Set-Location .\UDM17-Sudoku
dotnet restore .\UDM17-Sudoku.slnx
```

Các lệnh dưới đây được chạy từ thư mục chứa `UDM17-Sudoku.slnx`.

## Build

```powershell
# Authentication API
dotnet build .\Sudoku.Api\Sudoku.Api.csproj --no-restore

# TCP Game Server
dotnet build .\Sudoku.Server\Sudoku.Server.csproj --no-restore

# Windows client
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-windows10.0.19041.0 `
    --no-restore

# Android client
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-android `
    --no-restore
```

Build Release:

```powershell
dotnet build .\Sudoku.Api\Sudoku.Api.csproj -c Release
dotnet build .\Sudoku.Server\Sudoku.Server.csproj -c Release
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-windows10.0.19041.0 `
    -c Release
```

Nếu Mobile báo `Sudoku.Mobile.exe` đang bị khóa, đóng các cửa sổ client rồi build lại.

## Chạy đầy đủ trên Windows

Cần chạy đồng thời API, TCP Game Server và Mobile client.

### 1. Authentication API

Mở PowerShell thứ nhất:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\Sudoku.Api\Sudoku.Api.csproj --launch-profile http
```

Kết quả mong đợi: `Now listening on: http://localhost:5243`.

API tự áp dụng migration khi khởi động. Trong `Development`, API tạo hai tài khoản test nếu chưa tồn tại:

| Player | Username | Password |
|---|---|---|
| Player A | `test_player_a` | `SudokuTest!2026` |
| Player B | `test_player_b` | `SudokuTest!2026` |

### 2. TCP Game Server

Mở PowerShell thứ hai:

```powershell
dotnet run --project .\Sudoku.Server\Sudoku.Server.csproj
```

Server mặc định lắng nghe tại `0.0.0.0:5000`.

Kiểm tra cả hai dịch vụ:

```powershell
Get-NetTCPConnection -State Listen |
    Where-Object { $_.LocalPort -in 5000, 5243 } |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

### 3. Một Windows client

```powershell
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-windows10.0.19041.0

& .\Sudoku.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sudoku.Mobile.exe
```

## Chạy độc lập Player A và Player B

Build Mobile một lần:

```powershell
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-windows10.0.19041.0

$mobileExe = Resolve-Path `
    ".\Sudoku.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sudoku.Mobile.exe"
```

Mở hai client và tự đăng nhập bằng tham số Debug:

```powershell
Start-Process -FilePath $mobileExe -ArgumentList "--dev-player=test_player_a"
Start-Process -FilePath $mobileExe -ArgumentList "--dev-player=test_player_b"
```

Cho hai client tự đăng nhập và vào Quick Match:

```powershell
Start-Process -FilePath $mobileExe `
    -ArgumentList "--dev-player=test_player_a", "--dev-quick-match"

Start-Process -FilePath $mobileExe `
    -ArgumentList "--dev-player=test_player_b", "--dev-quick-match"
```

Các tham số `--dev-*` chỉ hoạt động trong Debug build, không hoạt động trong Release/production.

Nếu test thủ công:

1. Player A đăng nhập và tạo phòng hoặc bấm Quick Match.
2. Player B đăng nhập và tham gia phòng hoặc bấm Quick Match.
3. Khi cả hai sẵn sàng, server bắt đầu trận.
4. Mỗi client hiển thị bảng của mình và bảng tiến độ đối thủ.

## Chạy automated tests

```powershell
dotnet test .\Sudoku.Server.Tests\Sudoku.Server.Tests.csproj `
    --no-restore `
    --verbosity:quiet
```

Kết quả xác minh gần nhất:

```text
Passed: 60
Failed: 0
Skipped: 0
```

Chạy riêng nhóm board/multiplayer:

```powershell
dotnet test .\Sudoku.Server.Tests\Sudoku.Server.Tests.csproj `
    --filter "SudokuBoardCoordinatesTests|MatchCoordinatorTests|NetworkProtocolTests"
```

## Kiểm tra nhanh Login API

Sau khi API chạy:

```powershell
$loginBody = @{
    UsernameOrEmail = "test_player_a"
    Password = "SudokuTest!2026"
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://127.0.0.1:5243/api/auth/login" `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginBody
```

Kết quả đúng:

- Hợp lệ: HTTP `200` và có token.
- Sai mật khẩu: HTTP `401`.
- Tài khoản chưa kích hoạt: HTTP `403`.

Không ghi access token, refresh token hoặc thông tin production vào log/ảnh chụp.

## Chạy Android Emulator

Android Emulator dùng `10.0.2.2` để truy cập máy host:

```text
Authentication API: http://10.0.2.2:5243
TCP Game Server:     10.0.2.2:5000
```

```powershell
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj -f net10.0-android
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj -f net10.0-android -t:Run
```

Với thiết bị thật hoặc server ở máy khác:

```powershell
$env:SUDOKU_API_URL = "http://192.168.1.10:5243/"
$env:SUDOKU_GAME_HOST = "192.168.1.10"
$env:SUDOKU_GAME_PORT = "5000"
```

Thay IP bằng LAN IP của backend và cho phép port `5243`, `5000` qua firewall.

## Biến môi trường

| Biến | Ví dụ | Ý nghĩa |
|---|---|---|
| `SUDOKU_API_URL` | `http://127.0.0.1:5243/` | URL Authentication API |
| `SUDOKU_GAME_HOST` | `127.0.0.1` | Host TCP Game Server |
| `SUDOKU_GAME_PORT` | `5000` | Port TCP Game Server |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Môi trường chạy API |

## Xử lý lỗi thường gặp

### Login không kết nối được API

```powershell
Test-NetConnection 127.0.0.1 -Port 5243
```

Windows development dùng `http://127.0.0.1:5243`. MAUI dùng native `HttpClient`, nên CORS không phải nguyên nhân thông thường.

### Login thành công nhưng không vào Lobby

Login còn cần TCP Game Server:

```powershell
Test-NetConnection 127.0.0.1 -Port 5000
```

### Database không kết nối được

- Kiểm tra SQL Server LocalDB đã cài và đang chạy.
- Kiểm tra `ConnectionStrings:DefaultConnection`.
- Không commit secret hoặc cấu hình production.

### Quick Match không tìm thấy đối thủ

- Dùng hai username khác nhau.
- Hai client phải dùng cùng TCP host/port.
- Đảm bảo cả hai TCP handshake thành công.
- Client thứ hai phải tham gia trước khi client đầu tiên hết thời gian chờ.

## Quy ước Sudoku Duel

- Mỗi board có 9 hàng, 9 cột, 81 ô và chín vùng `3 × 3`.
- Flat index: `index = row * 9 + column`.
- Ô đề bài không được chỉnh sửa; ô người chơi nhập có thể sửa/xóa.
- Bảng đối thủ chỉ đọc.
- Hai player có cùng `MatchId`, cùng độ khó nhưng nhận hai puzzle khác nhau.
- Kết quả thắng/thua do server xác định.

## Kiểm tra trước khi commit

```powershell
git status --short
dotnet build .\Sudoku.Api\Sudoku.Api.csproj --no-restore
dotnet build .\Sudoku.Server\Sudoku.Server.csproj --no-restore
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj `
    -f net10.0-windows10.0.19041.0 `
    --no-restore
dotnet test .\Sudoku.Server.Tests\Sudoku.Server.Tests.csproj --no-restore
```

Stage source code nhưng loại trừ Markdown:

```powershell
git add -- Sudoku.Api Sudoku.Mobile Sudoku.Server Sudoku.Server.Tests Sudoku.Shared `
    ':(exclude)**/*.md'
```

Không commit `bin/`, `obj/`, `TestResults/`, log, screenshot, folder thiết kế/reference, secret hoặc token. Lịch sử bug và retest được lưu cục bộ trong `testcase.md`.
