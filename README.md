# TOEIC Reading Practice

Ứng dụng luyện TOEIC Reading gồm web quản trị, API ASP.NET Core, ứng dụng Flutter
dành cho học viên và cơ sở dữ liệu SQL Server. Học viên dùng ứng dụng không cần
đăng nhập; chỉ trang quản trị yêu cầu tài khoản admin.

## Yêu cầu

- Windows 10/11
- .NET SDK có thể build dự án .NET 9 (khuyến nghị .NET 9 SDK)
- Flutter SDK và Android Studio/emulator (nếu chạy ứng dụng Android)
- SQL Server đang chạy, với Windows Authentication được bật
- Quyền tạo database và bảng trên SQL Server khi chạy lần đầu

Mặc định ứng dụng kết nối tới `localhost`, database `toeic_reading`, sử dụng Windows
Authentication. Nếu SQL Server của bạn dùng instance hoặc thông tin kết nối khác,
xem phần [Cấu hình SQL Server](#cấu-hình-sql-server).

## Chạy nhanh

Các lệnh dưới đây chạy trong PowerShell tại thư mục dự án, ví dụ `D:\BMTTNC\topic`.
Trước tiên bảo đảm SQL Server đang chạy và máy đã cài .NET SDK, Flutter SDK.

**Cửa sổ PowerShell 1 — khởi tạo database rồi chạy web admin/API:**

```powershell
dotnet dev-certs https --trust
dotnet run --project Backend\aspnet-core\src\ToeicExam.Migrator -- -q
dotnet run --project Backend\aspnet-core\src\ToeicExam.Web.Host
```

Mở **https://localhost:44311/** để vào trang quản trị. Đăng nhập bằng tài khoản
admin demo được hướng dẫn bên dưới. Giữ cửa sổ này mở trong khi sử dụng ứng dụng.

**Cửa sổ PowerShell 2 — chạy app học viên trên Android emulator:**

```powershell
cd Frontend
flutter pub get
flutter run
```

Khởi động emulator trong Android Studio trước khi chạy `flutter run`. App mặc định
kết nối API qua `http://10.0.2.2:5055/api`. Học viên không cần tài khoản đăng nhập.

## Chạy ứng dụng lần đầu

Mở PowerShell tại thư mục gốc của dự án, nơi có file `README.md`.

### Cài .NET 9 trên Windows

Dự án hiện target .NET 9. Trên trang chính thức
[Download .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0), tải **.NET 9 SDK
x64 Installer** và chạy file `.exe`. Cài **SDK**, không chỉ cài Runtime, vì lệnh
`dotnet run` cần SDK để build dự án. SDK đã bao gồm runtime cần thiết để chạy ứng
dụng ASP.NET Core. Không cần gỡ .NET 10; có thể cài nhiều phiên bản .NET song song.

Sau khi cài đặt:

1. Đóng tất cả cửa sổ PowerShell/Terminal đang mở, rồi mở lại.
2. Chạy các lệnh sau:

   ```powershell
   dotnet --list-sdks
   dotnet --list-runtimes
   ```

3. Xác nhận kết quả có phiên bản `9.x`, gồm `Microsoft.NETCore.App 9.x` và
   `Microsoft.AspNetCore.App 9.x`.
4. Tiếp tục chạy Migrator và Web Host theo các bước bên dưới.

> **Lưu ý:** .NET 9 đã hết thời hạn hỗ trợ từ ngày 12/05/2026. Nên chỉ cài để chạy
> dự án này theo đúng target hiện tại; với dự án triển khai lâu dài, nên nâng cấp
> target framework lên phiên bản .NET còn được hỗ trợ.

### Nếu máy chỉ có .NET 10

Dự án target .NET 9. Nếu `dotnet --list-runtimes` không hiển thị
`Microsoft.NETCore.App 9.x`, có thể thử cho .NET dùng runtime mới hơn bằng cách
đặt biến môi trường trong **cùng cửa sổ PowerShell** trước khi chạy Migrator và
Web Host:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
```

Thiết lập này cho phép ứng dụng .NET 9 thử chạy trên runtime .NET 10. Cách tương
thích nhất vẫn là cài .NET 9 Runtime; nếu dùng ASP.NET Core, cài cả
.NET 9 ASP.NET Core Runtime. Sau khi cài runtime, mở lại PowerShell.

### 1. Kiểm tra .NET và SQL Server

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

Đảm bảo SQL Server đã chạy và tài khoản Windows hiện tại có thể kết nối tới server.
Nếu chưa có chứng chỉ HTTPS cho localhost, có thể tin cậy chứng chỉ phát triển bằng:

```powershell
dotnet dev-certs https --trust
```

### 2. Khởi tạo các bảng nền của ASP.NET Boilerplate

Chạy Migrator một lần từ thư mục gốc:

```powershell
dotnet run --project Backend\aspnet-core\src\ToeicExam.Migrator -- -q
```

Đợi lệnh kết thúc thành công trước khi tiếp tục. Migrator áp dụng các migration nền
của ASP.NET Boilerplate vào database.

### 3. Khởi động API và web quản trị

Trong cùng cửa sổ PowerShell hoặc một cửa sổ khác:

```powershell
dotnet run --project Backend\aspnet-core\src\ToeicExam.Web.Host
```

Khi host khởi động, ứng dụng sẽ tạo database nếu tài khoản có quyền, tạo các bảng
TOEIC còn thiếu và thêm tài khoản quản trị demo nếu chưa tồn tại. Mở URL:

**https://localhost:44311/**

Nếu cửa sổ chạy ứng dụng hiển thị URL khác, hãy mở URL đó. Giữ cửa sổ PowerShell
đang chạy để sử dụng web; nhấn `Ctrl+C` để dừng.

Trang quản trị chỉ dành cho admin:

| Email | Mật khẩu |
|---|---|
| `admin@toeic.local` | `Admin123!` |

Admin import file đề `.docx`, rà soát nội dung, nhập đáp án đúng rồi publish đề.
Đề chỉ có thể publish khi câu hỏi có đủ lựa chọn và đáp án.
Admin có thể xóa đề nháp hoặc đề đã xuất bản. Xóa đề sẽ xóa toàn bộ lượt làm bài,
câu trả lời và kết quả liên quan; thao tác không thể hoàn tác.

Muốn import sẵn đáp án, đánh dấu lựa chọn đúng bằng cách bọc chính thẻ `<OPTION>` đó
trong `<ANSWER>` bên trong mỗi `<QUESTION>`. Ví dụ, để chọn đáp án C:

```text
<QUESTION id="1">
  <STEM>Question text</STEM>
  <OPTION label="A">Choice A</OPTION>
  <OPTION label="B">Choice B</OPTION>
  <ANSWER><OPTION label="C">Choice C</OPTION></ANSWER>
  <OPTION label="D">Choice D</OPTION>
</QUESTION>
```

Định dạng ngắn `<ANSWER>C</ANSWER>` cũng được hỗ trợ. Thẻ `<ANSWER>` là tùy chọn;
nếu có, đáp án phải là A, B, C hoặc D và khớp một lựa chọn. Đề vẫn được import ở trạng
thái nháp và cần admin rà soát rồi publish. Tài liệu TOEIC mẫu cũ đi kèm không có thẻ
đáp án; file `TOEIC_Reading_Test_10_with_ANSWER_tags.docx` có thẻ đáp án cho cả 100 câu.
Trong file này, nội dung câu hỏi kết thúc ngay trước lựa chọn `<OPTION>` đầu tiên dù
không có thẻ đóng `</STEM>`; bộ import hỗ trợ định dạng đó.

## Chạy ứng dụng Flutter cho học viên

Mở cửa sổ PowerShell khác tại thư mục gốc. Khởi động Android emulator trước, sau đó:

```powershell
cd Frontend
flutter pub get
flutter run
```

Mặc định emulator Android gọi API qua `http://10.0.2.2:5055/api`. Nếu API chạy ở
địa chỉ khác, truyền URL gốc có `/api`:

```powershell
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5055/api
```

Thiết bị thật cần dùng địa chỉ IP LAN của máy đang chạy Backend, ví dụ
`http://192.168.1.20:5055/api`; cho phép cổng đó qua Windows Firewall. HTTP chỉ phù
hợp cho phát triển trong mạng tin cậy; khi triển khai, cấu hình HTTPS và dùng URL
HTTPS của máy chủ.

App Flutter không có màn hình đăng nhập. API cấp một khoá ngẫu nhiên riêng cho mỗi
lượt thi ẩn danh để chỉ lượt thi đó mới lưu câu trả lời và xem kết quả được.

### Chạy app Flutter trên web (Chrome)

Web platform đã được tạo trong dự án. Trước tiên khởi động Backend như hướng dẫn
ở trên, sau đó mở PowerShell tại thư mục gốc và chạy:

```powershell
cd Frontend
flutter pub get
flutter run -d chrome --web-port 8080 --dart-define=API_BASE_URL=http://localhost:5055/api
```

Chrome sẽ mở app tại `http://localhost:8080`. Địa chỉ này đã nằm trong danh sách
CORS development của Backend. Để chạy thủ công thay vì mở Chrome tự động, dùng
`flutter run -d web-server --web-hostname 0.0.0.0 --web-port 8080` rồi mở
`http://localhost:8080` trong trình duyệt.

### Build file APK

Để tạo bản Android debug có thể cài thử:

```powershell
cd Frontend
flutter build apk --debug
```

APK được tạo tại `Frontend\build\app\outputs\flutter-apk\app-debug.apk`. Để tạo APK
release dùng phát hành, chạy `flutter build apk --release`; cần cấu hình ký ứng dụng
Android trước khi phân phối chính thức.

## Cấu hình SQL Server

Cấu hình mặc định nằm trong:

- `Backend\aspnet-core\src\ToeicExam.Web.Host\appsettings.json`
- `Backend\aspnet-core\src\ToeicExam.Migrator\appsettings.json`

Có thể ghi đè connection string cho cả hai lệnh trong cửa sổ PowerShell hiện tại:

```powershell
$env:ConnectionStrings__Default = "Server=localhost\SQLEXPRESS;Database=toeic_reading;Trusted_Connection=True;TrustServerCertificate=True;"
```

Thay `localhost\SQLEXPRESS` bằng tên server/instance của bạn. Sau khi đặt cấu hình,
chạy lại Migrator trước, rồi khởi động Web Host. Với SQL Server Authentication,
connection string có thể dùng dạng:

```powershell
$env:ConnectionStrings__Default = "Server=localhost;Database=toeic_reading;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
```

Không đưa mật khẩu thật vào mã nguồn hoặc commit connection string chứa thông tin
đăng nhập.

## Khắc phục sự cố

- **Không kết nối được SQL Server:** kiểm tra dịch vụ SQL Server, tên instance,
  quyền Windows Authentication và giá trị `ConnectionStrings__Default`.
- **Không tạo được database/bảng:** tài khoản kết nối cần quyền tạo database và
  bảng; có thể tạo database `toeic_reading` trước hoặc cấp quyền phù hợp.
- **Lỗi chứng chỉ HTTPS:** chạy `dotnet dev-certs https --trust`, sau đó khởi động
  lại host và tải lại trang.
- **Không tìm thấy runtime .NET 9:** xem mục [Nếu máy chỉ có .NET 10](#nếu-máy-chỉ-có-net-10)
  và đặt `DOTNET_ROLL_FORWARD` trước khi chạy Migrator/Web Host, hoặc cài .NET 9 Runtime.
- **App không kết nối API trên Android emulator:** kiểm tra Backend đang chạy ở cổng
  HTTP `5055`; emulator dùng `10.0.2.2` để truy cập máy host, không dùng `localhost`.
- **App trên điện thoại thật không kết nối được:** đặt `API_BASE_URL` thành IP LAN
  của máy tính và cho phép cổng Backend qua firewall.

## Thư mục liên quan

- `Backend\AdminWeb` — web quản trị, yêu cầu admin đăng nhập
- `Backend\aspnet-core\src\ToeicExam.Web.Host` — API và host web quản trị
- `Backend\aspnet-core\src\ToeicExam.Migrator` — migration các bảng nền
- `Backend\aspnet-core\src\ToeicExam.Web.Host\Sql\toeic_reading.sql` — schema TOEIC
- `Frontend` — ứng dụng Flutter/Dart cho học viên, không yêu cầu đăng nhập
