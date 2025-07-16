using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Linq;

namespace RemoteDesktopServer
{
    public partial class MainWindow : Window
    {
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private bool isRunning = false;
        private readonly byte[] aesKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        private readonly byte[] aesIV = Encoding.UTF8.GetBytes("1234567890123456");

        private int sessionId = 0;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServer();
        }

        private void StopServer()
        {
            isRunning = false;
            try
            {
                sessionId++;
                if (stream != null)
                {
                    try {
                        byte[] disconnectData = Encoding.UTF8.GetBytes("DISCONNECT");
                        byte[] encryptedDisconnect = Encrypt(disconnectData, aesKey, aesIV);
                        var lengthBytes = BitConverter.GetBytes(encryptedDisconnect.Length);
                        stream.Write(lengthBytes, 0, lengthBytes.Length);
                        stream.Write(encryptedDisconnect, 0, encryptedDisconnect.Length);
                    }
                    catch { 
                    }
                    stream.Close();
                }
                stream = null;
                client?.Close();
                client = null;

                listener?.Stop();
                listener = null;
            }
            catch { 
            }

            Dispatcher.Invoke(() =>
            {
                btnStart.IsEnabled = true;
                btnStop.IsEnabled = false;
                txtStatus.AppendText("[INFO] Server stopped.\n");
            });
        }

        private Bitmap CaptureScreen()
        {
            var screen = Screen.PrimaryScreen.Bounds;
            var bitmap = new Bitmap(screen.Width, screen.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screen.X, screen.Y, 0, 0, screen.Size);
            }
            return bitmap;
        }

        private byte[] BitmapToBytes(Bitmap bitmap, long quality)
        {
            using (var ms = new MemoryStream())
            {
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .First(codec => codec.MimeType == "image/jpeg");
                var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bitmap.Save(ms, jpegEncoder, encoderParameters);
                return ms.ToArray();
            }
        }

        private void BtnTestCapture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var bitmap = CaptureScreen())
                {
                    var bytes = BitmapToBytes(bitmap, 100);
                    File.WriteAllBytes("test_screenshot.jpg", bytes);
                    txtStatus.AppendText("[INFO] Ảnh chụp màn hình đã được lưu tại test_screenshot.jpg\n");
                    txtStatus.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                txtStatus.AppendText($"[ERROR] {ex.Message}\n");
                txtStatus.ScrollToEnd();
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                listener = new TcpListener(System.Net.IPAddress.Any, 4000);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();
                isRunning = true;
                Dispatcher.Invoke(() =>
                {
                    btnStart.IsEnabled = false;
                    btnStop.IsEnabled = true;
                    txtStatus.AppendText("[INFO] Server started, waiting for client...\n");
                });

                while (isRunning)
                {
                    try
                    {
                        client = await listener.AcceptTcpClientAsync();
                        if (!isRunning || client == null) break;
                        stream = client.GetStream();

                        int thisSession = ++sessionId;

                        Dispatcher.Invoke(() =>
                        {
                            txtStatus.AppendText("[INFO] Client connected!\n");
                        });

                        var sendTask = Task.Run(() => SendScreenAsync(thisSession));
                        var recvTask = Task.Run(() => ReceiveCommandsAsync(thisSession));

                        await Task.WhenAny(sendTask, recvTask);
                    }
                    catch { }

                    try { stream?.Close(); } catch { }
                    stream = null;
                    try { client?.Close(); } catch { }
                    client = null;

                    Dispatcher.Invoke(() => txtStatus.AppendText("[INFO] Client disconnected or error. Waiting for new client...\n"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtStatus.AppendText($"[ERROR] {ex.Message}\n"));
                StopServer();
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopServer();
        }
        private async Task SendScreenAsync(int mySession)
        {
            try
            {
                while (isRunning && client != null && stream != null && mySession == sessionId)
                {
                    if (!isRunning || stream == null || client == null || mySession != sessionId)
                        break;

                    try
                    {
                        using (var bitmap = CaptureScreen())
                        {
                            var imageBytes = BitmapToBytes(bitmap, 50);
                            var encryptedBytes = Encrypt(imageBytes, aesKey, aesIV);
                            var lengthBytes = BitConverter.GetBytes(encryptedBytes.Length);

                            if (!isRunning || stream == null || client == null || mySession != sessionId)
                                break;

                            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                            await stream.WriteAsync(encryptedBytes, 0, encryptedBytes.Length);
                        }
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtStatus.AppendText("[INFO] Stop sending screen: " + ex.Message + "\n");
                            txtStatus.ScrollToEnd();
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText("[ERROR] Gửi màn hình - outer: " + ex.Message + "\n");
                    txtStatus.ScrollToEnd();
                });
            }
        }

        private async Task ReceiveCommandsAsync(int mySession)
        {
            var buffer = new byte[1024];
            try
            {
                while (isRunning && client != null && stream != null && mySession == sessionId)
                {
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, 4);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => {
                            txtStatus.AppendText($"[ERROR] Lỗi nhận stream: {ex.Message}\n");
                            txtStatus.ScrollToEnd();
                        });
                        break;
                    }

                    if (bytesRead == 0 || mySession != sessionId) break;
                    int length = BitConverter.ToInt32(buffer, 0);

                    var data = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        try
                        {
                            bytesRead = await stream.ReadAsync(data, totalRead, length - totalRead);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => {
                                txtStatus.AppendText($"[ERROR] Lỗi nhận luồng command: {ex.Message}\n");
                                txtStatus.ScrollToEnd();
                            });
                            break;
                        }

                        if (bytesRead == 0 || mySession != sessionId) break;
                        totalRead += bytesRead;
                    }

                    if (totalRead < length || mySession != sessionId) break;

                    var decryptedData = Decrypt(data, aesKey, aesIV);
                    var command = Encoding.UTF8.GetString(decryptedData);

                    HandleCommand(command);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[ERROR] Lỗi nền nhận lệnh: {ex.Message}\n");
                    txtStatus.ScrollToEnd();
                });
            }
        }

        private void HandleCommand(string command)
        {
            string NowStr() => DateTime.Now.ToString("HH:mm:ss");

            if (command.StartsWith("MOVE:"))
            {
                var parts = command.Substring(5).Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Chuột di chuyển đến: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("LCLICK:"))
            {
                var parts = command.Substring(7).Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Nhấp trái tại: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("LRELEASE:"))
            {
                var parts = command.Substring(9).Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Thả trái tại: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("RCLICK:"))
            {
                var parts = command.Substring(7).Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Nhấp phải tại: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("RRELEASE:"))
            {
                var parts = command.Substring(9).Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Thả phải tại: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("SCROLL:"))
            {
                var parts = command.Substring(7).Split(',');
                string direction = parts[0];
                int amount = int.Parse(parts[1]);
                int x = int.Parse(parts[2]);
                int y = int.Parse(parts[3]);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Cuộn {direction} với {amount} pixels tại: ({x}, {y})\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("KEY:"))
            {
                string keyStr = command.Substring(4);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Phím gửi: {keyStr}\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("LOGMOUSE:"))
            {
                string log = command.Substring(9);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Log chuột từ client: {log}\n");
                    txtStatus.ScrollToEnd();
                });
            }
            else if (command.StartsWith("LOGKEY:"))
            {
                string log = command.Substring(7);
                Dispatcher.Invoke(() =>
                {
                    txtStatus.AppendText($"[{NowStr()}] Log phím từ client: {log}\n");
                    txtStatus.ScrollToEnd();
                });
            }
        }

        private byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        private byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}