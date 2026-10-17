# Win10 End Task

**Tiếng Việt** | [English](README.md)

Công cụ nhỏ gọn mang tính năng "End task" khi chuột phải trên taskbar của Windows 11 xuống Windows 10, không làm mất Jump List gốc và không patch file hệ thống.

---

## Lý do ra đời

Windows 11 tích hợp sẵn nút "End task" ngay khi chuột phải vào icon taskbar. Trên Windows 10, để tắt một ứng dụng bị đơ ta thường phải mở Task Manager hoặc bấm `Alt + F4` (vốn vô dụng khi UI thread của app bị freeze).

Các giải pháp hiện nay:
- Dùng công cụ ép đổi về menu Win32 cổ điển thì mất sạch danh sách file gần đây (Recent files), tác vụ duyệt web và ghim ứng dụng.
- Inject DLL vào `explorer.exe` thì dễ gây crash Explorer khi Windows cập nhật.

Công cụ này giải quyết vấn đề bằng cách giữ nguyên 100% menu Jump List mặc định: khi chuột phải vào icon ứng dụng, một nút **End task** gọn gàng xuất hiện cạnh menu. Bấm vào là kill tiến trình ngay lập tức.

---

## Tính năng

- **Giữ nguyên Jump List:** Danh sách file gần đây, các tác vụ nhanh và ghim app hoạt động bình thường.
- **Dập tắt triệt để (Không tự mở lại):** Quét sạch toàn bộ cây tiến trình (`taskkill /F /T`) và các tiến trình con/watchdog liên quan (Discord, Riot Games, Steam, trình duyệt), không cho ứng dụng tự động hồi sinh.
- **Kiến trúc không hook (Zero-Lag):** Sử dụng cơ chế kiểm tra phần cứng bất đồng bộ (`GetAsyncKeyState`) thay vì global hook, đảm bảo chuột chơi game (1000Hz+) mượt 100% không bị micro-stutter hay khựng trỏ.
- **Tiêu thụ tài nguyên:** ~2.5 MB RAM, 0% CPU khi chạy nền.
- **Không phụ thuộc:** Biên dịch bằng trình biên dịch .NET Framework 4.0+ có sẵn trên mọi bản Windows 10.
- **Khay hệ thống:** Chuột phải icon khay để bật "Khởi động cùng Windows" hoặc Thoát.

---

## Tự biên dịch

Không cần cài Visual Studio hay SDK. Chỉ cần chạy file:

```cmd
build.bat
```

Script sẽ gọi `csc.exe` kèm `app.manifest` tại `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`.

File xuất ra: `Win10EndTask.exe` (~18 KB).

---

## Cơ chế hoạt động

1. Luồng nền siêu nhẹ kiểm tra phần cứng (`GetAsyncKeyState`) để bắt khoảnh khắc nhấn chuột phải trên taskbar.
2. Xử lý qua MSAA (`IAccessible`) để lấy thông tin nút taskbar mà không làm nghẽn Explorer.
3. Tự động bóc tách và đối chiếu PID chính xác qua tên app, kênh chat, tab web hoặc cửa sổ nhóm.
4. Hiển thị nút bấm nổi chuẩn Fluent (`EndTaskFloatingBar`) ở chế độ topmost không chiếm focus chuột.
5. Khi click: dập tắt toàn bộ cây tiến trình và các service chạy kèm. Nếu click chuột ra ngoài hoặc sau 8 giây không thao tác, nút tự đóng.

---

## Giấy phép

MIT
