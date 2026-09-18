# Bug & Test Case Notes

Ngày cập nhật: 2026-09-10  
Phạm vi: Login/API, kết nối Game Server, bảng đấu Sudoku hai người và yêu cầu tournament 2 người trong tài liệu đính kèm.

## Quy ước trạng thái

- `Not test yet`: chưa đủ chức năng, môi trường hoặc dữ liệu để chạy.
- `Passed`: đã chạy và kết quả đúng kỳ vọng.
- `Failed`: đã chạy và tái hiện lỗi.
- `Work in progress`: đã sửa/đang sửa nhưng chưa hoàn tất kiểm thử end-to-end.

> Lưu ý: `Test result 1/2/3` là ba lần kiểm thử độc lập hoặc ba mốc kiểm tra gần nhất. Dấu `—` nghĩa là chưa chạy lần đó.

## TC-LOGIN-01 — Login không kết nối được HTTP API

- **Name/Screen:** Login — API connection
- **Description:** Client hiển thị “Không thể kết nối đến máy chủ” dù máy có mạng. Desktop gọi cố định `https://localhost:7169/`; Android gọi `http://10.0.2.2:5243/`. API chỉ hoạt động nếu đúng profile/port và database migration khởi động thành công.
- **Proposed solution:** Chuyển API base URL thành cấu hình theo môi trường; dùng `https://localhost:7169` cho Windows development, `http://10.0.2.2:5243` cho Android emulator và LAN IP cho thiết bị thật. Thêm health check khi khởi động, log URL thực tế và hướng dẫn chạy API bằng đúng launch HTTPS.
- **Input/Scenario:** Chạy client nhưng API chưa chạy, chạy API bằng profile HTTP khác với URL client, hoặc chứng chỉ HTTPS localhost không được client tin cậy; nhập tài khoản hợp lệ và bấm Đăng nhập.
- **Expected result:** Client gọi đúng `POST /api/auth/login`; API trả phản hồi; nếu không kết nối được phải báo đúng lỗi kết nối thay vì quy lỗi cho mạng Internet.
- **Test result 1:** `Failed` — hành vi lỗi đã được người dùng cung cấp: loading rồi hiện popup lỗi kết nối chung.
- **Test result 2:** `Not run` — chưa có tài khoản test/database SQL Server hợp lệ để xác minh HTTP end-to-end.
- **Test result 3:** `Not run` — chưa xác minh trên Android/emulator.
- **Stage:** `Work in progress`
- **Proof:** `Sudoku.Mobile/Network/ApiClient.cs` chứa base URL cố định; `Sudoku.Api/Properties/launchSettings.json` khai báo HTTP `5243` và HTTPS `7169`; endpoint nằm tại `Sudoku.Api/Controllers/AuthController.cs`.

## TC-LOGIN-02 — Mọi lỗi API bị chuyển thành lỗi mạng chung

- **Name/Screen:** Login — Error handling
- **Description:** `ApiClient.PostAsync` trả `default` cho mọi HTTP status không thành công và mọi exception. `LoginPage` vì vậy không phân biệt connection refused, timeout, 401, 403, 500 hoặc JSON lỗi.
- **Proposed solution:** Thay kiểu trả về nullable bằng `ApiResult<T>` chứa status code, response body và loại lỗi. Bắt riêng `HttpRequestException`, `TaskCanceledException` và lỗi deserialize; ánh xạ 401/403/500/timeout/connection-refused sang thông báo người dùng tương ứng, đồng thời ghi diagnostic chi tiết vào console.
- **Input/Scenario:** Lần lượt mô phỏng backend tắt, timeout, HTTP 401, HTTP 403, HTTP 500 và response JSON sai định dạng.
- **Expected result:** Hiển thị thông báo riêng cho từng loại lỗi; console có request URL, method, status code, response body và exception; không lộ stack trace cho người dùng.
- **Test result 1:** `Failed` — đọc mã xác nhận tất cả nhánh trên đều có thể trở thành `null` và popup lỗi mạng chung.
- **Test result 2:** `Not run` — chưa sửa cơ chế trả lỗi có kiểu.
- **Test result 3:** `Not run` — chưa có integration test cho các HTTP status.
- **Stage:** `Failed`
- **Proof:** `Sudoku.Mobile/Network/ApiClient.cs` — nhánh non-success và `catch` đều `return default`; `Sudoku.Mobile/LoginPage.xaml.cs` dùng fallback “Không thể kết nối...”.

## TC-LOGIN-03 — Sai tài khoản/mật khẩu phải là lỗi xác thực

- **Name/Screen:** Login — Invalid credentials
- **Description:** Backend hiện trả HTTP 200 kèm `Success=false`, không trả HTTP 401. Client phụ thuộc hoàn toàn vào `Message` trong body.
- **Proposed solution:** Chuẩn hóa API: trả `401 Unauthorized` cho tài khoản/mật khẩu sai và một error code ổn định như `INVALID_CREDENTIALS`. Client ưu tiên status/error code thay vì so sánh message; giữ nội dung thông báo bằng tiếng Việt ở lớp UI.
- **Input/Scenario:** Nhập username/email không tồn tại hoặc mật khẩu sai.
- **Expected result:** Hiển thị “Tài khoản hoặc mật khẩu không chính xác”; không hiển thị lỗi mạng; loading kết thúc.
- **Test result 1:** `Code review` — backend trả `200 OK` với message “Sai tài khoản hoặc mật khẩu.”.
- **Test result 2:** `Not run` — chưa kiểm thử bằng database thực.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** `Sudoku.Api/Controllers/AuthController.cs`, action `Login`.

## TC-LOGIN-04 — Loading/retry và request đăng nhập trùng

- **Name/Screen:** Login — Retry/loading state
- **Description:** Nút bị disable trong khi request chạy, nhưng code chưa dùng cancellation/request guard riêng và chưa có test chứng minh retry không tạo request pending trùng. Một exception phát sinh ngoài `PostAsync` có thể bỏ qua đoạn phục hồi nút cuối hàm.
- **Proposed solution:** Dùng cờ `_isLoggingIn` hoặc `SemaphoreSlim` để chỉ cho phép một request; bao toàn bộ handler bằng `try/catch/finally`; luôn khôi phục nút/loading trong `finally`. Tạo `CancellationTokenSource` cho mỗi lần thử và hủy request cũ trước khi retry.
- **Input/Scenario:** Bấm Login, gây timeout/lỗi; bấm Thử lại nhanh nhiều lần; thử lỗi khi lưu token hoặc điều hướng.
- **Expected result:** Mỗi thao tác chỉ gửi một request; retry tạo đúng một request mới; nút và loading luôn trở về bình thường trong `finally`.
- **Test result 1:** `Code review` — có disable nút nhưng không có `try/finally` bao toàn bộ handler.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** `Sudoku.Mobile/LoginPage.xaml.cs`, handler `OnLoginClicked`.

## TC-LOGIN-05 — Đăng nhập HTTP thành công nhưng TCP Game Server thất bại

- **Name/Screen:** Login — TCP Game Server connection
- **Description:** Sau khi API xác thực thành công, client còn phải kết nối `Sudoku.Server` qua TCP port 5000. Đây là hai server/luồng kết nối khác nhau.
- **Proposed solution:** Tách rõ trạng thái HTTP authentication và TCP game connection. Sau login, thử kết nối TCP với timeout/retry hữu hạn; hiển thị hành động “Thử kết nối Game Server lại” mà không bắt người dùng đăng nhập lại; chỉ điều hướng Lobby khi handshake TCP thành công.
- **Input/Scenario:** Chạy API nhưng tắt `Sudoku.Server`; đăng nhập bằng tài khoản hợp lệ.
- **Expected result:** Giữ rõ trạng thái “đăng nhập thành công nhưng Game Server chưa kết nối”; không gọi đây là lỗi HTTP/API; nút trở lại bình thường.
- **Test result 1:** `Code review` — client có thông báo riêng cho TCP port 5000.
- **Test result 2:** `Not run` — thiếu tài khoản/database test.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** `Sudoku.Mobile/LoginPage.xaml.cs`; `Sudoku.Mobile/Network/TcpGameClient.cs`; `Sudoku.Server/Network/Server.cs`.

## TC-LOGIN-06 — CORS và desktop networking

- **Name/Screen:** Login — CORS/host configuration
- **Description:** MAUI `HttpClient` không chịu CORS như browser, nên CORS không phải nguyên nhân trực tiếp trong client hiện tại. Rủi ro thực tế là HTTPS development certificate, port/profile và localhost chỉ trỏ về chính thiết bị.
- **Proposed solution:** Không thêm CORS chỉ để chữa lỗi MAUI. Cấu hình host theo platform/environment, cài/trust development certificate trên Windows, dùng `10.0.2.2` cho emulator và LAN IP cho thiết bị thật; đưa URL production vào cấu hình build thay vì hard-code.
- **Input/Scenario:** Windows client gọi HTTPS localhost; Android emulator gọi `10.0.2.2`; thiết bị Android thật gọi địa chỉ hiện tại.
- **Expected result:** Windows và emulator kết nối đúng; thiết bị thật dùng LAN IP/config phù hợp; production không hard-code localhost.
- **Test result 1:** `Code review` — URL đang hard-code theo `#if ANDROID`.
- **Test result 2:** `Not run` — Windows HTTPS chưa test end-to-end.
- **Test result 3:** `Not run` — thiết bị thật chưa test.
- **Stage:** `Failed`
- **Proof:** `Sudoku.Mobile/Network/ApiClient.cs`; không có cấu hình CORS trong `Sudoku.Api/Program.cs` nhưng MAUI native không cần browser CORS.

## TC-SUDOKU-01 — Bảng không có đủ 81 ô runtime

- **Name/Screen:** SudokuPage — Board construction
- **Description:** XAML cũ chỉ khai báo row/column definitions nhưng không tạo cell; code lại truy cập `SudokuGrid.Children[row * 9 + col]`.
- **Proposed solution:** Tạo đủ 81 button khi khởi tạo trang, lưu bằng `Button[9,9]`, rồi truy xuất trực tiếp theo row/column. Không phụ thuộc thứ tự của `Grid.Children`; thêm kiểm tra tự động xác nhận đủ 81 cell.
- **Input/Scenario:** Điều hướng từ Lobby vào SudokuPage và gọi `UpdateBoard()`.
- **Expected result:** Có đúng 81 ô, truy cập hợp lệ theo row/column, không phát sinh lỗi index.
- **Test result 1:** `Failed` — code cũ có 0 child nhưng truy cập index 0..80.
- **Test result 2:** `Passed` — đã dựng mảng 81 button và build Windows thành công.
- **Test result 3:** `Not run` — chưa xác minh trực quan toàn bộ màn hình qua UI automation.
- **Stage:** `Work in progress`
- **Proof:** Thay đổi tại `Sudoku.Mobile/SudokuPage.xaml.cs`; build `net10.0-windows10.0.19041.0` hoàn tất với 0 compile error trước khi chạy app.

## TC-SUDOKU-02 — Sai cấu trúc 9×9 / nhóm 3×3

- **Name/Screen:** SudokuPage — 3×3 box layout
- **Description:** Bảng cũ là một grid phẳng 9×9, không có cấu trúc/đường phân cách chín vùng 3×3 rõ ràng.
- **Proposed solution:** Dùng outer grid 3×3 chứa chín inner grid 3×3; đặt spacing/border outer đậm hơn spacing cell. Giữ kích thước vuông và kiểm thử trực quan tại các độ phân giải Windows/Android.
- **Input/Scenario:** Mở màn Sudoku và quan sát đường kẻ sau hàng/cột 3 và 6.
- **Expected result:** Toàn bảng là 9×9; gồm đúng 9 vùng; mỗi vùng đúng 3×3; đường vùng đậm hơn đường giữa cell.
- **Test result 1:** `Failed` — giao diện cũ không nhóm các vùng 3×3.
- **Test result 2:** `Passed (structural)` — code mới tạo outer grid 3×3, mỗi child là inner grid 3×3.
- **Test result 3:** `Not run (visual)` — cần ảnh chụp màn hình sau khi đăng nhập/vào trận.
- **Stage:** `Work in progress`
- **Proof:** `Sudoku.Mobile/SudokuPage.xaml` và method `BuildSudokuGrid()` trong `Sudoku.Mobile/SudokuPage.xaml.cs`.

## TC-SUDOKU-03 — Hai người chơi nhận đề khác nhau

- **Name/Screen:** SudokuPage — Shared match puzzle
- **Description:** Trang cũ luôn gọi `GenerateSudoku()` cục bộ; mỗi client có thể sinh đề khác dù server đã gửi puzzle chung.
- **Proposed solution:** Trong match online, chỉ nạp `MatchSession.CurrentMatch.Puzzle` có đúng 81 giá trị hợp lệ; không tự sinh đề ở client. Server là nguồn dữ liệu duy nhất; khi reconnect phải tải `OwnBoard`/snapshot theo cùng `MatchId`.
- **Input/Scenario:** Player A và Player B cùng vào một match, so sánh 81 giá trị ban đầu.
- **Expected result:** Cả hai client hiển thị đúng cùng `MatchId` và cùng mảng `Puzzle` 81 phần tử do server gửi.
- **Test result 1:** `Failed` — constructor cũ tự sinh đề local trên từng máy.
- **Test result 2:** `Passed (code/build)` — constructor mới ưu tiên `MatchSession.CurrentMatch.Puzzle` và chỉ fallback local khi không có match.
- **Test result 3:** `Not run (2-client E2E)` — chưa có hai phiên đăng nhập hoạt động đồng thời.
- **Stage:** `Work in progress`
- **Proof:** `Sudoku.Mobile/Models/MatchSession.cs`; `LoadMatchPuzzle()` trong `Sudoku.Mobile/SudokuPage.xaml.cs`.

## TC-SUDOKU-04 — Nước đi chỉ đổi UI, không gửi server

- **Name/Screen:** SudokuPage — Submit move
- **Description:** Handler cũ gán trực tiếp text vào button, không gọi server nên điểm/tiến độ hai phía không đồng bộ và số sai không được server xác thực.
- **Proposed solution:** Gửi mọi nước đi online qua `MatchService.SubmitMoveAsync`; khóa UI trong lúc chờ; chỉ cập nhật ô khi `Accepted && IsCorrect`; xử lý error code cụ thể và nhận `OpponentProgressUpdated` để cập nhật tiến độ đối thủ.
- **Input/Scenario:** Chọn ô trống, nhập một số đúng rồi một số sai trong match online.
- **Expected result:** Mỗi nước đi gửi đúng `MatchId`, row, column, value; chỉ số đúng được ghi/khóa; số sai bị từ chối và hiển thị thông báo.
- **Test result 1:** `Failed` — code cũ chỉ cập nhật `_selectedCell.Text`.
- **Test result 2:** `Passed (code/build)` — đã nối `MatchService.SubmitMoveAsync` và xử lý Accepted/IsCorrect.
- **Test result 3:** `Not run (TCP E2E)` — chưa có hai client + Game Server hoạt động cùng lúc.
- **Stage:** `Work in progress`
- **Proof:** `OnNumberClicked` trong `Sudoku.Mobile/SudokuPage.xaml.cs`; server có validation tại `Sudoku.Server/Game/MatchManager.cs`.

## TC-SUDOKU-05 — Generator và luật hàng/cột/vùng 3×3

- **Name/Screen:** Server — Sudoku generation/validation
- **Description:** Xác minh puzzle/solution là 9×9, mỗi hàng/cột/vùng 3×3 hợp lệ và puzzle có nghiệm duy nhất.
- **Proposed solution:** Giữ validation ở server trước khi tạo match, tiếp tục dùng generator có seed cho test tái lập. Bổ sung property-based/multiple-seed tests cho cả ba độ khó và không bao giờ gửi solution xuống client.
- **Input/Scenario:** Chạy toàn bộ `Sudoku.Server.Tests` với Easy/Medium/Hard và các seed trong test.
- **Expected result:** Không tạo puzzle sai kích thước hoặc sai luật; tất cả test generator/validator pass.
- **Test result 1:** `Passed` — 42/42 server tests passed.
- **Test result 2:** `Passed` — chạy lại sau sửa SudokuPage, vẫn 42/42 passed.
- **Test result 3:** `—`.
- **Stage:** `Passed`
- **Proof:** Lệnh `dotnet test .\Sudoku.Server.Tests\Sudoku.Server.Tests.csproj --no-restore`; `Sudoku.Server/Game/SudokuGridValidator.cs`.

## TC-TOUR-01 — Shortcut tournament 2 người

- **Name/Screen:** Development — 2-player tournament shortcut
- **Description:** Yêu cầu button/route/seed để reset tournament, thêm Player Alpha/Beta, generate bracket và mở trực tiếp.
- **Proposed solution:** Vì codebase chưa có tournament engine, trước tiên cần xác nhận “tournament” có thực sự thuộc phạm vi sản phẩm. Nếu có, thêm route development-only `dev/tournament-2p`, seed idempotent cho Alpha/Beta và chặn route bằng điều kiện Debug/Development.
- **Input/Scenario:** Chạy development build và tìm “Open 2-Player Test Tournament”, route `/dev/tournament-2p` hoặc seed command.
- **Expected result:** Shortcut chỉ xuất hiện ở development/test và mở bracket hai người.
- **Test result 1:** `Failed (code inventory)` — không tìm thấy module, route, model hoặc UI tournament/bracket trong codebase.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** Tìm kiếm source theo `tournament|bracket|Player Alpha|Player Beta` không có implementation.

## TC-TOUR-02 — Bracket đúng 2 player / 1 round / 1 match

- **Name/Screen:** Tournament bracket — Initial generation
- **Description:** Bracket nhỏ nhất phải chứa đúng Player Alpha và Player Beta, một trận Final và một round; không semifinal/BYE/ghost/duplicate/null/undefined.
- **Proposed solution:** Xây bracket generator với nhánh riêng cho đúng hai participant: tạo một Round loại Final và đúng một Match chứa hai player; không pad power-of-two hoặc tạo BYE ở trường hợp này. Đặt unique constraint để không tạo lại match của cùng tournament/round/slot.
- **Input/Scenario:** Generate bracket với `[Player Alpha, Player Beta]`.
- **Expected result:** Players = 2; rounds = 1; matches = 1; Final.player1 = Alpha; Final.player2 = Beta; winner = null; score 0–0; trạng thái Pending/Ready/Not Started.
- **Test result 1:** `Not run` — chức năng tournament chưa tồn tại.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** Chưa có source/database schema tương ứng trong repository.

## TC-TOUR-03 — Player Alpha thắng Final

- **Name/Screen:** Tournament bracket — Alpha wins
- **Description:** Xác minh winner propagation khi Alpha thắng 1–0.
- **Proposed solution:** Thực hiện `SubmitResult` trong transaction: validate score, gán winner Alpha và champion Alpha một lần, đánh dấu Final completed và không gọi logic tạo next-round vì đây đã là Final. Thêm unit/integration test cho kết quả 1–0.
- **Input/Scenario:** Final: Player Alpha = 1, Player Beta = 0; confirm result.
- **Expected result:** Winner và Tournament Champion đều là Player Alpha; Beta bị loại; không sinh thêm match; Alpha chỉ được advance một lần.
- **Test result 1:** `Not run`.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** Chưa có chức năng tournament để chạy.

## TC-TOUR-04 — Reset và Player Beta thắng Final

- **Name/Screen:** Tournament bracket — Reset/Beta wins
- **Description:** Reset phải xóa winner cũ; Beta thắng 1–0 phải trở thành champion duy nhất.
- **Proposed solution:** Tạo thao tác reset transactionally để xóa score, winner, champion và trạng thái completed trước khi nhập kết quả mới. Sau Beta thắng, tính lại winner/champion từ dữ liệu hiện tại thay vì giữ cache của kết quả Alpha.
- **Input/Scenario:** Reset/recreate test tournament; nhập Alpha = 0, Beta = 1; confirm.
- **Expected result:** Winner/Champion = Player Beta; winner Alpha trước đó bị xóa; không duplicate final/match.
- **Test result 1:** `Not run`.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** Chưa có chức năng tournament để chạy.

## TC-TOUR-05 — Persistence và không duplicate bracket

- **Name/Screen:** Tournament bracket — Persistence/idempotency
- **Description:** Rời màn hình, mở lại, refresh/restart hoặc regenerate không được tạo thêm node/match.
- **Proposed solution:** Lưu tournament/round/match trong database và dùng API đọc lại thay vì generate khi mở màn hình. Biến lệnh generate thành idempotent bằng transaction + unique key; nếu bracket đã tồn tại thì trả bracket hiện có.
- **Input/Scenario:** Lưu kết quả Final; rời/mở lại; restart client; regenerate bracket nhiều lần.
- **Expected result:** Giữ players, score, winner, champion; tổng match vẫn bằng 1; không duplicate/null/BYE ngoài ý muốn.
- **Test result 1:** `Not run`.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** Chưa có repository/model/database tournament.

## TC-TOUR-06 — Database validation tournament

- **Name/Screen:** Database — Tournament records
- **Description:** Kiểm tra dữ liệu tournament hai người và tính idempotent của match record.
- **Proposed solution:** Bổ sung entity/migration `Tournament`, `Participant`, `Round`, `TournamentMatch`; thiết lập khóa ngoại và unique index `(TournamentId, RoundNumber, SlotNumber)`. Viết integration test query số record trước/sau reopen và regenerate.
- **Input/Scenario:** Generate tournament; query database trước kết quả, sau Alpha thắng và sau reopen/regenerate.
- **Expected result:** Participants = 2; rounds = 1; matches = 1; Final gồm Alpha/Beta; winner ban đầu null và sau đó đúng Alpha/Beta; không có Match trùng.
- **Test result 1:** `Not run` — `AppDbContext` hiện chỉ phục vụ auth, không có entity tournament.
- **Test result 2:** `Not run`.
- **Test result 3:** `Not run`.
- **Stage:** `Not test yet`
- **Proof:** `Sudoku.Api/Data/AppDbContext.cs` và migrations hiện tại không có bảng Tournament/Bracket/Match tournament.

## TC-REG-01 — Regression build và server test

- **Name/Screen:** Whole application — Regression
- **Description:** Đảm bảo thay đổi bảng Sudoku không phá build mobile hoặc logic server hiện có.
- **Proposed solution:** Thêm pipeline chạy build Windows/Android và toàn bộ test server trên mỗi PR. Trước build phải đóng app đang chạy hoặc xuất build sang thư mục riêng để tránh khóa `.exe`; thêm UI test cho số lượng cell và cấu trúc 3×3.
- **Input/Scenario:** Build target Windows và chạy toàn bộ test project server.
- **Expected result:** 0 compile error; toàn bộ automated tests pass.
- **Test result 1:** `Passed` — mobile Windows build thành công trước khi chạy app.
- **Test result 2:** `Passed` — 42/42 server tests passed.
- **Test result 3:** `Blocked by running executable once` — một lần rebuild bị file `.exe` đang chạy khóa; đây là vấn đề quy trình chạy/build, không phải compile error; tiến trình đã được đóng để build lại.
- **Stage:** `Passed`
- **Proof:** Output build/test trong phiên 2026-09-10; thay đổi chỉ nằm ở `Sudoku.Mobile/SudokuPage.xaml` và `.xaml.cs` cho phần board.

## Bằng chứng bổ sung cần thu thập

- Ảnh Login với backend tắt, timeout, sai mật khẩu và đăng nhập thành công.
- Console/log có URL, method, status code, response body và exception đã phân loại.
- Ảnh hai client cùng một `MatchId` và cùng puzzle 9×9/9 vùng 3×3.
- Video hoặc ảnh trước/sau khi nhập đúng/sai để chứng minh server xác thực nước đi.
- Ảnh bracket Final gồm đúng Player Alpha và Player Beta sau khi chức năng tournament được triển khai.
- Query/database export chứng minh đúng một tournament match và không duplicate sau reopen/regenerate.

## Tổng kết hiện tại

| Nhóm | Passed | Work in progress | Failed | Not test yet |
|---|---:|---:|---:|---:|
| Login/API/TCP | 0 | 1 | 2 | 3 |
| Sudoku board/match | 1 | 4 | 0 | 0 |
| Tournament 2-player | 0 | 0 | 0 | 6 |
| Regression | 1 | 0 | 0 | 0 |

Các mục tournament được ghi là `Not test yet` vì codebase hiện là game Sudoku đối kháng hai người, chưa có tournament/bracket engine. Không được đổi các mục này thành `Passed` cho tới khi chức năng thật sự tồn tại và walkthrough end-to-end đã chạy thành công.

## Kết quả chạy gần nhất — 2026-09-10

- API khởi động thành công tại `http://127.0.0.1:5243`; database migration và development seed hoàn tất.
- `POST /api/auth/login` với mật khẩu sai trả HTTP `401`; tài khoản development hợp lệ trả HTTP `200` và có access/refresh token.
- TCP Game Server lắng nghe tại `0.0.0.0:5000`.
- Mobile Windows build thành công; tiến trình ứng dụng khởi động và phản hồi.
- Automated tests: `58/58 Passed`.
- UI automation trong phiên không liệt kê được cửa sổ native (`apps: []`), nên không có ảnh/click-through và không đánh dấu UI end-to-end là Passed.
- Luồng thực tế của dự án là `Login → Lobby → Quick Match → SudokuPage`; không có route/module Tournament riêng.
- Luật game là hai người giải cùng một đề trên **hai board tiến độ độc lập**, không phải game thay phiên ghi A/B lên một board chung. Vì vậy không có rotation/mirroring, không đổi lượt, và event đối thủ chỉ đồng bộ tiến độ thay vì sao chép giá trị cell.

### Acceptance matrix gần nhất

| Hạng mục | Kết quả | Bằng chứng/Ghi chú |
|---|---|---|
| Login | `FAIL (UI chưa xác minh)` | HTTP hợp lệ đã PASS, nhưng chưa click được app native. |
| Enter Tournament | `FAIL / Not available` | Không có module Tournament; luồng tương đương là Lobby/Quick Match. |
| Connect Player A & Player B | `FAIL (UI chưa xác minh)` | Server/unit flow PASS; chưa chạy hai cửa sổ UI. |
| 9 rows | `PASS` | `SudokuBoardCoordinates.Size == 9`; automated test pass. |
| 9 columns | `PASS` | `SudokuBoardCoordinates.Size == 9`; automated test pass. |
| 81 cells | `PASS` | `CellCount == 81`; mapping/build test pass. |
| Top-left mapping | `PASS` | `(0,0) → 0`. |
| Top-right mapping | `PASS` | `(0,8) → 8`. |
| Center mapping | `PASS` | `(4,4) → 40`. |
| Bottom-left mapping | `PASS` | `(8,0) → 72`. |
| Bottom-right mapping | `PASS` | `(8,8) → 80`. |
| Player A → Player B cell sync | `FAIL / Not applicable` | Hai player có board độc lập theo luật hiện tại; chỉ progress được push. |
| Player B → Player A cell sync | `FAIL / Not applicable` | Hai player có board độc lập theo luật hiện tại; chỉ progress được push. |
| Turn handling | `FAIL / Not applicable` | Sudoku race là simultaneous, không có currentTurn. |
| Occupied-cell protection | `PASS (server/unit)` | Given cell bị khóa; correct cell bị khóa ở UI; duplicate move ID idempotent. |
| Refresh/reconnect | `PASS (server/unit), FAIL (UI chưa xác minh)` | Snapshot khôi phục OwnBoard; chưa restart client qua UI. |
| Duplicate socket events | `PASS (server/unit)` | Cùng MoveId chỉ được áp dụng một lần; listener MatchService được unsubscribe khi rời trang. |
| Visual design unchanged | `PASS for this change set` | Không chỉnh XAML, màu, font, spacing hay kích thước trong lượt sửa Login/logic này. |

# Multiplayer Competition – Two Player Board Verification

Môi trường chạy: API `127.0.0.1:5243`, TCP Game Server `0.0.0.0:5000`, hai client/session logic độc lập với `player-a` và `player-b`. UI native không xuất hiện trong inventory của công cụ tự động hóa, vì vậy kiểm tra hình ảnh/click thật được đánh dấu `BLOCKED`.

Ảnh kiểm chứng hai board được render từ chính `SudokuGenerator` của server với seed `20260910`: [two-player-sudoku-boards.png](../outputs/multiplayer-proof/two-player-sudoku-boards.png). Hai bảng dùng cùng puzzle; số xanh là move riêng của Player A và số đỏ là move riêng của Player B. Đây là ảnh kiểm chứng dữ liệu/layout, không phải screenshot cửa sổ MAUI.

## TC-MP-001 — Two players can join the same match

- **Test Case ID:** TC-MP-001
- **Title:** Hai player tham gia cùng một match
- **Preconditions:** TCP server chạy; hai connection đã handshake bằng hai PlayerId khác nhau.
- **Test Steps:** Player A tạo room; Player B join đúng RoomId; Player A gọi StartMatch.
- **Expected Result:** Hai player nằm trong cùng room/match và nhận cùng MatchId.
- **Actual Result:** Integration test tạo hai `TcpClient`, join cùng room và nhận cùng MatchId thành công.
- **Status:** `PASS`
- **Notes / Evidence:** `NetworkProtocolTests.StartMatch_ReturnsPreparedToRequesterAndPushesPreparedToOpponent`; 59/59 automated tests pass.

## TC-MP-002 — Match starts with exactly two expected players

- **Test Case ID:** TC-MP-002
- **Title:** Match bắt đầu với đúng hai player
- **Preconditions:** Room có player-a và player-b, chưa có active match.
- **Test Steps:** Start match; cả hai gửi PlayerReady; đọc trạng thái match.
- **Expected Result:** Chỉ player-a/player-b thuộc match; không trùng PlayerId; state chuyển Ongoing sau khi cả hai ready.
- **Actual Result:** Server coordinator xác nhận room hai người, reject PlayerId trùng và start đúng một active match.
- **Status:** `PASS`
- **Notes / Evidence:** `MatchCoordinatorTests`; `MatchManager_WithInvalidIdentifiers_IsRejected`.

## TC-MP-003 — Player 1 Sudoku board renders as 9x9

- **Test Case ID:** TC-MP-003
- **Title:** Board Player 1 render đúng 9×9
- **Preconditions:** MatchSession có Puzzle/OwnBoard 81 phần tử; mở SudokuPage.
- **Test Steps:** Khởi tạo trang; đếm outer boxes, inner cells; quan sát UI.
- **Expected Result:** 9 hàng, 9 cột, 81 cell; không overlap/stretch/shift.
- **Actual Result:** Code tạo 9 box × 9 cell tổng cộng 81; build pass. Không quan sát được cửa sổ native để xác minh hình ảnh.
- **Status:** `BLOCKED`
- **Notes / Evidence:** `SudokuPage.BuildSudokuGrid`; ảnh `../outputs/multiplayer-proof/two-player-sudoku-boards.png`; blocker screenshot MAUI thật: Computer Use trả `apps: []`.

## TC-MP-004 — Player 2 Sudoku board state is also valid 9x9

- **Test Case ID:** TC-MP-004
- **Title:** Board state Player 2 đúng 9×9
- **Preconditions:** Player B nhận MatchPrepared từ server.
- **Test Steps:** Đọc Puzzle và OwnBoard trong response dành cho Player B; kiểm tra length và giá trị.
- **Expected Result:** Puzzle/OwnBoard có đúng 81 phần tử và cùng puzzle với Player A.
- **Actual Result:** Hai TCP client nhận Puzzle giống nhau; OwnBoard của mỗi client có 81 phần tử.
- **Status:** `PASS`
- **Notes / Evidence:** `NetworkProtocolTests.StartMatch_ReturnsPreparedToRequesterAndPushesPreparedToOpponent`; ảnh hai board dùng seed server `20260910`.

## TC-MP-005 — Player IDs map to the correct game state

- **Test Case ID:** TC-MP-005
- **Title:** PlayerId ánh xạ đúng board state
- **Preconditions:** Match có PlayerAId và PlayerBId khác nhau.
- **Test Steps:** Player A gửi một move đúng; đọc BoardA, BoardB và snapshot tương ứng.
- **Expected Result:** Move thuộc BoardA; BoardB không bị đổi; opponent lookup trả đúng player còn lại.
- **Actual Result:** BoardA đổi đúng một cell; cùng cell trên BoardB vẫn giữ giá trị puzzle ban đầu.
- **Status:** `PASS`
- **Notes / Evidence:** `MatchCoordinatorTests.PlayerMove_ChangesOnlyOwnBoardAtTheExactCoordinate`.

## TC-MP-006 — Player 1 move updates the correct board

- **Test Case ID:** TC-MP-006
- **Title:** Move Player 1 cập nhật đúng board/tọa độ
- **Preconditions:** Match Ongoing; chọn một editable cell.
- **Test Steps:** Submit move với row/column/value; so sánh toàn bộ board với puzzle trước move.
- **Expected Result:** Chỉ đúng một cell tại row/column được đổi.
- **Actual Result:** Đúng một cell trên BoardA thay đổi; không có row/column swap.
- **Status:** `PASS`
- **Notes / Evidence:** Automated test đếm differences = 1; mapping dùng `row * 9 + column`.

## TC-MP-007 — Player 2 move updates the correct opponent state

- **Test Case ID:** TC-MP-007
- **Title:** Move Player 2 cập nhật đúng state Player 2
- **Preconditions:** Match Ongoing; Player B là thành viên hợp lệ.
- **Test Steps:** Submit move dưới PlayerId B; đọc snapshot B và opponent counters phía A.
- **Expected Result:** Move nằm trên BoardB; BoardA không bị ghi đè; progress event hướng tới A.
- **Actual Result:** Server chọn board qua `match.GetBoard(playerId)` và gửi `OpponentProgressUpdated` cho `GetOpponent(playerId)`; unit state isolation pass.
- **Status:** `PASS`
- **Notes / Evidence:** `MatchManager.SubmitMove`, `MessageDispatcher.OnProgressChanged`, test board isolation.

## TC-MP-008 — Moves synchronize correctly between clients

- **Test Case ID:** TC-MP-008
- **Title:** Đồng bộ move/progress giữa hai client
- **Preconditions:** Hai UI client cùng ở SudokuPage và đã subscribe event.
- **Test Steps:** A nhập đúng/sai; quan sát A và B; sau đó B thực hiện move và quan sát A.
- **Expected Result:** Own board chỉ đổi ở người thực hiện; opponent nhận đúng progress một lần.
- **Actual Result:** Tầng server/protocol có event đúng; SudokuPage đã cập nhật counters trong MatchSession. Chưa quan sát được hai UI client thật.
- **Status:** `BLOCKED`
- **Notes / Evidence:** `MatchService.OpponentProgressUpdated`; blocker là không điều khiển được cửa sổ native.

## TC-MP-009 — Player 1 action does not overwrite Player 2 state

- **Test Case ID:** TC-MP-009
- **Title:** Player 1 không ghi đè board Player 2
- **Preconditions:** Hai board bắt đầu từ cùng puzzle.
- **Test Steps:** A submit move tại editable coordinate; so sánh BoardA và BoardB.
- **Expected Result:** BoardA đổi đúng cell; BoardB không đổi.
- **Actual Result:** Test xác nhận BoardB tại coordinate đó vẫn bằng 0/puzzle ban đầu.
- **Status:** `PASS`
- **Notes / Evidence:** `PlayerMove_ChangesOnlyOwnBoardAtTheExactCoordinate`.

## TC-MP-010 — Refresh/reconnect preserves match board state

- **Test Case ID:** TC-MP-010
- **Title:** Reconnect khôi phục đúng match/board
- **Preconditions:** Player đã handshake, có session token và đã thực hiện move.
- **Test Steps:** Ngắt connection; reconnect bằng session token; gọi GetMatchStatus; mở lại SudokuPage.
- **Expected Result:** Giữ MatchId, OwnBoard, correct/error count và opponent; không reset puzzle.
- **Actual Result:** TCP reconnect và server snapshot tests pass; SudokuPage ưu tiên OwnBoard. Chưa restart UI thật.
- **Status:** `BLOCKED`
- **Notes / Evidence:** `TcpServer_RequiresHandshakeThenSupportsLobbyAndReconnect`, `DuplicateMoveId_IsAppliedOnceAndSnapshotRestoresBoard`; UI blocker `apps: []`.

## TC-MP-011 — Given Sudoku cells cannot be modified

- **Test Case ID:** TC-MP-011
- **Title:** Given cell bị khóa cho cả hai player
- **Preconditions:** Match Ongoing; chọn một puzzle cell khác 0.
- **Test Steps:** A và B lần lượt gửi value 0 vào cùng given coordinate.
- **Expected Result:** Cả hai bị reject `GivenCellLocked`; giá trị hai board không đổi.
- **Actual Result:** Server reject cả hai và giữ nguyên BoardA/BoardB.
- **Status:** `PASS`
- **Notes / Evidence:** `MatchCoordinatorTests.GivenCell_CannotBeModifiedByEitherPlayer`.

## TC-MP-012 — Editable cells modify the correct coordinate

- **Test Case ID:** TC-MP-012
- **Title:** Editable cell dùng đúng coordinate/index
- **Preconditions:** Flat board có 81 cell.
- **Test Steps:** Kiểm tra `(0,0)`, `(0,8)`, `(1,0)`, `(4,4)`, `(8,0)`, `(8,8)` theo hai chiều index/coordinate.
- **Expected Result:** Lần lượt ánh xạ `0, 8, 9, 40, 72, 80`; round-trip không sai lệch.
- **Actual Result:** Tất cả DataRow mapping pass; out-of-range bị reject.
- **Status:** `PASS`
- **Notes / Evidence:** `SudokuBoardCoordinatesTests.CoordinateMapping_RoundTripsCorrectly`.

## TC-MP-013 — Match/game ID references the correct Sudoku board

- **Test Case ID:** TC-MP-013
- **Title:** MatchId tham chiếu đúng puzzle/OwnBoard
- **Preconditions:** Hai player đã nhận MatchPrepared.
- **Test Steps:** So sánh MatchId, RoomId, Puzzle của requester/opponent; submit move bằng MatchId đó.
- **Expected Result:** Cùng MatchId/RoomId/puzzle; move không thể tác động match khác.
- **Actual Result:** TCP integration xác nhận hai response dùng cùng MatchId và puzzle; MatchManager lookup board theo MatchId + PlayerId.
- **Status:** `PASS`
- **Notes / Evidence:** `StartMatch_ReturnsPreparedToRequesterAndPushesPreparedToOpponent`.

## TC-MP-014 — Opponent progress displays correct data

- **Test Case ID:** TC-MP-014
- **Title:** Opponent progress dùng đúng dữ liệu đối thủ
- **Preconditions:** Hai player trong match; đối thủ gửi move đúng/sai.
- **Test Steps:** Nhận `OpponentProgressUpdated`; cập nhật OpponentCorrectCount/OpponentErrorCount; quan sát UI.
- **Expected Result:** State và phần hiển thị tiến độ phản ánh đúng opponent, không dùng own counters.
- **Actual Result:** SudokuPage đã có progress bar/percentage; event cập nhật cả counter và đúng cell trên opponent board. Hai client đã vào cùng match, chờ người dùng tạo move để xác nhận trực quan.
- **Status:** `BLOCKED`
- **Notes / Evidence:** `SudokuPage.OnOpponentProgressUpdated`, `OpponentProgressBar`, protocol `OpponentBoard`.

## TC-MP-015 — Board contains exactly 81 cells

- **Test Case ID:** TC-MP-015
- **Title:** Mỗi Sudoku board có đúng 81 cell
- **Preconditions:** Puzzle/OwnBoard được serialize dạng flat array.
- **Test Steps:** Kiểm tra constant, payload A/B và reject arrays 80/82 phần tử.
- **Expected Result:** Chỉ board length 81 được chấp nhận; không thiếu/thừa row/cell.
- **Actual Result:** CellCount = 81; payload hai player = 81; arrays 80 và 82 bị validation trả false.
- **Status:** `PASS`
- **Notes / Evidence:** `SudokuBoardCoordinatesTests.BoardDimensions_AreNineByNineWithEightyOneCells` và TCP match test.

## TC-UI-001 — Competitive gameplay visual identity

- **Name / Screen:** Sudoku Duel / Competitive gameplay.
- **Description:** Màn chơi dùng cùng hệ pastel tím, cyan, thẻ trắng bo tròn và nút tím của Login/Register.
- **Input / Scenario:** Player A và Player B đăng nhập, Quick Match và mở SudokuPage.
- **Expected Result:** Có opponent progress, hai tab, board là trọng tâm và number pad; không sửa các màn hình authentication.
- **Test Result 1:** XAML source generation thành công.
- **Test Result 2:** Windows build thành công, 0 error.
- **Test Result 3:** Screenshot thật từ MAUI đã được so sánh trực tiếp với Stitch; composition, board và controls khớp gần reference.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.screen.png`; `Sudoku.Mobile/SudokuPage.xaml`.
- **Proposed Solution:** Nếu sai kích thước trên thiết bị thật, chỉ hiệu chỉnh spacing/board size trong SudokuPage.

## TC-UI-002 — Exact 9×9 board and combined highlighting

- **Name / Screen:** Sudoku Duel / Sudoku board.
- **Description:** Board có 9 sub-grid 3×3, đúng 81 cell; hỗ trợ selected, same-number, row, column và block highlight.
- **Input / Scenario:** Chọn một cell có giá trị 5 trên My Board hoặc Opponent.
- **Expected Result:** Cell chọn màu tím; các số 5 khác màu cyan; hàng/cột/block màu lavender; đường 3×3 đậm hơn đường cell.
- **Test Result 1:** `BuildSudokuGrid` tạo 9 box × 9 button.
- **Test Result 2:** Marker hai client báo `cellCount=81`.
- **Test Result 3:** Priority được áp dụng selected → matching number → row/column/block.
- **Stage:** `PASS`
- **Proof:** `Sudoku.Mobile/SudokuPage.xaml.cs`; automated dimension/coordinate tests.
- **Proposed Solution:** Giữ mapping duy nhất qua `SudokuBoardCoordinates.ToIndex/FromIndex` và thứ tự priority trong `ApplyBoardVisuals`.

## TC-UI-003 — Independent read-only opponent board

- **Name / Screen:** Sudoku Duel / My Board and Opponent tabs.
- **Description:** Hai tab dùng chung layout 9×9 nhưng đọc từ hai state hoàn toàn độc lập; Opponent chỉ cho inspect.
- **Input / Scenario:** Chuyển sang Opponent, chọn cell và thử bấm number pad.
- **Expected Result:** Board không reset; không thể gửi move từ Opponent; My Board không bị thay đổi.
- **Test Result 1:** `_myBoard` và `_opponentBoard` là hai mảng riêng.
- **Test Result 2:** Number pad bị disable và handler chặn khi `_showingOpponent=true`.
- **Test Result 3:** Snapshot test xác nhận board A/B không ghi đè nhau.
- **Stage:** `PASS`
- **Proof:** `MatchTimingTests.PlayerStatus_ContainsSeparateCurrentBoardsForBothPlayers`; `SudokuPage.xaml.cs`.
- **Proposed Solution:** Mọi update tiếp theo phải định tuyến theo PlayerId; không dùng một board state chung cho hai tab.

## TC-UI-004 — Real-time opponent progress and board

- **Name / Screen:** Sudoku Duel / Opponent progress.
- **Description:** Move đúng của đối thủ cập nhật phần trăm và đúng cell trên tab Opponent mà không refresh.
- **Input / Scenario:** Player B giải đúng một editable cell trong khi Player A đang xem một trong hai tab.
- **Expected Result:** Progress A tăng; opponent board A cập nhật đúng row/column/value; own board A không đổi.
- **Test Result 1:** Protocol snapshot có `OpponentBoard` đủ 81 phần tử.
- **Test Result 2:** Delta event ghi board khi `Accepted` và `BoardChanged`, gồm cả số người chơi nhập sai.
- **Test Result 3:** Server snapshot/event tests pass; hai client thật đã vào cùng match và probe xác nhận tab Opponent là read-only.
- **Stage:** `PASS`
- **Proof:** 59/59 server tests pass; `MatchManager.GetPlayerStatus`; `SudokuPage.OnOpponentProgressUpdated`.
- **Proposed Solution:** Khi reconnect nạp lại cả OwnBoard/OpponentBoard từ snapshot, sau đó tiếp tục áp dụng event delta.

## TC-UI-005 — My Board tab state preservation

- **Name / Screen:** Sudoku Duel / My Board tab.
- **Description:** My Board giữ nguyên state khi chuyển My Board → Opponent → My Board.
- **Input / Scenario:** Nhập một số hợp lệ, chuyển qua Opponent rồi quay lại.
- **Expected Result:** Giá trị đã nhập vẫn nằm đúng coordinate; board không regenerate.
- **Test Result 1:** Tab chỉ đổi `_showingOpponent`, không gọi generator/load lại puzzle.
- **Test Result 2:** Hai mảng `_myBoard` và `_opponentBoard` tồn tại suốt vòng đời page.
- **Test Result 3:** Development board probe gọi đúng hai tab handler và trả `tabStatePreserved=True`.
- **Stage:** `PASS`
- **Proof:** `SudokuPage.OnMyBoardTabClicked`, `SudokuPage.OnOpponentTabClicked`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`.
- **Proposed Solution:** Người test thực hiện walkthrough trên hai cửa sổ đang mở; nếu state mất, kiểm tra không tái tạo page khi đổi tab.

## TC-UI-006 — Number pad 1–9 and exact-coordinate input

- **Name / Screen:** Sudoku Duel / Number pad.
- **Description:** Card number pad có đúng chín nút riêng từ 1 đến 9 và gửi đúng selected coordinate.
- **Input / Scenario:** Chọn một editable cell rồi bấm từng số 1–9.
- **Expected Result:** Chỉ selected cell được gửi/cập nhật; nút tương ứng chuyển màu tím và các số giống nhau được highlight.
- **Test Result 1:** XAML có chính xác Number1…Number9.
- **Test Result 2:** Handler dùng `_selectedRow`, `_selectedColumn`, value của đúng button.
- **Test Result 3:** Runtime probe đi qua cùng cell/button handler trả `number1Entered=True` đến `number9Entered=True` và `all81HitboxesMapped=True`.
- **Stage:** `PASS`
- **Proof:** `Sudoku.Mobile/SudokuPage.xaml`, `OnNumberClicked`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`.
- **Proposed Solution:** Giữ tất cả input đi qua `SudokuBoardCoordinates`; hoàn thành manual click walkthrough.

## TC-UI-007 — Fixed-cell protection and editable erase

- **Name / Screen:** Sudoku Duel / Board editing.
- **Description:** Given cell không bị ghi đè; player-entered cell có thể xóa trên My Board.
- **Input / Scenario:** Thử nhập vào given cell; nhập đúng vào empty cell; chọn Erase.
- **Expected Result:** Given không đổi; editable cell nhận số rồi có thể trở về 0; Opponent không bị ảnh hưởng.
- **Test Result 1:** UI chặn khi `_originalPuzzle[row,column] != 0`.
- **Test Result 2:** Server test `GivenCell_CannotBeModifiedByEitherPlayer` pass.
- **Test Result 3:** Server `ApplyMove(value=0)` giảm correct count và phát delta board.
- **Stage:** `PASS`
- **Proof:** `SudokuPage.OnNumberClicked`, `OnEraseClicked`; server tests 59/59.
- **Proposed Solution:** Không dùng trạng thái `IsEnabled=false` cho given cell vì người chơi vẫn cần select/highlight; chặn tại input handler/server.

## TC-UI-008 — Selected, row, column, block and same-number highlights

- **Name / Screen:** Sudoku Duel / Cell highlights.
- **Description:** Tất cả highlight hiện đồng thời với priority đúng như Stitch.
- **Input / Scenario:** Chọn một cell đã có số.
- **Expected Result:** Selected có nền tím nhạt + outline tím; số giống nhau cyan; row/column/block lavender; normal cell trắng.
- **Test Result 1:** Screenshot runtime cho thấy selected outline, row/column/block và matching-number cyan.
- **Test Result 2:** `ApplyBoardVisuals` áp priority related → matching → selected.
- **Test Result 3:** Text given đậm màu tối, entered number màu tím.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.screen.png`; `SudokuPage.ApplyBoardVisuals`.
- **Proposed Solution:** Không thay thứ tự các nhánh visual state; chỉ hiệu chỉnh token màu nếu reference thay đổi.

## TC-UI-009 — Opponent tab read-only

- **Name / Screen:** Sudoku Duel / Opponent tab.
- **Description:** Tab đối thủ dùng cùng board component nhưng vô hiệu hóa mọi control thay đổi state.
- **Input / Scenario:** Mở Opponent và thử number pad, Erase, Hint; vẫn select cell để inspect.
- **Expected Result:** Board giữ cùng kích thước; banner read-only hiện; input controls mờ/disabled; cell selection highlight vẫn hoạt động.
- **Test Result 1:** Handler number/erase đều chặn khi `_showingOpponent`.
- **Test Result 2:** Number pad, Erase và Hint disabled/opacity 40%.
- **Test Result 3:** Runtime probe chuyển tab qua đúng handler, thử nhập và trả `opponentReadOnly=True`, `tabStatePreserved=True`.
- **Stage:** `PASS`
- **Proof:** `SudokuPage.RefreshScreen`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`.
- **Proposed Solution:** Manual verify tab Opponent trên Player A/B; giữ read-only guard cả UI và handler.

## TC-UI-010 — Responsive square board and Stitch comparison

- **Name / Screen:** Sudoku Duel / Responsive layout.
- **Description:** Content co theo viewport gần 390–420px; board luôn vuông, tối đa 370×370 như Stitch.
- **Input / Scenario:** Render Windows viewport rộng và thay đổi page width gần kích thước mobile.
- **Expected Result:** Board Width=Height; cell đều; content tối đa 408px hữu dụng; không stretch dọc/ngang.
- **Test Result 1:** Screenshot thật cho board khoảng 370×370, đúng 9×9.
- **Test Result 2:** `OnPageSizeChanged` đặt Width/Height/Min/Max cùng một giá trị.
- **Test Result 3:** So với `screen.png`: hierarchy, positions, palette, tabs, board, controls và number pad rất gần; Windows còn title bar đen và font là Open Sans thay vì Fredoka/Nunito.
- **Stage:** `PASS`
- **Proof:** Stitch `screen.png`; `%TEMP%\sudoku-dev-launch\test_player_a.screen.png`.
- **Proposed Solution:** Nếu cần pixel parity tuyệt đối, thêm font Fredoka/Nunito có license vào MAUI Resources/Fonts và kiểm tra tiếp trên Android portrait.

## TC-UI-011 — Không mất đường gạch giữa các ô Sudoku

- **Name / Screen:** Sudoku Duel / My Board và Opponent Board — UI/UX grid lines.
- **Description:** Kiểm tra lỗi một số cạnh ô bị mất hoặc hiển thị không đồng nhất khi render board, đổi highlight hay chuyển giữa hai tab. Board phải thể hiện rõ cấu trúc 9×9 và chín vùng 3×3.
- **Input / Scenario:** Mở trận 1v1 bằng Player A và Player B; quan sát hai board; chọn lần lượt cell ở góc, cạnh, giữa board và sát ranh giới block 3×3; chuyển My Board → Opponent Board → My Board.
- **Expected Result:** Cả 81 ô luôn có đủ bốn cạnh; đường cell mảnh và liên tục; đường phân chia 3×3 cùng outer border màu tím, dày hơn; selected/row/column/block/same-number highlight không che hoặc làm đứt đường gạch; hai board có cùng cấu trúc hình học.
- **Test Result 1:** Kiểm tra ảnh runtime Player A: không còn cạnh cell bị mất; đường 3×3 và outer border hiển thị liên tục.
- **Test Result 2:** Kiểm tra ảnh runtime Player B: cấu trúc 9×9 giống Player A và không có đoạn line bị thiếu.
- **Test Result 3:** Runtime board probe trả `all81CellsHaveBorders=True`, `all81HitboxesMapped=True` và `tabStatePreserved=True`.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.screen.png`; `%TEMP%\sudoku-dev-launch\test_player_b.screen.png`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`; `Sudoku.Mobile/SudokuPage.xaml.cs`.
- **Proposed Solution:** Bọc từng cell Button bằng một `Border` riêng có stroke `0.75`, đặt `RowSpacing/ColumnSpacing=0` trong từng block 3×3, giữ separator tím 3px ở grid cha và dùng Z-index cho selected border. Mọi trạng thái highlight chỉ đổi nền cell, không được thay thế hoặc phủ lên border.

## TC-BRD-001 — Continuous 9×9 grid boundaries

- **Name / Screen:** Sudoku Duel / My Board và Opponent Board.
- **Description:** Xác nhận đủ 81 cell, mỗi cell có bốn cạnh liên tục; đường 3×3 và viền ngoài rõ hơn đường cell; highlight không che mất đường.
- **Input / Scenario:** Mở hai client trong cùng trận, quan sát toàn bộ board và chọn lần lượt tất cả 81 cell.
- **Expected Result:** Không có đoạn line bị thiếu ở bất kỳ hàng/cột/block nào; board đúng 9×9 gồm chín block 3×3.
- **Test Result 1:** Runtime probe trả `all81CellsHaveBorders=True` và `all81HitboxesMapped=True`.
- **Test Result 2:** Ảnh thật Player A và Player B cho thấy đường mảnh từng cell, đường tím 3×3 và outer border liên tục.
- **Test Result 3:** MAUI Windows build thành công, không có compile error.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.screen.png`; `%TEMP%\sudoku-dev-launch\test_player_b.screen.png`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`.
- **Proposed Solution:** Không dùng `Grid.RowSpacing/ColumnSpacing` để giả lập line. Bọc từng Button bằng `Border` stroke 0.75, giữ separator tím 3px ở cấp block/board và đưa selected border lên trên bằng Z-index.

## TC-BRD-002 — Number input 1–9 writes immediately

- **Name / Screen:** Sudoku Duel / My Board number pad.
- **Description:** Sửa lỗi bấm số nhưng selected cell không hiển thị, đặc biệt khi số khác đáp án Sudoku.
- **Input / Scenario:** Chọn một ô trống editable rồi nhập lần lượt 1, 2, 3, 4, 5, 6, 7, 8, 9 vào cùng ô.
- **Expected Result:** Mỗi lần bấm, số xuất hiện ngay đúng cell; player-entered value có thể được thay bằng số khác; server vẫn đánh dấu đúng/sai và đếm error.
- **Test Result 1:** Probe trả `number1Entered=True` đến `number9Entered=True`.
- **Test Result 2:** Probe tổng hợp trả `numbers1To9Entered=True`; Button text và `_myBoard[row,column]` cùng giá trị sau mỗi lần nhập.
- **Test Result 3:** 59/59 server tests pass, gồm snapshot lưu số nhập sai trước khi người chơi sửa lại.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`; `Sudoku.Mobile/SudokuPage.xaml.cs`; `Sudoku.Server/Game/MatchManager.cs`; `Sudoku.Server.Tests/MatchCoordinatorTests.cs`.
- **Proposed Solution:** Cập nhật local state và render trước khi await network; server chấp nhận/lưu mọi giá trị 1–9 ở editable cell, dùng `IsCorrect` để chấm điểm thay vì reject input sai; chỉ rollback khi request thật sự bị reject hoặc mất kết nối.

## TC-BRD-003 — Given lock and Erase flow

- **Name / Screen:** Sudoku Duel / Cell editing controls.
- **Description:** Chỉ ô given từ puzzle gốc bị khóa; ô do người chơi nhập luôn sửa/xóa được.
- **Input / Scenario:** Thử ghi đè một given cell, sau đó nhập một số vào empty cell rồi bấm Erase.
- **Expected Result:** Given không đổi; editable cell nhận số rồi trở về rỗng sau Erase; state client/server đồng bộ.
- **Test Result 1:** Probe trả `originalGivenLocked=True`.
- **Test Result 2:** Probe trả `eraseWorked=True` và text cell trở về rỗng.
- **Test Result 3:** Server tests xác nhận given bị reject và clear move cập nhật correct count đúng.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`; `EnterNumberAsync`; `EraseSelectedCellAsync`; server tests 59/59.
- **Proposed Solution:** Quyết định lock chỉ bằng `_originalPuzzle[row,column] != 0`, không dùng giá trị hiện tại của `_myBoard`; gửi value `0` cho Erase và cập nhật lại local flat board.

## TC-BRD-004 — Independent opponent state and tab preservation

- **Name / Screen:** Sudoku Duel / My Board ↔ Opponent tabs.
- **Description:** Đảm bảo hai board độc lập, tab Opponent chỉ đọc và đổi tab không reset dữ liệu.
- **Input / Scenario:** Nhập/xóa trên My Board, chuyển sang Opponent, thử nhập số, rồi quay lại My Board.
- **Expected Result:** Opponent không nhận input trực tiếp; own board không đổi; selected/highlight và state vẫn hợp lệ sau khi quay lại.
- **Test Result 1:** Hai client đăng nhập riêng vào cùng MatchId `68db2a80-5ad1-4fb0-86e2-b68c862d77e1`, mỗi payload có `cellCount=81`.
- **Test Result 2:** Probe trả `opponentReadOnly=True`.
- **Test Result 3:** Probe trả `tabStatePreserved=True`; opponent delta chấp nhận mọi `Accepted && BoardChanged`.
- **Stage:** `PASS`
- **Proof:** `%TEMP%\sudoku-dev-launch\test_player_a.ready.txt`; `%TEMP%\sudoku-dev-launch\test_player_b.ready.txt`; `%TEMP%\sudoku-dev-launch\test_player_a.board-probe.txt`.
- **Proposed Solution:** Duy trì `_myBoard` và `_opponentBoard` riêng; chặn number/erase khi `_showingOpponent`; chỉ cập nhật opponent state từ snapshot/delta gắn đúng PlayerId.

## TC-BRD-005 — Hint behavior remains unchanged

- **Name / Screen:** Sudoku Duel / Hint action.
- **Description:** Bản sửa board không thay đổi rule Hint hiện có của chế độ 1v1 ranked.
- **Input / Scenario:** Bấm Hint trên My Board trong trận competitive; chuyển Opponent và quan sát control Hint.
- **Expected Result:** My Board hiển thị thông báo Hint bị vô hiệu trong competitive duel; Opponent không cho thao tác Hint; không tự ghi sai coordinate.
- **Test Result 1:** `OnHintClicked` vẫn giữ thông báo rule competitive, không sửa puzzle hoặc board state.
- **Test Result 2:** `RefreshScreen` disable/mờ Hint khi đang xem Opponent.
- **Test Result 3:** Chưa click được dialog native bằng CUA vì môi trường không expose MAUI app (`apps: []`).
- **Stage:** `NOT TESTED YET`
- **Proof:** `Sudoku.Mobile/SudokuPage.xaml.cs`; CUA inventory `apps: []`.
- **Proposed Solution:** Giữ nguyên behavior Hint hiện tại; thực hiện một manual click để xác nhận dialog. Nếu sau này Hint được phép ghi số, bắt buộc tái sử dụng selected row/column và cùng state-update path với number input.

---

## TC-011 — Create Room chỉ mở prompt mặc định

**Test Case ID:** TC-011  
**Title:** Create Room không dùng màn hình thiết kế đã duyệt  
**Area / Feature:** Create Room / UI và Navigation  
**Severity:** High  
**Status:** Fixed

### Preconditions

- Player đã đăng nhập và đang ở Lobby.

### Steps to Reproduce

1. Bấm Create Room trong multiplayer Lobby.
2. Quan sát UI được mở.

### Expected Result

Mở modal đúng `createroom.png`, có Room Name, Easy/Medium/Hard, Fair Play, Cancel, Close và Create Room.

### Actual Result

Code cũ chỉ gọi `DisplayPromptAsync`, không có difficulty hoặc approved layout.

### Root Cause

Create Room chưa có page/route riêng.

### Fix

Tạo `CreateRoomPage`, đăng ký Shell route và đổi Lobby handler sang navigation hiện có.

### Retest Result

PASS — ảnh runtime khớp reference và page mở từ Lobby.

### Related Files

- `Sudoku.Mobile/CreateRoomPage.xaml`
- `Sudoku.Mobile/CreateRoomPage.xaml.cs`
- `Sudoku.Mobile/views/LobbyPage.xaml.cs`
- `Sudoku.Mobile/AppShell.xaml.cs`

---

## TC-012 — Room không lưu difficulty đã chọn

**Test Case ID:** TC-012  
**Title:** Difficulty bị hardcode ở lúc Start Match  
**Area / Feature:** Create Room / Room state  
**Severity:** High  
**Status:** Fixed

### Preconditions

- Host tạo room và chọn một difficulty.

### Steps to Reproduce

1. Chọn Easy/Medium/Hard.
2. Tạo room.
3. Đọc room DTO và bắt đầu match.

### Expected Result

Room lưu đúng một difficulty và cả hai player dùng difficulty đó.

### Actual Result

`CreateRoomRequest` chỉ có RoomName; Quick Match/Start Match dùng difficulty hardcode.

### Root Cause

Room model và protocol thiếu `Difficulty`.

### Fix

Thêm `Difficulty` vào request, DTO, shared Room, mobile LobbyRoom và dùng `room.Difficulty` khi generate puzzle.

### Retest Result

PASS — protocol test xác nhận room Easy và match dùng cùng room difficulty.

### Related Files

- `Sudoku.Shared/Models/Room.cs`
- `Sudoku.Shared/Network/ProtocolContracts.cs`
- `Sudoku.Server/Network/MessageDispatcher.cs`
- `Sudoku.Server/Game/MatchCoordinator.cs`
- `Sudoku.Mobile/Services/LobbyService.cs`

---

## TC-013 — Hai player nhận cùng Sudoku puzzle

**Test Case ID:** TC-013  
**Title:** Match chỉ có một OriginalPuzzle/SolutionGrid dùng chung  
**Area / Feature:** Duel / Puzzle assignment  
**Severity:** Critical  
**Status:** Fixed

### Preconditions

- Room đủ hai player và bắt đầu Duel.

### Steps to Reproduce

1. Start match từ room.
2. Đọc Puzzle của Player A và Player B.
3. So sánh đủ 81 giá trị serialized.

### Expected Result

Hai puzzle cùng difficulty nhưng clue layout khác nhau.

### Actual Result

Match cũ tạo đúng một GeneratedSudoku và dùng cho Board A lẫn Board B.

### Root Cause

Match model chỉ có một `OriginalPuzzle` và `SolutionGrid`.

### Fix

Generate puzzle B riêng, regenerate nếu 81 giá trị trùng; lưu `OriginalPuzzleB/SolutionGridB`; snapshot và move validation chọn puzzle/solution theo player.

### Retest Result

PASS — TCP integration so sánh actual arrays bằng `Enumerable.SequenceEqual` và xác nhận `false`; 60/60 tests pass.

### Related Files

- `Sudoku.Server/Game/MatchCoordinator.cs`
- `Sudoku.Server/Game/MatchModels.cs`
- `Sudoku.Server/Game/MatchManager.cs`
- `Sudoku.Server.Tests/NetworkProtocolTests.cs`

---

## TC-014 — Regression test còn kỳ vọng hai puzzle giống nhau

**Test Case ID:** TC-014  
**Title:** Network protocol test fail sau khi áp dụng Fair Play  
**Area / Feature:** Automated tests / Duel protocol  
**Severity:** Medium  
**Status:** Fixed

### Preconditions

- Server đã trả puzzle riêng theo player.

### Steps to Reproduce

1. Chạy 60 server tests sau thay đổi.
2. Quan sát `StartMatch_ReturnsPreparedToRequesterAndPushesPreparedToOpponent`.

### Expected Result

Test phản ánh rule mới: cùng MatchId, khác puzzle.

### Actual Result

1 test FAIL vì assertion cũ dùng `CollectionAssert.AreEqual` cho hai puzzle.

### Root Cause

Test cũ mã hóa behavior chung puzzle đã bị thay thế.

### Fix

Đổi assertion sang kiểm tra room difficulty và hai mảng puzzle không `SequenceEqual`.

### Retest Result

PASS — 60 passed, 0 failed.

### Related Files

- `Sudoku.Server.Tests/NetworkProtocolTests.cs`

---

# Sudoku Duel Result Screen — Bug History

## TC-001 — Thiếu file `defeat.png` theo tên được chỉ định

**Test Case ID:** TC-001  
**Title:** Thư mục Defeat không có file `defeat.png`  
**Area / Feature:** Duel Result / Design assets  
**Severity:** Low  
**Status:** Fixed

### Preconditions

- Có thư mục `sudoku_duel_defeat_result_screen` từ thiết kế đã duyệt.

### Steps to Reproduce

1. Mở thư mục thiết kế Defeat.
2. Tìm `code.html` và `defeat.png` như đặc tả.

### Expected Result

Thư mục có đúng `code.html` và `defeat.png`.

### Actual Result

Ảnh tồn tại với tên `Screenshot 2026-09-10 201939.png`; không có `defeat.png`.

### Root Cause

Asset được export với tên ảnh chụp mặc định thay vì tên giao nhận trong đặc tả.

### Fix

Tạo `defeat.png` từ chính ảnh đã duyệt; SHA-256 của hai file giống nhau nên không thay đổi pixel thiết kế.

### Retest Result

PASS

### Related Files

- `../sudoku_duel_defeat_result_screen/defeat.png`
- `../sudoku_duel_defeat_result_screen/Screenshot 2026-09-10 201939.png`

---

## TC-002 — Match kết thúc nhưng không tự mở Result screen

**Test Case ID:** TC-002  
**Title:** `SudokuPage` không xử lý sự kiện `MatchFinished`  
**Area / Feature:** Duel Result / Realtime navigation  
**Severity:** High  
**Status:** Fixed

### Preconditions

- Player A và Player B đang ở cùng một Duel đang chạy.
- Server xác định trận đã kết thúc.

### Steps to Reproduce

1. Cho một player hoàn thành Sudoku.
2. Server phát `MatchFinished` tới hai client.
3. Quan sát trang hiện tại của mỗi client.

### Expected Result

Winner tự động mở Victory; loser tự động mở Defeat mà không refresh.

### Actual Result

`MatchService` có phát `MatchFinished`, nhưng `SudokuPage` không subscribe nên không có chuyển trang.

### Root Cause

Gameplay page chỉ subscribe `MatchUpdated` và `OpponentProgressUpdated`.

### Fix

Subscribe `MatchFinished`, xử lý cả final state từ `MatchUpdated`, thêm guard `_resultNavigationStarted`, rồi route theo authoritative `WinnerPlayerId`.

### Retest Result

PASS — hai trận thật đều tự chuyển đồng thời trên cả hai client.

### Related Files

- `Sudoku.Mobile/SudokuPage.xaml.cs`
- `Sudoku.Mobile/Services/MatchService.cs`
- `Sudoku.Mobile/AppShell.xaml.cs`

---

## TC-003 — Test/build fail vì binary bị server đang chạy khóa

**Test Case ID:** TC-003  
**Title:** `Sudoku.Shared.dll` bị khóa trong lần build đầu  
**Area / Feature:** Build / Test environment  
**Severity:** Low  
**Status:** Fixed

### Preconditions

- `Sudoku.Server.exe` cũ đang chạy từ thư mục `bin/Debug`.

### Steps to Reproduce

1. Thay đổi shared match contract.
2. Chạy `dotnet test` khi server cũ còn hoạt động.

### Expected Result

Build dependency và test chạy thành công.

### Actual Result

MSBuild báo `MSB3027/MSB3021`, không copy được `Sudoku.Shared.dll` vì PID server đang khóa file.

### Root Cause

Runtime test cũ chưa được đóng trước khi rebuild shared assembly.

### Fix

Dừng đúng các PID test cũ, build lại server/mobile rồi chạy lại test.

### Retest Result

PASS — 60/60 tests pass; server và Windows client build 0 error.

### Related Files

- `Sudoku.Shared/Models/MatchContracts.cs`
- `Sudoku.Server/Sudoku.Server.csproj`
- `Sudoku.Mobile/Sudoku.Mobile.csproj`

---

## TC-004 — Result response thiếu thời điểm kết thúc authoritative

**Test Case ID:** TC-004  
**Title:** Không thể đóng băng Completion Time chính xác từ server  
**Area / Feature:** Duel Result / Completion time  
**Severity:** Medium  
**Status:** Fixed

### Preconditions

- Match đã có `StartedAtUtc` và server đã tạo `MatchResult.FinishedAtUtc`.

### Steps to Reproduce

1. Hoàn tất trận trước deadline.
2. Đọc `MatchStatusResponse` trên client.
3. Tính thời gian hiển thị Result.

### Expected Result

Client nhận được thời điểm bắt đầu và kết thúc authoritative để hiển thị thời lượng cố định.

### Actual Result

`MatchResult` nội bộ có `FinishedAtUtc` nhưng `MatchStatusResponse` không gửi field này.

### Root Cause

Thiếu mapping trường kết thúc trong shared contract và `GetPlayerStatus`.

### Fix

Thêm `FinishedAtUtc` vào contract, map từ server result, tính `FinishedAtUtc - StartedAtUtc` đúng một lần và không tạo timer mới trên Result page.

### Retest Result

PASS — hai client cùng match nhận cùng completion time; unit test xác nhận frozen duration `04:32`.

### Related Files

- `Sudoku.Shared/Models/MatchContracts.cs`
- `Sudoku.Server/Game/MatchManager.cs`
- `Sudoku.Server.Tests/MatchTimingTests.cs`
- `Sudoku.Mobile/Models/DuelResultSession.cs`

---

## TC-005 — Result state không khôi phục sau khi restart app

**Test Case ID:** TC-005  
**Title:** Refresh/restart làm mất Defeat/Victory state  
**Area / Feature:** Duel Result / Persistence  
**Severity:** Medium  
**Status:** Fixed

### Preconditions

- Match đã kết thúc và client đang ở Result screen.

### Steps to Reproduce

1. Đóng client sau khi nhận Result.
2. Khởi động lại với cùng tài khoản.
3. Mở lại final result.

### Expected Result

Giữ nguyên result, MatchId, opponent và completion time; không tạo match mới.

### Actual Result

Lần triển khai đầu dùng state trong process/Preferences và lookup không khôi phục được Result trong Windows unpackaged flow.

### Root Cause

Result chưa có persisted JSON độc lập theo player; lookup trước TCP cũng chưa nhận `playerId` đã resolve từ login.

### Fix

Lưu JSON theo SHA-256 của `playerId` trong `FileSystem.AppDataDirectory`; `GetCurrent(playerId)` khôi phục trước khi kết nối gameplay.

### Retest Result

PASS — restart Player A vẫn mở Defeat của MatchId `2ae1777c-6baa-4eb0-825a-72a0fe060829`, opponent `test_player_b`, time `00:04.091`.

### Related Files

- `Sudoku.Mobile/Models/DuelResultSession.cs`
- `Sudoku.Mobile/LoginPage.xaml.cs`

---

## TC-006 — Active TCP session cũ chặn việc xem lại Result

**Test Case ID:** TC-006  
**Title:** Restart sớm báo `Player already has an active session`  
**Area / Feature:** Duel Result / Multiplayer reconnect  
**Severity:** Medium  
**Status:** Fixed

### Preconditions

- Client bị đóng cưỡng bức ngay sau trận; server chưa giải phóng active session cũ.

### Steps to Reproduce

1. Đóng Player A ngay sau Result.
2. Mở lại Player A ngay lập tức.
3. Login trong khi server còn session cũ.

### Expected Result

Final Result đã lưu vẫn xem được, không phụ thuộc gameplay TCP session.

### Actual Result

TCP trả `InvalidPayload: Player already has an active session`, chặn navigation tới Result.

### Root Cause

Luồng restore kết nối TCP trước khi đọc final result đã lưu.

### Fix

Khi restore final result, tải state và điều hướng trước bước TCP; không disconnect global state trong flow bình thường.

### Retest Result

PASS — Result khôi phục được cả khi Game Server không chạy.

### Related Files

- `Sudoku.Mobile/LoginPage.xaml.cs`
- `Sudoku.Mobile/Models/DuelResultSession.cs`
- `Sudoku.Mobile/Network/TcpGameClient.cs`

---

## TC-007 — Font Result screen không khớp PNG đã duyệt

**Test Case ID:** TC-007  
**Title:** Bản render đầu dùng Open Sans thay cho Fredoka/Nunito  
**Area / Feature:** Duel Result / Visual fidelity  
**Severity:** Low  
**Status:** Fixed

### Preconditions

- Project ban đầu chỉ chứa Open Sans.

### Steps to Reproduce

1. Render Victory và Defeat lần đầu.
2. So title, opponent, time và Home với PNG/reference HTML.

### Expected Result

Body dùng Nunito; title, opponent, time và Home dùng Fredoka như `code.html`.

### Actual Result

Màu và layout đúng nhưng hình dạng chữ khác rõ vì toàn bộ đang dùng Open Sans.

### Root Cause

Thiếu hai font được khai báo trong approved HTML.

### Fix

Thêm Fredoka/Nunito variable fonts và đăng ký alias trong `MauiProgram`; áp dụng đúng vai trò typography của reference.

### Retest Result

PASS — ảnh runtime lần hai khớp typography reference gần nhất trong MAUI.

### Related Files

- `Sudoku.Mobile/Resources/Fonts/Fredoka-Variable.ttf`
- `Sudoku.Mobile/Resources/Fonts/Nunito-Variable.ttf`
- `Sudoku.Mobile/MauiProgram.cs`
- `Sudoku.Mobile/VictoryResultPage.xaml`
- `Sudoku.Mobile/DefeatResultPage.xaml`

---

## TC-008 — Restore Result đi vòng qua Lobby gây lỗi TCP thừa

**Test Case ID:** TC-008  
**Title:** Lobby load rooms khi restore offline  
**Area / Feature:** Duel Result / Navigation  
**Severity:** Low  
**Status:** Fixed

### Preconditions

- Có persisted Result; Game Server không chạy.

### Steps to Reproduce

1. Login lại để restore Result.
2. Luồng cũ điều hướng `//LobbyPage` trước khi push Result route.

### Expected Result

Mở Result trực tiếp, không gọi API/TCP gameplay không cần thiết.

### Actual Result

Lobby ghi `Load Rooms Error: TCP connection is not available` trước khi Result xuất hiện.

### Root Cause

Restore dùng Lobby làm route trung gian.

### Fix

Push trực tiếp route Victory/Defeat từ Login; Home vẫn dùng route chuẩn `//LobbyPage`.

### Retest Result

PASS

### Related Files

- `Sudoku.Mobile/LoginPage.xaml.cs`
- `Sudoku.Mobile/AppShell.xaml.cs`

---

## TC-009 — Persisted file của Player B không xuất hiện trong một lượt kiểm tra

**Test Case ID:** TC-009  
**Title:** Chỉ thấy JSON Result của Player A sau một lần reverse test  
**Area / Feature:** Duel Result / Per-player persistence  
**Severity:** Medium  
**Status:** Cannot Reproduce

### Preconditions

- Player B thắng và cả hai Result screen đã xuất hiện.

### Steps to Reproduce

1. Đóng hai client sau reverse test đầu tiên.
2. Kiểm tra thư mục `duel-results`.
3. Tìm JSON được hash riêng cho Player A và Player B.

### Expected Result

Có hai file độc lập, mỗi file chứa cùng MatchId nhưng result/current player khác nhau.

### Actual Result

Trong một lượt kiểm tra chỉ tìm thấy file Player A; marker runtime của Player B vẫn xác nhận Victory đúng.

### Root Cause

Không xác định chắc chắn; khả năng lượt kiểm tra dùng process/binary trước lần build persistence cuối.

### Fix

Không áp dụng sửa logic mới; chạy lại toàn bộ reverse match bằng cùng binary cuối và kiểm tra file ngay khi cả hai Result đang mở.

### Retest Result

PASS — có hai JSON khác tên hash; cùng MatchId `91d23791-ef8f-41a4-b25f-9062ed7e047b`, A=`IsVictory:false`, B=`IsVictory:true`.

### Related Files

- `Sudoku.Mobile/Models/DuelResultSession.cs`
- `%LOCALAPPDATA%/User Name/com.companyname.sudoku.mobile/Data/duel-results/`

---

## TC-010 — Clean Windows build build còn nullable warnings

**Test Case ID:** TC-010  
**Title:** Build thành công nhưng phát sinh cảnh báo chữ ký event handler  
**Area / Feature:** Build quality / Existing UI pages  
**Severity:** Low  
**Status:** Needs Investigation

### Preconditions

- Build MAUI Windows target từ source hiện tại.

### Steps to Reproduce

1. Chạy `dotnet build Sudoku.Mobile/Sudoku.Mobile.csproj -f net10.0-windows10.0.19041.0 --no-restore`.
2. Quan sát output source-generated XAML.

### Expected Result

Build 0 error và 0 warning.

### Actual Result

Build 0 error nhưng báo nullable `CS8622`, chủ yếu từ event handlers có `object sender` trong Login/Register và hai tab Sudoku.

### Root Cause

Nullability của tham số `sender` không khớp `EventHandler` nullable annotations của MAUI.

### Fix

Chưa sửa vì các handler authentication là code có sẵn, nằm ngoài phạm vi Result screen. Cách xử lý đề xuất: đổi chữ ký phù hợp sang `object? sender` và clean-build xác nhận lại.

### Retest Result

FAIL — warning còn tồn tại; không có compile error và không ảnh hưởng walkthrough Result.

### Related Files

- `Sudoku.Mobile/LoginPage.xaml.cs`
- `Sudoku.Mobile/RegisterPage.xaml.cs`
- `Sudoku.Mobile/SudokuPage.xaml.cs`

---
