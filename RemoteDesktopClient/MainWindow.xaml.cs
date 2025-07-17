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
using System.Threading;

namespace RemoteDesktopClient
{
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected = false;
        private readonly byte[] aesKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        private readonly byte[] aesIV = Encoding.UTF8.GetBytes("1234567890123456");
        private Ellipse fakeCursor;

        private System.Timers.Timer inputTimer;
        private System.Windows.Point lastMousePosition;
        private volatile bool isMousePositionChanged = false;
        private volatile int scrollDeltaAccumulator = 0;

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

            inputTimer = new System.Timers.Timer(50);
            inputTimer.Elapsed += InputTimer_Elapsed;
            inputTimer.AutoReset = true;
        }

        private async void InputTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!isConnected) return;

            if (isMousePositionChanged)
            {
                isMousePositionChanged = false;
                try
                {
                    double scaleX = 0, scaleY = 0;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (imageScreen.ActualWidth > 0) scaleX = Screen.PrimaryScreen.Bounds.Width / imageScreen.ActualWidth;
                        if (imageScreen.ActualHeight > 0) scaleY = Screen.PrimaryScreen.Bounds.Height / imageScreen.ActualHeight;
                    });

                    if (scaleX > 0 && scaleY > 0)
                    {
                        int x = (int)(lastMousePosition.X * scaleX);
                        int y = (int)(lastMousePosition.Y * scaleY);
                        await SendCommandToServer($"MOVE:{x},{y}");
                    }
                }
                catch { }
            }

            int currentScrollDelta = Interlocked.Exchange(ref scrollDeltaAccumulator, 0);
            if (currentScrollDelta != 0)
            {
                try
                {
                    string direction = currentScrollDelta > 0 ? "up" : "down";
                    int amount = Math.Abs(currentScrollDelta);
                    await SendCommandToServer($"SCROLL:{direction},{amount}");
                }
                catch { }
            }
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

                inputTimer.Start();

                Dispatcher.Invoke(() =>
                {
                    txtServerIP.IsEnabled = false;
                    btnConnect.IsEnabled = false;
                    btnDisconnect.IsEnabled = true;
                    System.Windows.MessageBox.Show("Kết nối thành công!");
                });

                await Task.Run(() => ReceiveScreenAsync());
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
                inputTimer.Stop();
                isConnected = false;
                stream?.Close();
                client?.Close();
                Dispatcher.Invoke(() =>
                {
                    txtServerIP.IsEnabled = true;
                    btnConnect.IsEnabled = true;
                    btnDisconnect.IsEnabled = false;
                    imageScreen.Source = null;
                    if (System.Windows.Application.Current.MainWindow != null && System.Windows.Application.Current.MainWindow.IsVisible)
                    {
                        System.Windows.MessageBox.Show("Disconnect thành công");
                    }
                });
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
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
                    if (bytesRead < 4) { Disconnect(); break; }
                    int length = BitConverter.ToInt32(buffer, 0);

                    if (length > buffer.Length) { Disconnect(); break; }

                    var data = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        bytesRead = await stream.ReadAsync(data, totalRead, length - totalRead);
                        if (bytesRead == 0) { Disconnect(); break; }
                        totalRead += bytesRead;
                    }
                    if (totalRead < length) continue;

                    var decryptedData = Decrypt(data, aesKey, aesIV);
                    var command = Encoding.UTF8.GetString(decryptedData);

                    if (command == "DISCONNECT")
                    {
                        Dispatcher.Invoke(() => System.Windows.MessageBox.Show("Server đã dừng, kết nối ngắt."));
                        Disconnect();
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
                            });
                        }
                    }
                }
                catch
                {
                    Disconnect();
                    break;
                }
            }
        }

        private void ImageScreen_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point position = e.GetPosition(imageScreen);
            Canvas.SetLeft(fakeCursor, position.X - fakeCursor.Width / 2);
            Canvas.SetTop(fakeCursor, position.Y - fakeCursor.Height / 2);

            if (isConnected)
            {
                lastMousePosition = position;
                isMousePositionChanged = true;
            }
        }

        private async void ImageScreen_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            imageScreen.Focus();
            if (isConnected) await SendCommandToServer("LCLICK");
        }

        private async void ImageScreen_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            imageScreen.Focus();
            if (isConnected) await SendCommandToServer("RCLICK");
        }

        private async void ImageScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isConnected) await SendCommandToServer("LRELEASE");
        }

        private async void ImageScreen_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isConnected) await SendCommandToServer("RRELEASE");
        }

        private void ImageScreen_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (isConnected)
            {
                Interlocked.Add(ref scrollDeltaAccumulator, e.Delta);
            }
        }

        private async void TxtCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && isConnected && !string.IsNullOrEmpty(txtCommand.Text))
            {
                await SendCommandToServer($"KEY:{txtCommand.Text}");
                txtCommand.Clear();
                MainGrid.Focus(); // CHUYỂN FOCUS VỀ GRID CHÍNH
            }
        }

        private async void BtnSendKey_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected && !string.IsNullOrEmpty(txtCommand.Text))
            {
                await SendCommandToServer($"KEY:{txtCommand.Text}");
                txtCommand.Clear();
                MainGrid.Focus(); // CHUYỂN FOCUS VỀ GRID CHÍNH
            }
        }

        private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox)
            {
                return;
            }

            if (isConnected && e.Key == Key.Back)
            {
                await SendCommandToServer("KEY:{BS}");
            }
        }

        private async Task SendCommandToServer(string command)
        {
            if (!isConnected) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command);
                byte[] encryptedData = Encrypt(data, aesKey, aesIV);

                var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await stream.WriteAsync(encryptedData, 0, encryptedData.Length);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi gửi lệnh: {ex.Message}");
                Disconnect();
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