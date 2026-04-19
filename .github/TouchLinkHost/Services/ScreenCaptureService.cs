using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TouchLinkHost.Models;

namespace TouchLinkHost.Services
{
    /// <summary>
    /// 屏幕捕获服务 - GDI+ 屏幕截图 + JPEG 编码
    /// </summary>
    public class ScreenCaptureService : IDisposable
    {
        private readonly MouseKeyboardService _mouseKeyboard;
        private Thread? _streamingThread;
        private bool _isStreaming;
        private string? _targetIp;
        private int _targetPort;
        private UdpClient? _udpClient;
        private readonly object _lock = new object();

        // Quality settings
        private ScreenQuality _quality = ScreenQuality.Medium;
        private int _fps = 30;
        private int _width = 1920;
        private int _height = 1080;

        // Recording
        private bool _isRecording;
        private readonly string _recordingsPath;

        public event Action<string>? OnInfo;
        public event Action<string>? OnError;

        public ScreenCaptureService(MouseKeyboardService mouseKeyboard)
        {
            _mouseKeyboard = mouseKeyboard;
            _recordingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TouchLink", "Recordings");
            Directory.CreateDirectory(_recordingsPath);
        }

        /// <summary>
        /// 设置屏幕质量
        /// </summary>
        public void SetQuality(ScreenQuality quality)
        {
            _quality = quality;
            switch (quality)
            {
                case ScreenQuality.Low:
                    _fps = 15;
                    _width = 1280;
                    _height = 720;
                    break;
                case ScreenQuality.Medium:
                    _fps = 30;
                    _width = 1920;
                    _height = 1080;
                    break;
                case ScreenQuality.High:
                    _fps = 60;
                    _width = 1920;
                    _height = 1080;
                    break;
            }
            OnInfo?.Invoke($"Quality set to {quality}: {_fps}fps {_width}x{_height}");
        }

        /// <summary>
        /// 开始屏幕串流
        /// </summary>
        public void StartStreaming(string targetIp, int port)
        {
            _targetIp = targetIp;
            _targetPort = port;
            _isStreaming = true;

            _streamingThread = new Thread(StreamLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _streamingThread.Start();

            OnInfo?.Invoke($"Screen streaming started to {targetIp}:{port}");
        }

        /// <summary>
        /// 停止屏幕串流
        /// </summary>
        public void StopStreaming()
        {
            _isStreaming = false;
            _streamingThread?.Join(1000);
            _streamingThread = null;
            _udpClient?.Close();
            _udpClient = null;
            OnInfo?.Invoke("Screen streaming stopped");
        }

        /// <summary>
        /// 开始录制
        /// </summary>
        public void StartRecording(string? fileName = null)
        {
            if (_isRecording) return;
            
            _isRecording = true;
            var name = fileName ?? $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.gif";
            var path = Path.Combine(_recordingsPath, name);
            OnInfo?.Invoke($"Recording started: {path}");
        }

        /// <summary>
        /// 停止录制
        /// </summary>
        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            OnInfo?.Invoke("Recording stopped");
        }

        /// <summary>
        /// 获取录制文件列表
        /// </summary>
        public string[] GetRecordings()
        {
            if (!Directory.Exists(_recordingsPath))
                return Array.Empty<string>();
            return Directory.GetFiles(_recordingsPath, "*.gif");
        }

        /// <summary>
        /// 下载录制文件
        /// </summary>
        public string? GetRecordingPath(string fileName)
        {
            var path = Path.Combine(_recordingsPath, fileName);
            return File.Exists(path) ? path : null;
        }

        private void StreamLoop()
        {
            var frameInterval = 1000 / _fps;
            var lastFrame = DateTime.Now;

            while (_isStreaming)
            {
                try
                {
                    var now = DateTime.Now;
                    var elapsed = (now - lastFrame).TotalMilliseconds;

                    if (elapsed >= frameInterval)
                    {
                        CaptureAndSend();
                        lastFrame = now;
                    }

                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    if (_isStreaming)
                    {
                        OnError?.Invoke($"Stream error: {ex.Message}");
                    }
                }
            }
        }

        private void CaptureAndSend()
        {
            try
            {
                // Capture screen
                using var bitmap = CaptureScreen();
                if (bitmap == null) return;

                // Compress to JPEG
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Jpeg);
                var jpegData = ms.ToArray();

                // Send via UDP with header
                SendFrame(jpegData);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Capture error: {ex.Message}");
            }
        }

        private Bitmap? CaptureScreen()
        {
            try
            {
                // Capture primary screen
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return null;

                var bounds = screen.Bounds;
                var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

                    // Resize if needed
                    if (_width != bounds.Width || _height != bounds.Height)
                    {
                        var resized = new Bitmap(_width, _height);
                        using (var gr = Graphics.FromImage(resized))
                        {
                            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            gr.DrawImage(bitmap, 0, 0, _width, _height);
                        }
                        bitmap.Dispose();
                        return resized;
                    }
                }

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void SendFrame(byte[] jpegData)
        {
            try
            {
                if (_udpClient == null && _targetIp != null)
                {
                    _udpClient = new UdpClient();
                    _udpClient.Connect(_targetIp, _targetPort);
                }

                if (_udpClient == null) return;

                // Frame header: 0x54 0x4C 0x46 + 3 bytes length
                var header = new byte[6];
                header[0] = 0x54; // 'T'
                header[1] = 0x4C; // 'L'
                header[2] = 0x46; // 'F'
                header[3] = (byte)(jpegData.Length & 0xFF);
                header[4] = (byte)((jpegData.Length >> 8) & 0xFF);
                header[5] = (byte)((jpegData.Length >> 16) & 0xFF);

                var packet = new byte[header.Length + jpegData.Length];
                Buffer.BlockCopy(header, 0, packet, 0, header.Length);
                Buffer.BlockCopy(jpegData, 0, packet, header.Length, jpegData.Length);

                _udpClient.Send(packet, packet.Length);
            }
            catch (SocketException ex)
            {
                // Target unreachable, recreate socket
                _udpClient?.Close();
                _udpClient = null;
                if (ex.SocketErrorCode != SocketError.ConnectionReset)
                {
                    OnError?.Invoke($"Send error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopStreaming();
        }
    }
}
