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
using System.Runtime.InteropServices;

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

        #region PInvoke SendInput
        // Vùng này định nghĩa các cấu trúc và hàm SendInput để mô phỏng input
        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint Type;
            public MOUSEKEYBDHARDWAREINPUT Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct MOUSEKEYBDHARDWAREINPUT
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;
            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
            [FieldOffset(0)]
            public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort Vk;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint Msg;
            public ushort ParamL;
            public ushort ParamH;
        }

        // Các hằng số cho SendInput
        internal const uint INPUT_MOUSE = 0;
        internal const uint MOUSEEVENTF_MOVE = 0x0001;
        internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const int MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;
        #endregion

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
                    try
                    {
                        byte[] disconnectData = Encoding.UTF8.GetBytes("DISCONNECT");
                        byte[] encryptedDisconnect = Encrypt(disconnectData, aesKey, aesIV);
                        var lengthBytes = BitConverter.GetBytes(encryptedDisconnect.Length);
                        stream.Write(lengthBytes, 0, lengthBytes.Length);
                        stream.Write(encryptedDisconnect, 0, encryptedDisconnect.Length);
                    }
                    catch { }
                    stream.Close();
                }
                stream = null;
                client?.Close();
                client = null;
                listener?.Stop();
                listener = null;
            }
            catch { }

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
                var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
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
                    try
                    {
                        using (var bitmap = CaptureScreen())
                        {
                            var imageBytes = BitmapToBytes(bitmap, 50);
                            var encryptedBytes = Encrypt(imageBytes, aesKey, aesIV);
                            var lengthBytes = BitConverter.GetBytes(encryptedBytes.Length);
                            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                            await stream.WriteAsync(encryptedBytes, 0, encryptedBytes.Length);
                        }
                        await Task.Delay(100);
                    }
                    catch { break; }
                }
            }
            catch { }
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
                    catch { break; }

                    if (bytesRead == 0) break;
                    int length = BitConverter.ToInt32(buffer, 0);
                    var data = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        try
                        {
                            bytesRead = await stream.ReadAsync(data, totalRead, length - totalRead);
                        }
                        catch { break; }
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }
                    if (totalRead < length) break;
                    var decryptedData = Decrypt(data, aesKey, aesIV);
                    var command = Encoding.UTF8.GetString(decryptedData);
                    HandleCommand(command);
                }
            }
            catch { }
        }

        private void HandleCommand(string command)
        {
            try
            {
                string[] parts = command.Split(':');
                string action = parts[0];
                string[] values = parts.Length > 1 ? parts[1].Split(',') : new string[0];

                INPUT[] inputs = new INPUT[1];
                inputs[0].Type = INPUT_MOUSE;

                switch (action)
                {
                    case "MOVE":
                        int x = int.Parse(values[0]);
                        int y = int.Parse(values[1]);
                        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
                        int screenHeight = Screen.PrimaryScreen.Bounds.Height;
                        inputs[0].Data.Mouse.X = (x * 65535) / screenWidth;
                        inputs[0].Data.Mouse.Y = (y * 65535) / screenHeight;
                        inputs[0].Data.Mouse.Flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
                        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                        break;

                    case "LCLICK":
                        inputs[0].Data.Mouse.Flags = MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP;
                        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                        break;

                    case "RCLICK":
                        inputs[0].Data.Mouse.Flags = MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP;
                        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                        break;

                    case "SCROLL":
                        string direction = values[0];
                        int amount = int.Parse(values[1]);
                        int delta = direction == "up" ? amount : -amount;
                        inputs[0].Data.Mouse.MouseData = (uint)delta;
                        inputs[0].Data.Mouse.Flags = MOUSEEVENTF_WHEEL;
                        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                        break;

                    case "KEY":
                        string keyStr = parts[1];
                        SendKeys.SendWait(keyStr);
                        break;

                }

                Dispatcher.Invoke(() =>
                {
                    string nowStr = DateTime.Now.ToString("HH:mm:ss");
                    txtStatus.AppendText($"[{nowStr}] Executed: {command}\n");
                    txtStatus.ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    string nowStr = DateTime.Now.ToString("HH:mm:ss");
                    txtStatus.AppendText($"[{nowStr}] Error executing '{command}': {ex.Message}\n");
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