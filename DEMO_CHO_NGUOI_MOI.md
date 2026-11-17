# Demo Sudoku: tự mở và chơi thử một trận

**Vừa lấy source về máy mới?** Làm [hướng dẫn lần đầu](HUONG_DAN_LAN_DAU.md) trước để cài công cụ, cấu hình database và build bộ chạy.

Bạn không cần biết lập trình để làm phần thực hành này. Làm theo thứ tự từ bước 1 đến bước 7; chỉ sang bước sau khi thấy kết quả mong đợi.

**Mục tiêu:** trên cùng một máy tính, mở Sudoku trên điện thoại Android giả lập cho người A và mở Sudoku trên Windows cho người B. Hai người đăng nhập, tìm nhau và chơi cùng một trận. Bạn có thể tự điều khiển cả hai cửa sổ.

**Phạm vi:** hướng dẫn dùng bộ chạy đã chuẩn bị tại `E:\prj_sudoku`. Nếu dùng máy khác hoặc chưa có bộ chạy, xem phần “Chuẩn bị lần đầu” cuối tài liệu trước.

## 1. Hiểu những gì sắp mở

| Tên | Hiểu đơn giản | Bạn cần làm gì? |
|---|---|---|
| Android giả lập | Một chiếc điện thoại ảo trên màn hình máy tính | Dùng làm người chơi A |
| Sudoku Windows | Cửa sổ game chạy trực tiếp trên máy tính | Dùng làm người chơi B |
| API | Dịch vụ kiểm tra tài khoản và mật khẩu | Script mở giúp bạn |
| Game Server | Dịch vụ ghép hai người và xử lý trận đấu | Script mở giúp bạn |
| Lobby | Sảnh chờ sau khi đăng nhập | Bấm tìm đối thủ tại đây |

Android Studio dùng để quản lý điện thoại ảo. Bạn không cần mở mã nguồn Sudoku trong Android Studio.

## 2. Mở PowerShell

1. Lưu công việc đang làm, đóng các điện thoại giả lập cũ và cửa sổ Sudoku cũ.
2. Nhấn phím **Windows**, gõ **PowerShell**, rồi mở **Windows PowerShell**.
3. Sao chép dòng dưới, dán vào PowerShell và nhấn **Enter**:

```powershell
Set-Location 'E:\prj_sudoku\UDM17-Sudoku'
```

**Bạn cần thấy:** dòng chờ nhập lệnh có đường dẫn `E:\prj_sudoku\UDM17-Sudoku`. Lệnh này chỉ đưa PowerShell đến đúng thư mục dự án.

Chỉ sao chép nội dung bên trong khung lệnh. Không sao chép dấu ba huyền hoặc chữ `PS ...>` nếu thấy ở cửa sổ PowerShell.

## 3. Bật hệ thống và mở Android A

Dán dòng sau vào cùng cửa sổ PowerShell rồi nhấn **Enter**:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\testing\Start-Android.ps1
```

Lệnh này chạy script có sẵn của dự án. `-ExecutionPolicy Bypass` áp dụng cho tiến trình PowerShell vừa mở, không đổi thiết lập chính sách lâu dài của máy.

Script lần lượt bật API, bật Game Server, mở máy ảo **Sudoku Local**, chờ Android sẵn sàng rồi mở Sudoku. Các dịch vụ chạy nền nên bạn có thể không thấy cửa sổ riêng của chúng.

**Trong lúc chờ:**

- Lần mở máy ảo có thể mất vài phút. Sau khi Android khởi động, script còn chờ thêm 30 giây.
- Không chạy lại lệnh liên tục và không bấm nút ▶ mở thêm máy ảo trong Android Studio.
- Nếu script báo lỗi màu đỏ, dừng tại đây và xem bảng xử lý lỗi bên dưới.

**Bạn cần thấy:** điện thoại ảo mở ứng dụng Sudoku đến màn hình **Welcome Back!**, có ô tài khoản, ô mật khẩu và nút **Login & Play!**. Nếu vẫn là màn hình tím `.NET`, tiếp tục chờ; chưa nhập tài khoản.

Dòng `Sudoku is open...` trong PowerShell chỉ xác nhận đã gửi lệnh mở app. Hãy nhìn điện thoại để xác nhận giao diện đã dùng được.

## 4. Đăng nhập người A trên Android

Trên điện thoại ảo, bấm từng ô và nhập:

| Ô nhập | Nội dung |
|---|---|
| Username | `test_player_a` |
| Password | `SudokuTest!2026` |

Bấm **Login & Play!** đúng một lần. Nếu nút hiện **Đang kết nối...**, chờ kết quả.

**Bạn cần thấy:** sảnh Lobby có chữ **Ready to play?** và nút **FIND OPPONENT →**. Tạm thời chưa bấm tìm đối thủ.

Đây là tài khoản demo, được API tạo khi chạy ở môi trường Development nếu chưa tồn tại. Demo này dùng tài khoản sẵn có nên không cần đăng ký hoặc nhận mã qua email.

## 5. Mở và đăng nhập người B trên Windows

Quay lại PowerShell. Khi lệnh ở bước 3 đã chạy xong và dòng chờ nhập lệnh xuất hiện lại, dán:

```powershell
Start-Process -FilePath '.\Sudoku.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sudoku.Mobile.exe'
```

**Bạn cần thấy:** một cửa sổ Sudoku trên Windows, bên cạnh điện thoại ảo. Đây là người B; đừng mở thêm bản thứ ba.

Trong cửa sổ Windows, nhập:

| Ô nhập | Nội dung |
|---|---|
| Username | `test_player_b` |
| Password | `SudokuTest!2026` |

Bấm **Login & Play!** và chờ vào Lobby.

**Kiểm tra trước khi tiếp tục:** cả Android A và Windows B đều đang ở Lobby. Hai bên phải dùng hai tài khoản khác nhau.

## 6. Ghép hai người vào cùng trận

1. Trên Windows B, bấm **FIND OPPONENT →**.
2. Ngay sau đó, trên Android A, bấm **FIND OPPONENT →**.
3. Chờ hệ thống ghép trận, không bấm liên tục.

**Bạn cần thấy:** cả hai bên hiện màn hình **Sudoku Duel**, có bảng 9 × 9, đồng hồ đếm ngược và thông tin đối thủ. A gặp `test_player_b`, B gặp `test_player_a`.

Nếu một bên đã hết thời gian tìm kiếm, đưa cả hai về trạng thái có thể tìm trận rồi thực hiện lại hai thao tác trên. Nếu xuất hiện màn hình chuẩn bị phòng, bấm **I'M READY** ở bên chưa sẵn sàng.

## 7. Thực hành trên bảng Sudoku

Luật cơ bản: điền số từ **1 đến 9** vào ô trống. Trong mỗi hàng ngang, cột dọc và khối vuông 3 × 3, các số không được lặp lại. Số có sẵn của đề không sửa được.

### Bài tập A — Nhập một số

1. Trên Android A, chọn tab **My Board** nếu đang xem bảng đối thủ.
2. Bấm một ô trống. Ghi nhớ vị trí: đếm hàng từ trên xuống, cột từ trái sang, bắt đầu từ 1.
3. Bấm một nút số **1–9** bên dưới bảng. Ví dụ thao tác là chọn ô rồi bấm `5`; số đúng phụ thuộc đề đang hiển thị, không phải ô nào cũng điền `5`.
4. Chờ game xử lý nước đi trước khi bấm tiếp.

**Quan sát:** số vừa nhập xuất hiện ở ô đã chọn. Nếu đúng, tiến độ được cập nhật; nếu sai, game đánh dấu lỗi và tăng **Mistakes**. Việc không trùng số nhìn thấy chưa đủ bảo đảm đó là đáp án đúng của đề.

Nếu nhập sai, bấm **Erase** để xóa ô lỗi rồi mới nhập tiếp. Xóa ô lỗi không xóa số lần đã sai. Nút **Hint** không cung cấp gợi ý trong trận đấu cạnh tranh.

### Bài tập B — Kiểm tra người kia có nhận nước đi không

1. Trên Windows B, bấm tab **Rival's Board — VIEW ONLY**.
2. Tìm ô A vừa nhập theo hàng và cột đã ghi nhớ.
3. Kiểm tra bảng đối thủ đã nhận thay đổi. Nếu A đã xóa ô lỗi, hãy quan sát lúc A nhập một nước mới.
4. Trên Windows B, quay về **My Board**, chọn ô trống và nhập một số.
5. Trên Android A, mở **Rival's Board** để quan sát nước đi của B.

**Bạn cần thấy:** hai phía nhận được thay đổi của đối thủ. Tab bảng đối thủ chỉ dùng để xem; muốn nhập số, phải quay về **My Board**.

### Bài tập C — Theo dõi đến kết quả

Tiếp tục giải nếu muốn. Nếu chưa biết giải hết bảng, có thể dừng nhập và chờ đồng hồ về `00:00` để quan sát cách hệ thống kết thúc trận. Không đóng một người chơi giữa chừng nếu đang muốn thử luồng kết thúc bình thường.

**Bạn cần thấy:** game chuyển sang màn hình kết quả. Người thắng/thua phụ thuộc diễn biến và quy tắc tính kết quả; không mặc định A sẽ thắng. Làm theo nút trở về Lobby trên giao diện để quay lại sảnh.

## 8. Khi nào demo được coi là thành công?

- [ ] A đăng nhập thành công trên Android.
- [ ] B đăng nhập thành công trên Windows bằng tài khoản khác A.
- [ ] Cả hai vào cùng trận và thấy tên đối thủ đúng.
- [ ] A nhập số, B xem được thay đổi ở bảng đối thủ.
- [ ] B nhập số, A xem được thay đổi ở bảng đối thủ.
- [ ] Nếu thực hành đến hết trận: thấy màn hình kết quả và quay lại Lobby.

Câu giới thiệu có thể đọc khi trình bày: “Đây là game Sudoku đối kháng giữa hai người. Tôi dùng Android cho người A và Windows cho người B. Hai người đăng nhập bằng tài khoản riêng, tìm đối thủ và giải Sudoku. Hệ thống kiểm tra nước đi và cập nhật bảng của đối thủ để hai bên theo dõi.”

## 9. Gặp lỗi thì làm gì?

| Bạn thấy gì? | Việc cần làm |
|---|---|
| `Set-Location` báo không tìm thấy đường dẫn | Mở File Explorer, kiểm tra có thư mục `E:\prj_sudoku\UDM17-Sudoku` không. Nếu dự án ở nơi khác, thay đường dẫn bước 2 bằng đường dẫn thực tế. |
| Không tìm thấy DLL, EXE hoặc APK | Bộ chạy chưa đủ. Xem “Chuẩn bị lần đầu” bên dưới; chạy lại script không tự build các file này. |
| Script yêu cầu đóng emulator khác | Đóng các điện thoại giả lập đang mở, chờ chúng tắt hẳn rồi chạy lại bước 3 một lần. |
| Không có máy ảo `Sudoku_Local` | Cần tạo máy ảo có đúng ID này trong Android Studio; xem tài liệu Android bên dưới. |
| Android báo “isn't responding” | Chọn **Wait** một lần, chờ khoảng một phút. Nếu vẫn treo, chọn **Close app**, đóng máy ảo rồi thử lại bước 3 một lần. Nếu lặp lại, cần kiểm tra log; không tiếp tục bấm Wait vô hạn. |
| Đăng nhập báo sai | Kiểm tra đúng A/B, chữ hoa `S`, `T`, dấu `!` và không có khoảng trắng thừa. Tài khoản đã tồn tại không được script tự đặt lại mật khẩu. |
| Báo tài khoản đang có phiên hoạt động | Đóng cửa sổ Sudoku cũ đang dùng cùng tài khoản. Không dùng A cho cả hai bên. |
| Không kết nối được máy chủ | Chạy lệnh kiểm tra cổng dưới đây. Xem lỗi API tại `E:\prj_sudoku\outputs\android-local\api-error.log` và `api.log`. |
| Không ghép được trận | Kiểm tra cả hai đã vào Lobby, dùng hai tài khoản khác nhau và cùng bấm tìm đối thủ khi bên kia còn đang tìm. |
| Không nhập số được | Quay về **My Board**; chọn ô trống của đề. Nếu đang có ô sai, bấm **Erase** trước. |

Lệnh kiểm tra dịch vụ, chạy trong PowerShell:

```powershell
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 5000,5243 } | Select-Object LocalAddress,LocalPort
```

**Cần có cả `5000` và `5243`** trong cột `LocalPort`. Nếu thiếu một cổng thì dịch vụ tương ứng chưa lắng nghe. Có đủ hai cổng chỉ là kiểm tra ban đầu; đăng nhập và vào trận mới xác nhận luồng thực tế hoạt động.

Không dùng **Wipe Data** hoặc xóa database để thử sửa lỗi: các thao tác đó có thể làm mất dữ liệu.

## 10. Khi thực hành xong

Đóng cửa sổ Sudoku Windows và điện thoại giả lập. API và Game Server do script mở vẫn có thể chạy nền; đóng PowerShell không bảo đảm dừng chúng. Nếu chỉ nghỉ rồi chơi tiếp, có thể giữ hai dịch vụ; lần sau script sẽ kiểm tra cổng trước khi mở thêm.

Nếu cần dừng hẳn, mở **Task Manager → Details**, xác định đúng tiến trình Game Server `Sudoku.Server.exe` và tiến trình API có command line chứa `Sudoku.Api.dll`, rồi kết thúc chúng sau khi mọi người đã chơi xong. Không kết thúc tất cả tiến trình `dotnet.exe`, vì có thể có ứng dụng khác đang dùng. Tắt Game Server làm mất phòng và lịch sử trận đang lưu trong bộ nhớ.

## Chuẩn bị lần đầu hoặc chạy sau khi sửa code

Trên máy hiện tại đã có API DLL, Game Server EXE, Windows client EXE và APK Android ở các đường dẫn dùng trong hướng dẫn. Điều đó chưa xác nhận chúng chạy thành công trên một máy mới.

Nếu chưa có môi trường, làm phần [yêu cầu môi trường, cấu hình và cài đặt trong README](README.md#5-yêu-cầu-môi-trường): Windows, .NET SDK 10, MAUI, .NET Framework 4.7.2 Developer Pack, SQL Server/LocalDB và cấu hình database/JWT. Sau đó xem [hướng dẫn Android](testing/ANDROID.md) để chuẩn bị Android SDK và máy ảo `Sudoku_Local`. Script hiện tìm Android SDK tại `%LOCALAPPDATA%\Android\Sdk`.

API cần kết nối được database để khởi động và tạo tài khoản demo. Demo đăng nhập bằng tài khoản mẫu không yêu cầu gửi email; chức năng đăng ký/OTP là luồng riêng.

Lệnh build lại API, server, Windows client và APK nằm trong [hướng dẫn chạy trên máy hiện tại](../HUONG_DAN_CHAY_SUDOKU_ANDROID_STUDIO.md#5-build-lại-sau-khi-sửa-source). Nếu file đó không đi kèm bản sao dự án, dùng phần cài đặt trong README và hướng dẫn Android ở trên.

Sau khi có APK mới, để Android ổn định rồi cài lại bằng:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\testing\Start-Android.ps1 -Reinstall
```

Không cần build hoặc cài lại mỗi lần chơi. Tài khoản và mật khẩu mẫu chỉ dùng cho thực hành cục bộ.
