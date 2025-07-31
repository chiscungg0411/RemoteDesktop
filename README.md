# Ứng dụng Remote Desktop

## Tổng quan

Dự án này là một ứng dụng Remote Desktop cho phép người dùng điều khiển một máy tính từ xa qua mạng. Ứng dụng bao gồm hai thành phần chính: một ứng dụng server chạy trên máy tính cần điều khiển và một ứng dụng client chạy trên máy tính điều khiển.

## Tính năng-   **Điều khiển từ xa:** Điều khiển chuột và bàn phím trên máy tính từ xa.
-   **Chia sẻ màn hình:** Xem màn hình của máy tính từ xa theo thời gian thực.
-   **Giao tiếp an toàn:** Sử dụng mã hóa AES để truyền dữ liệu an toàn giữa client và server.
-   **Xác thực Username/Password:** Yêu cầu xác thực username và password trước khi thiết lập kết nối.
-   **Giao diện đơn giản:** Giao diện thân thiện với người dùng, dễ cài đặt và sử dụng.

## Công nghệ sử dụng

-   **C#:** Ngôn ngữ lập trình chính cho cả ứng dụng client và server.
-   **WPF (Windows Presentation Foundation):** Được sử dụng để xây dựng giao diện đồ họa cho cả hai ứng dụng.
-   **TCP Sockets:** Để thiết lập giao tiếp mạng giữa client và server.
-   **Mã hóa AES:** Advanced Encryption Standard để bảo mật truyền dữ liệu.
-   **System.Drawing:** Để chụp màn hình trên phía server.
-   **System.Windows.Forms:** Để mô phỏng các thao tác chuột và bàn phím trên phía server.

## Yêu cầu

Trước khi chạy ứng dụng, hãy đảm bảo bạn đã cài đặt những thứ sau:

-   **.NET Framework 4.7.2 trở lên:** Yêu cầu để chạy cả ứng dụng client và server.
-   **Hệ điều hành Windows:** Được phát triển và thử nghiệm trên Windows.

## Hướng dẫn cài đặt

### Server

1.  **Clone repository:**
    ```bash
    git clone [repository_url]    cd RemoteDesktopServer
    ```
2.  **Mở dự án trong Visual Studio.**
3.  **Build dự án:** Build dự án `RemoteDesktopServer` trong Visual Studio.
4.  **Chạy file thực thi:** Điều hướng đến thư mục đầu ra (ví dụ: `bin/Debug`) và chạy file `RemoteDesktopServer.exe`.

### Client

1.  **Clone repository:**
    ```bash
    git clone [repository_url]
    cd RemoteDesktopClient
    ```
2.  **Mở dự án trong Visual Studio.**
3.  **Build dự án:** Build dự án `RemoteDesktopClient` trong Visual Studio.
4.  **Chạy file thực thi:** Điều hướng đến thư mục đầu ra (ví dụ: `bin/Debug`) và chạy file `RemoteDesktopClient.exe`.

## Hướng dẫn sử dụng

### Server

1.  **Khởi động server:** Nhấp vào nút "Start Server" để bắt đầu lắng nghe các kết nối đến.
2.  **Ghi lại địa chỉ IP:** Ghi lại địa chỉ IP của máy chủ, vì nó sẽ được yêu cầu bởi client.
3.  **Cấu hình Firewall:** Đảm bảo rằng tường lửa của bạn cho phép các kết nối đến trên cổng 4000 (hoặc cổng bạn cấu hình cho server sử dụng).

### Client

1.  **Nhập địa chỉ IP Server:** Nhập địa chỉ IP của máy chủ vào trường "Server IP".
2.  **Nhập thông tin đăng nhập:** Nhập username và password để xác thực với server.
3.  **Kết nối đến Server:** Nhấp vào nút "Connect" để thiết lập kết nối với server.
4.  **Điều khiển từ xa:** Sau khi kết nối, ứng dụng client sẽ hiển thị màn hình từ xa và bạn có thể điều khiển nó bằng chuột và bàn phím của mình.
5.  **Ngắt kết nối:** Nhấp vào nút "Disconnect" để chấm dứt kết nối.

## Cân nhắc về bảo mật

-   **Mã hóa AES:** Ứng dụng sử dụng mã hóa AES để bảo vệ dữ liệu được truyền giữa client và server. Tuy nhiên, khóa được mã hóa cứng trong mã nguồn. Để sử dụng trong môi trường production, hãy cân nhắc triển khai một cơ chế trao đổi khóa an toàn hơn.
-   **Xác thực Username/Password:** Ứng dụng sử dụng xác thực username/password đơn giản. Hãy cân nhắc triển khai các cơ chế xác thực mạnh mẽ hơn như xác thực đa yếu tố để tăng cường bảo mật.
-   **Cấu hình Firewall:** Đảm bảo rằng tường lửa của bạn được cấu hình đúng cách để chỉ cho phép các kết nối cần thiết.

## Đóng góp

Đóng góp được hoan nghênh! Nếu bạn muốn đóng góp cho dự án này, vui lòng làm theo các bước sau:

1.  Fork repository.
2.  Tạo một branch mới cho tính năng hoặc sửa lỗi của bạn.
3.  Thực hiện các thay đổi của bạn.
4.  Kiểm tra kỹ lưỡng các thay đổi của bạn.
5.  Gửi một pull request.

## Tuyên bố từ chối trách nhiệm

Ứng dụng này được cung cấp "nguyên trạng", không có bất kỳ bảo hành nào. Sử dụng nó có nguy cơ của riêng bạn. Các nhà phát triển không chịu trách nhiệm cho bất kỳ thiệt hại hoặc mất mát nào do việc sử dụng ứng dụng này gây ra.

## Hình ảnh từ dự án

### Server
<img width="606" height="394" alt="image" src="https://github.com/user-attachments/assets/4453bcdc-e42b-4b24-87ea-7d4174ca8838" />

### Client
<img width="680" height="693" alt="image" src="https://github.com/user-attachments/assets/b18e1a31-a1c9-4d19-a687-7e055dc96892" />


