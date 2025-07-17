using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RemoteDesktopClient
{
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected = false;
        private readonly byte[] aesKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        private readonly byte[] aesIV = Encoding.UTF8.GetBytes("1234567890123456");
        private bool isDragging = false;
        private Ellipse fakeCursor;

        public MainWindow()
        {
            InitializeComponent();
            fakeCursor = new Ellipse()
            {
                Width = 18,
                Height = 18,
                Fill = Brushes.Red,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Opacity = 0.7
            };
            cursorOverlay.Children.Add(fakeCursor);
            Canvas.SetLeft(fakeCursor, -100);
            Canvas.SetTop(fakeCursor, -100);

            imageScreen.SizeChanged += ImageScreen_SizeChanged;
        }

        private void ImageScreen_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            cursorOverlay.Width = imageScreen.ActualWidth;
            cursorOverlay.Height = imageScreen.ActualHeight;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(txtServerIP.Text, 4000);
                stream = client.GetStream();
                isConnected = true;
                Dispatcher.Invoke(() =>
                {
                    txtServerIP.IsEnabled = false;
                    btnConnect.IsEnabled = false;
                    btnDisconnect.IsEnabled = true;
                    System.Windows.MessageBox.Show("Kết nối thành công!");
                });

                await Task.Run(() => ReceiveScreenAsync());
            }
            catch (SocketException ex)
            {
                System.Windows.MessageBox.Show($"Lỗi kết nối mạng: {ex.Message}");
                Disconnect();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi kết nối: {ex.Message}");
                Disconnect();
            }
        }

        private void Disconnect()
        {
            if (isConnected)
            {
                isConnected = false;
                stream?.Close();
                client?.Close();
                Dispatcher.Invoke(() =>
                {
                    txtServerIP.IsEnabled = true;
                    btnConnect.IsEnabled = true;
                    btnDisconnect.IsEnabled = false;
                    imageScreen.Source = null;
                    System.Windows.MessageBox.Show("Disconnect thành công");
                });
            }
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private async Task ReceiveScreenAsync()
        {
            var buffer = new byte[1024 * 1024];
            while (isConnected && client?.Connected == true)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, 4);
                    if (bytesRead == 0) continue;
                    int length = BitConverter.ToInt32(buffer, 0);

                    var data = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        bytesRead = await stream.ReadAsync(data, totalRead, length - totalRead);
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }

                    var decryptedData = Decrypt(data, aesKey, aesIV);
                    string command = Encoding.UTF8.GetString(decryptedData);

                    if (command == "DISCONNECT")
                    {
                        Disconnect();
                        Dispatcher.Invoke(() => System.Windows.MessageBox.Show("Server đã dừng, kết nối ngắt."));                       
                        break;
                    }
                    else
                    {
                        using (var ms = new MemoryStream(decryptedData))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.StreamSource = ms;
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                imageScreen.Source = bitmap;

                                cursorOverlay.Width = imageScreen.ActualWidth;
                                cursorOverlay.Height = imageScreen.ActualHeight;
                            });
                        }
                    }
                }
                catch (CryptographicException ex)
                {
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show($"Lỗi mã hóa: {ex.Message}"));
                    Disconnect();
                    break;
                }
                catch (IOException)
                {
                    Disconnect();
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show($"Lỗi không xác định: {ex.Message}"));
                    Disconnect();
                    break;
                }
            }
        }

        private async void ImageScreen_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point position = e.GetPosition(imageScreen);

            Canvas.SetLeft(fakeCursor, position.X - fakeCursor.Width / 2);
            Canvas.SetTop(fakeCursor, position.Y - fakeCursor.Height / 2);

            if (isConnected)
            {
                try
                {
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);

                    string command = $"MOVE:{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh di chuyển: {ex.Message}");
                    Disconnect();
                }
            }
        }
        private async void ImageScreen_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isConnected)
            {
                try
                {
                    isDragging = true;
                    System.Windows.Point position = e.GetPosition(imageScreen);
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);
                    string command = $"LCLICK:{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Nhấp trái tại: ({x}, {y})";
                    await SendLogToServer("LOGMOUSE:" + log);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh nhấp trái: {ex.Message}");
                }
            }
        }

        private async void ImageScreen_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isConnected && isDragging)
            {
                try
                {
                    isDragging = false;
                    System.Windows.Point position = e.GetPosition(imageScreen);
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);
                    string command = $"LRELEASE:{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Thả trái tại: ({x}, {y})";
                    await SendLogToServer("LOGMOUSE:" + log);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh thả trái: {ex.Message}");
                }
            }
        }

        private async void ImageScreen_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isConnected)
            {
                try
                {
                    System.Windows.Point position = e.GetPosition(imageScreen);
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);
                    string command = $"RCLICK:{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Nhấp phải tại: ({x}, {y})";
                    await SendLogToServer("LOGMOUSE:" + log);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh nhấp phải: {ex.Message}");
                }
            }
        }

        private async void ImageScreen_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isConnected)
            {
                try
                {
                    System.Windows.Point position = e.GetPosition(imageScreen);
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);
                    string command = $"RRELEASE:{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Thả phải tại: ({x}, {y})";
                    await SendLogToServer("LOGMOUSE:" + log);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh thả phải: {ex.Message}");
                }
            }
        }

        private async void ImageScreen_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (isConnected)
            {
                try
                {
                    string direction = e.Delta > 0 ? "up" : "down";
                    int amount = Math.Abs(e.Delta) / 10;
                    System.Windows.Point position = e.GetPosition(imageScreen);
                    double scaleX = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                    double scaleY = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    int x = (int)(position.X * scaleX);
                    int y = (int)(position.Y * scaleY);
                    string command = $"SCROLL:{direction},{amount},{x},{y}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Cuộn {direction} với {amount} pixels tại: ({x}, {y})";
                    await SendLogToServer("LOGMOUSE:" + log);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh cuộn: {ex.Message}");
                }
            }
        }

        private async void TxtCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && isConnected && !string.IsNullOrEmpty(txtCommand.Text))
            {
                try
                {
                    string command = $"KEY:{txtCommand.Text}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Phím gửi: {txtCommand.Text}";
                    await SendLogToServer("LOGKEY:" + log);

                    txtCommand.Clear();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh phím: {ex.Message}");
                }
            }
        }

        private async void BtnSendKey_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected && !string.IsNullOrEmpty(txtCommand.Text))
            {
                try
                {
                    string command = $"KEY:{txtCommand.Text}";
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);

                    string log = $"Phím gửi: {txtCommand.Text}";
                    await SendLogToServer("LOGKEY:" + log);

                    txtCommand.Clear();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi lệnh phím: {ex.Message}");
                }
            }
        }

        private async Task SendLogToServer(string log)
        {
            if (isConnected)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(log);
                    byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                    var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(encryptedData, 0, encryptedData.Length);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi gửi log lên server: {ex.Message}");
                }
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