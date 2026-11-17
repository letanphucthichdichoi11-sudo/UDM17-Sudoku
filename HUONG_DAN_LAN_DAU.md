# Hướng dẫn lần đầu chạy Sudoku sau khi lấy source

Tài liệu dành cho người vừa clone/pull dự án về máy và chưa biết cách chạy. Bạn sẽ cài công cụ, tạo database cục bộ, build ứng dụng rồi mở hai người chơi để đấu thử.

**Máy dùng trong hướng dẫn:** Windows 10/11, bộ xử lý Intel/AMD 64-bit. Chạy thử bằng **hai cửa sổ Windows trước**; phần Android nằm cuối tài liệu. Không cần điện thoại thật hoặc cấu hình gửi email để đăng nhập bằng tài khoản mẫu.

## 1. Những điều cần biết trước khi bắt đầu

Pull source chỉ tải mã nguồn. Các thư mục `bin` và `obj` thường không có trên Git; bạn phải **build** (biên dịch mã nguồn thành ứng dụng) trước khi mở file `.exe`.

Game gồm ba phần cần chạy cùng lúc:

| Thành phần | Công việc | Cách nhận biết |
|---|---|---|
| `Sudoku.Api` | Kiểm tra tài khoản, kết nối database | Lắng nghe cổng `5243` |
| `Sudoku.Server` | Ghép trận, kiểm tra nước đi, đồng bộ hai người | Lắng nghe cổng `5000` |
| `Sudoku.Mobile` | Giao diện chơi game; có bản Windows và Android | Cửa sổ đăng nhập Sudoku |

Để đấu thử, bạn cần **một API, một Game Server và hai cửa sổ game**. Hai người dùng tài khoản khác nhau.

Quy ước trong tài liệu:

- Thư mục ví dụ là `C:\Projects\UDM17-Sudoku`. Nếu bạn lưu nơi khác, thay đường dẫn này bằng đường dẫn thực tế.
- Mỗi khung lệnh ghi rõ chạy ở cửa sổ nào. Chỉ sao chép nội dung trong khung, không sao chép dấu ba huyền.
- Nhấn **Enter** để chạy lệnh. Nếu thấy lỗi, xử lý trước khi sang bước kế tiếp.

## 2. Cài công cụ — chỉ làm lần đầu

### 2.1. Cài Visual Studio và .NET

Cài **Visual Studio 2026 phiên bản 18.0 trở lên** để làm việc với .NET 10 theo [hướng dẫn .NET trên Windows của Microsoft](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Visual Studio là ứng dụng phát triển, khác với Visual Studio Code.

Mở **Visual Studio Installer → Modify**, chọn các workload:

- **.NET Multi-platform App UI development**: build giao diện MAUI Windows/Android.
- **.NET desktop development**: build Game Server Windows Forms.
- **ASP.NET and web development**: làm việc với API.

Trong **Individual components**, kiểm tra có:

- **.NET 10 SDK**; nếu chưa có, cài từ [trang tải .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Chọn SDK, không chỉ cài Runtime.
- **.NET Framework 4.7.2 targeting pack/SDK**. Nếu Installer không cung cấp, cài [Developer Pack 4.7.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472). Game Server và thư viện Shared dùng phiên bản này.
- Windows SDK mà workload MAUI yêu cầu; giữ các thành phần mặc định của workload.

Hoàn tất cài đặt, khởi động lại nếu trình cài yêu cầu. Tham khảo thêm [hướng dẫn cài MAUI](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0).

### 2.2. Cài SQL Server Express LocalDB

LocalDB là database nhỏ chạy trên máy của bạn để lưu tài khoản. Không cần thuê máy chủ hoặc cài SQL Server Management Studio cho demo này.

Trong Visual Studio Installer, tìm **SQL Server Express LocalDB** ở **Individual components** và cài nếu có. Cũng có thể cài LocalDB qua bộ cài SQL Server Express theo [tài liệu Microsoft](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb).

### 2.3. Kiểm tra công cụ

Nhấn **Windows**, tìm **Developer PowerShell for Visual Studio**, mở ứng dụng này. Dùng cửa sổ này cho các lệnh build bên dưới để có cả `dotnet` và `msbuild`.

```powershell
dotnet --list-sdks
dotnet workload list
msbuild -version
sqllocaldb info
```

**Kết quả cần có:** SDK bắt đầu bằng `10.0.`, workload MAUI phù hợp Windows, phiên bản MSBuild và lệnh LocalDB chạy được. Nếu vừa cài công cụ mà lệnh chưa nhận, đóng cửa sổ rồi mở Developer PowerShell mới.

Nếu thiếu MAUI, quay lại Installer để bổ sung workload. Không tiếp tục build khi máy chỉ có .NET Runtime.

## 3. Đến đúng thư mục source

Mở thư mục dự án bằng File Explorer. Tìm thư mục chứa đồng thời:

```text
UDM17-Sudoku.slnx
Sudoku.Api
Sudoku.Server
Sudoku.Mobile
Sudoku.Shared
```

Đó là thư mục gốc dự án, không phải thư mục cha chứa nó. Trong Developer PowerShell, chạy:

```powershell
Set-Location 'C:\Projects\UDM17-Sudoku'
Test-Path .\UDM17-Sudoku.slnx
```

**Cần thấy `True`.** Nếu thấy `False`, sửa đường dẫn trước. Không cần clone lại nếu đã có source.

## 4. Chuẩn bị database cục bộ

Trong cùng cửa sổ, chạy:

```powershell
sqllocaldb info
```

Nếu danh sách **chưa có `MSSQLLocalDB`**, chạy một lần:

```powershell
sqllocaldb create MSSQLLocalDB
```

Sau đó chạy:

```powershell
sqllocaldb start MSSQLLocalDB
sqllocaldb info MSSQLLocalDB
```

**Cần thấy:** instance `MSSQLLocalDB` có trạng thái `Running`. Không xóa instance có sẵn. Bước tiếp theo dùng database mới tên `SudokuDemoLocal`; API sẽ tự tạo database và các bảng bằng migration khi khởi động.

## 5. Build các thành phần

Chạy lần lượt trong Developer PowerShell, vẫn ở thư mục gốc dự án:

```powershell
dotnet build .\Sudoku.Api\Sudoku.Api.csproj -c Debug
```

```powershell
msbuild .\Sudoku.Server\Sudoku.Server.csproj /restore /p:Configuration=Debug
```

```powershell
dotnet build .\Sudoku.Mobile\Sudoku.Mobile.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -p:BrowserStack=false
```

Mỗi lệnh phải kết thúc thành công, thường có dòng **Build succeeded** và **0 Error(s)**. Lần đầu cần Internet để tải các gói NuGet, có thể mất vài phút. Tham số `TargetFrameworks` giới hạn lần build này vào Windows, để bước đầu chưa phải chuẩn bị iOS/Android.

Kiểm tra ba file đầu ra:

```powershell
Test-Path .\Sudoku.Api\bin\Debug\net10.0\Sudoku.Api.dll
Test-Path .\Sudoku.Server\bin\Debug\Sudoku.Server.exe
Test-Path .\Sudoku.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sudoku.Mobile.exe
```

**Cần thấy ba dòng `True`.** Không chạy các bước tiếp theo nếu còn thiếu file.

## 6. Cửa sổ số 1 — cấu hình và bật API

Trong cửa sổ Developer PowerShell hiện tại, sao chép cả khối:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=SudokuDemoLocal;Trusted_Connection=True;TrustServerCertificate=True'
dotnet run --project .\Sudoku.Api\Sudoku.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5243
```

Giải thích:

- `Development` bật dữ liệu mẫu cho thực hành.
- Chuỗi kết nối trỏ đến LocalDB của chính bạn, dùng tài khoản Windows hiện tại.
- JWT là mã xác thực đăng nhập. Với bản source hiện tại, giữ cấu hình JWT đi kèm dự án cho demo cục bộ; lưu ý giới hạn bên dưới.
- Các biến `$env:...` ghi đè cấu hình cho tiến trình này, không sửa file cấu hình trong Git.

**Cần thấy:** `Now listening on: http://127.0.0.1:5243`, không có lỗi database. Lần đầu API áp dụng migration và tạo hai tài khoản nếu chưa tồn tại:

| Người chơi | Username | Password |
|---|---|---|
| A | `test_player_a` | `SudokuTest!2026` |
| B | `test_player_b` | `SudokuTest!2026` |

**Giữ cửa sổ này mở.** Nó đang chạy API nên không xuất hiện dòng chờ nhập lệnh mới; đây là bình thường. Không cần tự chạy `dotnet ef database update` cho luồng này, cũng không cần SMTP để đăng nhập tài khoản mẫu.

Các biến môi trường chỉ tồn tại trong cửa sổ này. Nếu đóng rồi mở PowerShell mới, cần chạy lại cả khối cấu hình trên.

**Giới hạn của source hiện tại:** hàm `GenerateJwtToken` trong `AuthController.cs` đang ghi cố định khóa, issuer và audience, trong khi phần kiểm tra token đọc `JwtSettings` từ cấu hình. Vì vậy không tự thay riêng `JwtSettings` rồi mong phần tạo token đổi theo. Bộ cấu hình đi kèm hiện khớp với hàm này; nếu đã có biến môi trường/User Secrets JWT từ lần chạy khác, cần kiểm tra và bỏ ghi đè không khớp. Trước khi triển khai dùng thật, cần sửa phần tạo token để cùng đọc cấu hình và dùng khóa riêng.

## 7. Cửa sổ số 2 — bật Game Server và hai người chơi

Mở **một cửa sổ Developer PowerShell mới**, giữ cửa sổ API số 1 chạy. Chạy:

```powershell
Set-Location 'C:\Projects\UDM17-Sudoku'
Start-Process -FilePath '.\Sudoku.Server\bin\Debug\Sudoku.Server.exe' -WindowStyle Hidden
```

Game Server chạy nền. Kiểm tra hai dịch vụ:

```powershell
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 5000,5243 } | Select-Object LocalAddress,LocalPort
```

**Cần thấy cả `5000` và `5243`** trong cột `LocalPort`. Không mở thêm Game Server nếu cổng `5000` đã do bản Sudoku của bạn sử dụng. Nếu cổng bị ứng dụng khác chiếm, xem phần lỗi bên dưới.

Đặt địa chỉ cho hai client sẽ mở từ cửa sổ số 2:

```powershell
$env:SUDOKU_API_URL = 'http://127.0.0.1:5243/'
$env:SUDOKU_GAME_HOST = '127.0.0.1'
$env:SUDOKU_GAME_PORT = '5000'
```

Mở người A:

```powershell
Start-Process -FilePath '.\Sudoku.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sudoku.Mobile.exe'
```

Trong cửa sổ game vừa hiện, nhập `test_player_a` / `SudokuTest!2026`, bấm **Login & Play!** một lần và chờ vào **Lobby** (sảnh chờ có nút **FIND OPPONENT**).

Sau khi A vào Lobby, chạy lại đúng lệnh mở client trên để có **cửa sổ game thứ hai**. Đăng nhập cửa sổ thứ hai bằng `test_player_b` / `SudokuTest!2026`.

**Cần thấy:** hai cửa sổ game đều ở Lobby. Không dùng cùng tài khoản cho hai cửa sổ.

## 8. Chơi thử và xác nhận đã chạy được

1. Bấm **FIND OPPONENT →** ở B, rồi ngay sau đó bấm nút này ở A.
2. Chờ cả hai vào **Sudoku Duel**, thấy bảng 9 × 9 và tên đối thủ đúng.
3. Ở A, chọn ô trống trên **My Board**, bấm một số **1–9** bên dưới.
4. Ở B, mở **Rival's Board — VIEW ONLY**, quan sát ô A vừa nhập.
5. Ở B, quay về **My Board**, nhập một số; ở A, mở bảng đối thủ để kiểm tra chiều ngược lại.

Đề mỗi trận có thể khác nhau nên không có một ô/số cố định để sao chép. Nếu nhập sai, bấm **Erase** để xóa ô lỗi trước khi đi tiếp; số lần sai vẫn được giữ. **Hint** bị tắt trong trận đối kháng.

**Thành công khi:** hai người đăng nhập, vào cùng trận và nhìn thấy nước đi của nhau. Bạn có thể tiếp tục giải hoặc chờ hết giờ để xem kết quả.

Xem thêm bài tập và cách quan sát giao diện trong [demo cho người mới](DEMO_CHO_NGUOI_MOI.md#7-thực-hành-trên-bảng-sudoku).

## 9. Tắt và mở lại vào ngày khác

Khi dùng xong:

1. Đóng hai cửa sổ game.
2. Ở cửa sổ API số 1, nhấn **Ctrl+C** để dừng API.
3. Mở **Task Manager → Details**, xác định đúng `Sudoku.Server.exe` của dự án rồi **End task** nếu cần dừng Game Server chạy nền.

Database LocalDB vẫn giữ tài khoản. Phòng và lịch sử trận trong Game Server lưu trong bộ nhớ nên mất khi tắt server.

Lần sau, nếu chưa sửa source, **không cần cài công cụ hoặc build lại**: làm lại bước 6, 7 và 8. Mỗi cửa sổ mới phải đến đúng thư mục dự án trước. Nếu Game Server còn chạy thì bỏ qua lệnh mở server; không chạy bản trùng.

Nếu vừa pull code mới, đóng ứng dụng và dịch vụ, build lại ở bước 5 rồi chạy lại. Migration có thể thay đổi database; nếu đang giữ dữ liệu quan trọng, sao lưu trước khi chạy phiên bản mới.

## 10. Muốn dùng Android thay cho người A

Chỉ làm phần này sau khi chạy thành công hai client Windows. Giữ API và Game Server, đóng hai client Windows để giải phóng tài khoản.

### 10.1. Chuẩn bị Android

1. Cài [Android Studio](https://developer.android.com/studio) và hoàn tất Setup Wizard.
2. Trong **SDK Manager**, ghi lại **Android SDK Location**. Với hướng dẫn dưới, đặt SDK tại `%LOCALAPPDATA%\Android\Sdk`; nếu dùng đường dẫn khác, sửa dòng `$androidSdk` theo máy bạn.
3. Cài **Android SDK Platform-Tools**, **Android Emulator**, **Android SDK Command-line Tools** và platform/build-tools mà workload .NET Android yêu cầu. Chấp nhận giấy phép khi được hỏi. Trong Visual Studio Installer, giữ thành phần Android SDK/OpenJDK của workload MAUI để có Java dùng khi build.
4. Vào **Device Manager → Create Virtual Device**, chọn một mẫu điện thoại và tải system image tương thích máy. Tạo tên máy ảo **Sudoku Local**, kiểm tra ID nếu định dùng script `Start-Android.ps1` sau này. Bản hướng dẫn này mở máy ảo thủ công nên không phụ thuộc ID cố định.
5. Bấm **▶** để mở một máy ảo, chờ vào màn hình chính và thao tác phản hồi bình thường.

Xem [hướng dẫn tạo máy ảo chính thức](https://developer.android.com/studio/run/managing-avds). Nếu emulator báo thiếu tăng tốc/ảo hóa, xử lý theo thông báo của Android Studio trước khi tiếp tục.

### 10.2. Build APK và cài lên máy ảo

Trong cửa sổ Developer PowerShell số 2, tại thư mục gốc dự án:

```powershell
$androidSdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
dotnet publish .\Sudoku.Mobile\Sudoku.Mobile.csproj -c Release -f net10.0-android -p:TargetFrameworks=net10.0-android -p:BrowserStack=false -p:AndroidPackageFormats=apk "-p:AndroidSdkDirectory=$androidSdk" -o .\artifacts\android
```

**Cần thấy:** publish thành công và có file `com.companyname.sudoku.mobile-Signed.apk` trong `artifacts\android`. Thư mục này được tạo bởi lệnh publish.

Kéo file APK đó từ File Explorer vào cửa sổ máy ảo. Chờ cài xong, mở danh sách ứng dụng của điện thoại và chọn **Sudoku.Mobile**. Nếu đã có bản cũ, cài đè bằng APK mới; không cần xóa dữ liệu để cập nhật.

### 10.3. Đăng nhập và tìm trận

1. Android đăng nhập A: `test_player_a` / `SudokuTest!2026`.
2. Mở một client Windows bằng lệnh bước 7, đăng nhập B: `test_player_b` / `SudokuTest!2026`.
3. Làm lại bước 8 để ghép trận và kiểm tra đồng bộ.

APK cục bộ dùng `10.0.2.2` để đến API/cổng game trên máy tính, còn client Windows dùng `127.0.0.1`. Hai địa chỉ này là mặc định trong code cho từng nền tảng. Phần này dành cho Android Emulator chạy trên cùng máy, không áp dụng nguyên xi cho điện thoại thật.

Không cần dùng script mở nhanh trong lần thiết lập này. Nếu về sau dùng `testing/Start-Android.ps1`, đọc điều kiện đường dẫn SDK, ID máy ảo và vị trí APK trong script; nó không tự build source hoặc thay bạn cấu hình database.

## 11. Lỗi thường gặp

| Thông báo/hiện tượng | Cách xử lý |
|---|---|
| `dotnet` không được nhận diện | Cài .NET 10 **SDK**, mở terminal mới, kiểm tra `dotnet --list-sdks`. |
| `msbuild` không được nhận diện | Mở **Developer PowerShell for Visual Studio**, kiểm tra đã cài workload desktop. |
| `sqllocaldb` không được nhận diện | Cài LocalDB, mở terminal mới; nếu vẫn lỗi, kiểm tra đường dẫn công cụ LocalDB theo bộ cài. |
| Không tìm thấy project hoặc `.slnx` | Quay lại bước 3; đang đứng sai thư mục. |
| Thiếu reference assemblies `.NETFramework,Version=v4.7.2` | Cài **Developer Pack/targeting pack 4.7.2**, không chỉ Runtime. |
| `NETSDK1147` hoặc thiếu MAUI workload | Bổ sung workload MAUI bằng Visual Studio Installer; kiểm tra bằng `dotnet workload list`. |
| `NU1301`, tải NuGet thất bại | Kiểm tra Internet/proxy và quyền truy cập nguồn NuGet, sau đó chạy lại đúng lệnh build bị lỗi. |
| API báo lỗi SQL/không mở được database | Kiểm tra LocalDB `Running`, chạy khối cấu hình bước 6 trong **cùng cửa sổ** chạy API. Không dùng chuỗi kết nối máy của thành viên khác. |
| API báo lỗi JWT hoặc token không hợp lệ | Kiểm tra `JwtSettings` trong cấu hình và các ghi đè môi trường/User Secrets có khớp phần tạo token không; xem giới hạn ở bước 6. |
| `Address already in use` hoặc cổng `5000`/`5243` đã bị chiếm | Kiểm tra dịch vụ Sudoku cũ có đang chạy không. Dùng Task Manager xác định tiến trình theo PID; không kết thúc tiến trình lạ chỉ để giải phóng cổng. |
| Đăng nhập sai tài khoản mẫu | Kiểm tra đúng mật khẩu, API chạy Development và migration thành công. Seeder chỉ tạo tài khoản chưa có, không đặt lại mật khẩu của tài khoản đã tồn tại. |
| Đã đăng nhập nhưng không vào Lobby | Kiểm tra Game Server/cổng `5000`, không chỉ API `5243`. |
| Không tìm được đối thủ | Cần hai tài khoản khác nhau, cùng server và cùng đang tìm. Nếu hết thời gian chờ, tìm lại ở cả hai bên. |
| Android build báo không tìm thấy SDK/Java | Kiểm tra Android SDK Location, đường dẫn `$androidSdk`, thành phần OpenJDK/Android của MAUI và nội dung lỗi build. |
| Android còn dùng giao diện/code cũ | Build lại APK rồi cài đè file mới; pull source không tự cập nhật ứng dụng đã cài. |
| Android báo “isn't responding” | Chọn Wait một lần, đợi khoảng một phút; nếu vẫn treo, đóng app/máy ảo và thử lại một lần. Nếu tái diễn, cần kiểm tra log. Không Wipe Data để thử nhanh. |

Khi nhờ người khác hỗ trợ, gửi **bước đang làm, lệnh đã chạy và thông báo lỗi**. Không gửi khóa JWT, token đăng nhập hoặc mật khẩu riêng.

## 12. Checklist hoàn tất lần đầu

- [ ] Có .NET 10 SDK, workload MAUI/desktop, targeting pack 4.7.2 và LocalDB.
- [ ] Đang ở đúng thư mục có `UDM17-Sudoku.slnx`.
- [ ] Build API, Game Server và Windows client thành công.
- [ ] API mở được database cục bộ và lắng nghe `5243`.
- [ ] Game Server lắng nghe `5000`.
- [ ] A và B đăng nhập bằng hai tài khoản khác nhau.
- [ ] Hai bên ghép trận và nhìn thấy nước đi của nhau.
- [ ] Nếu dùng Android: đã publish và cài APK cục bộ, A vào trận với Windows B.

Tài liệu được đối chiếu với source và cấu hình hiện tại; chưa được thực hành toàn bộ trên một bản Windows mới hoàn toàn. Tài khoản demo và cấu hình HTTP ở đây chỉ dùng phát triển cục bộ.
