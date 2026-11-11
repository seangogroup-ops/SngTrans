# SngTrans cho Windows (bản C# .NET 10)

[**⬇ Tải SngTrans cho Windows**](https://github.com/seangogroup-ops/SngTrans/releases/latest/download/SngTrans-day-du.exe)
(bấm là tải ngay file `.exe`, chạy được trên mọi máy Windows 10/11, không cần cài thêm gì)


Chạy ngầm ở khay hệ thống. Bôi đen chữ ở bất kỳ app nào (Word, PDF, trình duyệt...) là hiện thẻ dịch.
Viết lại hoàn toàn từ bản Python/PySide6; **giữ nguyên** thư mục dữ liệu `%APPDATA%\SngTrans`
(cài đặt, ghi chú, phím tắt cũ dùng tiếp được).

**Hướng dẫn sử dụng:** https://sng-trans.vercel.app/ (nguồn trang web nằm ở thư mục `docs/`: `index.html`, `huong-dan.html`).

## Tạo app (.exe)
Cần **.NET 10 SDK** (https://dotnet.microsoft.com/download/dotnet/10.0). Bấm đúp `build.bat`, ra hai bản:

| Thư mục | Đặc điểm |
|---|---|
| `dist\SngTrans-nhe\SngTrans.exe` | Rất nhỏ (vài MB). Máy chạy cần cài **.NET 10 Desktop Runtime** (một lần). |
| `dist\SngTrans-day-du\SngTrans.exe` | Lớn hơn (~50–70 MB), tự chứa .NET, chép đi đâu cũng chạy. |

Chạy thử từ mã nguồn: bấm đúp `run.bat` (cửa sổ đen in nhật ký từng bước).

> Vì sao không phải Native AOT? WinForms/WPF chưa hỗ trợ Native AOT nên bản này là .NET thường (ReadyToRun,
> gói một file). Muốn nhẹ hơn nữa thì phải viết lại giao diện bằng Win32 thuần hoặc Rust.

## Dùng
- Lần chạy đầu app tự bật **Chạy cùng Windows** (chạy ngầm khi bật máy). Muốn tắt: Cài đặt > Chạy cùng Windows.
- Bôi đen (kéo chuột hoặc nhấp đúp/ba) -> thẻ dịch hiện ngay trên con trỏ.
- Phím tắt `Alt+1` dịch phần đang bôi đen ngay (đổi được trong Cài đặt; Backspace = về mặc định).
- Phím tắt `Alt+2` bật/tắt **Tự dịch khi bôi đen** ở bất kỳ app nào (hiện thông báo nhỏ; đổi được ở Cài đặt > Phím bật/tắt tự dịch; Backspace = về mặc định).
- Cập nhật lên bản này: phím tắt khách đã lưu từ trước (Alt+F1/F2, hay phím tự đổi) bị xoá một lần, về mặc định `Alt+1` / `Alt+2`. Sau đó khách đổi phím ở Cài đặt thì được giữ nguyên.
- Lần đầu tải về và mỗi khi cập nhật lên phiên bản mới, app hiện một màn hướng dẫn một trang (hai phím tắt đang dùng) với nút **Next**; mỗi phiên bản chỉ hiện đúng một lần.
- `Esc` hoặc bấm ra ngoài để đóng; nút ghim để giữ thẻ; kéo vùng trống của thẻ để di chuyển.
- Biểu tượng khay: **Tự dịch** (bật/tắt), **Ghi chú**, **Cài đặt**, **Cập nhật**, **Thoát**.
- **Ghi chú** trên thẻ lưu câu/từ kèm bản dịch (tab Ghi chú, xuất CSV được).
- Tick "Không dịch app này" trên thẻ để loại một app; bỏ chặn ở tab Chặn app.

## Tắt hẳn (kết thúc tác vụ)
- Chuột phải biểu tượng khay > **Thoát**, hoặc nút **Kết thúc tác vụ** ở chân hộp Cài đặt.
- Ở bất kỳ cửa sổ nào: `Ctrl+Alt+Shift+Q`.
- Không cần mở app: bấm đúp `Tat-SngTrans.bat`, hoặc `SngTrans.exe --quit`.
- Hộp Cài đặt chỉ tự mở ở lần chạy đầu tiên; các lần sau app chạy ngầm. Mở lại bằng cách bấm biểu tượng khay hoặc bấm đúp `SngTrans.exe` lần nữa.

## Lưu ý
- Lấy chữ bằng cách gửi Ctrl+C rồi trả clipboard về như cũ (giữ chữ, ảnh, file, HTML, RTF).
- Cửa sổ dòng lệnh (cmd, PowerShell, Windows Terminal) bị bỏ qua vì Ctrl+C ở đó là ngắt chương trình.
- Dùng endpoint dịch miễn phí của Google nên cần có mạng. Kết nối HTTPS được giữ sẵn; chỉ gửi thêm sang
  endpoint dự phòng khi endpoint chính chậm quá 0,6 giây.
- Hook chuột/phím chạy trên luồng riêng nên giao diện bận cũng không làm giật chuột.

## Nếu app không dịch
1. Bấm đúp `run.bat`, bôi đen thử một chữ và xem dòng nào hiện ra.
2. Nhật ký luôn ở `%APPDATA%\SngTrans\sngtrans.log`. Gửi file này nếu cần nhờ sửa.
3. Không thấy biểu tượng khay: Windows 11 hay giấu nó sau mũi tên `^` ở góc phải taskbar.
4. Không bôi đen được trong phần mềm đang chạy "Run as administrator": chạy SngTrans bằng quyền admin tương ứng.
5. Phím tắt không hoạt động: có thể app khác đang chiếm tổ hợp đó, đổi phím khác trong Cài đặt.

## Tự cập nhật (làm hết trên web GitHub, không cần cài .NET)
App tự kiểm tra bản mới trên **GitHub Releases** (20 giây sau khi mở, rồi 6 giờ một lần) và **tự tải, thay exe, mở lại bản mới**,
không hiện hộp thông báo. Nếu đang mở thẻ dịch hoặc hộp Cài đặt thì chờ xong mới cài. Kiểm tra tay: chuột phải biểu tượng khay > **Cập nhật**.
Tự cài cần bản phát hành có file `.sha256` đi kèm (workflow đã tạo sẵn); thiếu hoặc sai mã thì app không cài.

Thiết lập một lần:
1. Kho **Public** `seangogroup-ops/SngTrans`, kéo thả toàn bộ thư mục dự án lên (Add file > Upload files).
2. `Services/Updater.cs` đã điền sẵn `DefaultRepo = "seangogroup-ops/SngTrans"`.

Phát hành bản mới: tab **Actions** > **Build Windows exe** > **Run workflow** > gõ số phiên bản (vd `2.1.0`) > Run.
Khoảng 5 phút sau, trang **Releases** có `SngTrans-nhe.exe` và `SngTrans-day-du.exe`; máy đang chạy bản cũ sẽ tự cập nhật.
(Cách khác: tạo tag `v2.1.0` bằng git cũng tự phát hành.)

## Kiểm thử phần lõi
`cd tests\Core.Tests && dotnet run` (69 kiểm tra: phân tích phản hồi Google, đoán ngôn ngữ, chia đoạn,
dự phòng khi bị giới hạn tần suất, đọc/ghi cài đặt kiểu cũ...).
