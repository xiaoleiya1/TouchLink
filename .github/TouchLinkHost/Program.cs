using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using TouchLinkHost.Models;
using TouchLinkHost.Services;

namespace TouchLinkHost
{
    class Program
    {
        private static MouseKeyboardService? _mouseKeyboard;
        private static ScreenCaptureService? _screenCapture;
        private static LanService? _lanService;
        private static bool _isRunning = true;
        private static HttpListener? _downloadServer;
        private static bool _downloadServerRunning = false;
        private static readonly int DOWNLOAD_PORT = 8888;
        private static string? _localIp;
        
        private static ScreenQuality _currentQuality = ScreenQuality.Medium;
        private static bool _autoStartEnabled = false;
        private static string? _currentClientIp;

        static void Main(string[] args)
        {
            Console.Clear();
            PrintBanner();
            PrintMenu();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Shutdown();
            };

            InitializeServices();

            while (_isRunning)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    HandleKeyPress(key);
                }
                Thread.Sleep(100);
            }
        }

        static void HandleKeyPress(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.D:
                    if (!_downloadServerRunning) StartDownloadServer();
                    else StopDownloadServer();
                    break;
                case ConsoleKey.R:
                    RefreshDisplay();
                    break;
                case ConsoleKey.S:
                    ShowSettings();
                    break;
                case ConsoleKey.L:
                    ShowLanStatus();
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    Shutdown();
                    break;
            }
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("         TouchLink Host - Remote Control          ");
            Console.WriteLine("==================================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void PrintMenu()
        {
            Console.WriteLine("  [D] - 开启/关闭 下载页");
            Console.WriteLine("  [S] - 设置 (画质/自启)");
            Console.WriteLine("  [L] - 查看已连接设备");
            Console.WriteLine("  [R] - 刷新显示");
            Console.WriteLine("  [Q] - 退出程序");
            Console.WriteLine();
        }

        static void PrintStatus(bool connected = false)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("  本机 IP:    " + (_localIp ?? "获取中..."));
            Console.WriteLine("  TCP 端口:   7891 (命令通道)");
            Console.WriteLine("  UDP 端口:   7892 (屏幕流)");
            Console.WriteLine("  UDP 端口:   7893 (设备发现)");
            Console.WriteLine();
            if (connected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  状态:       已连接 " + _currentClientIp);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("  状态:       等待连接...");
            }
            Console.WriteLine("  画质:       " + _currentQuality);
            Console.WriteLine("  自启:       " + (_autoStartEnabled ? "开启" : "关闭"));
            if (_downloadServerRunning)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("  下载页:     http://" + _localIp + ":" + DOWNLOAD_PORT);
                Console.ResetColor();
            }
            Console.WriteLine("==================================================");
            Console.WriteLine();
        }

        static void RefreshDisplay()
        {
            Console.Clear();
            PrintBanner();
            PrintMenu();
            bool connected = _lanService?.IsConnected ?? false;
            PrintStatus(connected);
        }

        static void ShowSettings()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============= 设置菜单 =============");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("1. 屏幕画质: " + _currentQuality);
            Console.WriteLine("   L=低 M=中 H=高");
            Console.WriteLine();
            Console.WriteLine("2. 开机自启: " + (_autoStartEnabled ? "开启" : "关闭"));
            Console.WriteLine("   A=切换");
            Console.WriteLine();
            Console.WriteLine("3. 多显示器:");
            var displays = _mouseKeyboard?.GetDisplays() ?? Array.Empty<DisplayInfo>();
            for (int i = 0; i < displays.Length; i++)
            {
                var d = displays[i];
                Console.WriteLine("   " + i + ": " + d.Width + "x" + d.Height + (d.IsPrimary ? " [主屏]" : ""));
            }
            Console.WriteLine();
            Console.WriteLine("按 [Q] 返回");
            Console.WriteLine();

            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                    break;
                    
                switch (char.ToUpper(key.KeyChar))
                {
                    case 'L': _currentQuality = ScreenQuality.Low; _screenCapture?.SetQuality(_currentQuality); break;
                    case 'M': _currentQuality = ScreenQuality.Medium; _screenCapture?.SetQuality(_currentQuality); break;
                    case 'H': _currentQuality = ScreenQuality.High; _screenCapture?.SetQuality(_currentQuality); break;
                    case 'A': _autoStartEnabled = !_autoStartEnabled; SetAutoStart(_autoStartEnabled); break;
                }
                
                Console.WriteLine();
                Console.WriteLine("已切换: " + _currentQuality + " | 自启: " + (_autoStartEnabled ? "开启" : "关闭"));
            }

            RefreshDisplay();
        }

        static void ShowLanStatus()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============= 已连接设备 =============");
            Console.ResetColor();
            Console.WriteLine();

            if (_lanService?.IsConnected == true && _currentClientIp != null)
            {
                Console.WriteLine("IP: " + _currentClientIp);
                Console.WriteLine("状态: 已连接");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("暂无设备连接");
                Console.WriteLine();
                Console.WriteLine("在 Android 设备上打开 TouchLink 连接");
            }

            Console.WriteLine();
            Console.WriteLine("按 [Q] 返回");

            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                    break;
            }

            RefreshDisplay();
        }

        static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (key == null) return;
                
                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TouchLinkHost.exe");
                
                if (enable)
                {
                    key.SetValue("TouchLink", $"\"{exePath}\"");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[自启] 已开启");
                }
                else
                {
                    key.DeleteValue("TouchLink", false);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[自启] 已关闭");
                }
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[错误] 无法设置自启: " + ex.Message);
                Console.ResetColor();
            }
        }

        static void InitializeServices()
        {
            _mouseKeyboard = new MouseKeyboardService();
            _screenCapture = new ScreenCaptureService(_mouseKeyboard);
            _lanService = new LanService(_mouseKeyboard, _screenCapture);

            _lanService.OnClientConnected += (ip) =>
            {
                _currentClientIp = ip;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[连接] Android 设备已连接: " + ip);
                Console.ResetColor();
                RefreshDisplay();
            };

            _lanService.OnClientDisconnected += (ip) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[断开] Android 设备已断开: " + ip);
                Console.ResetColor();
                _currentClientIp = null;
                _screenCapture?.StopStreaming();
                RefreshDisplay();
            };

            _lanService.Start();

            _localIp = GetLocalIP();
            Console.WriteLine();
            PrintStatus(false);
        }

        static string? GetLocalIP()
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var ep = socket.LocalEndPoint as IPEndPoint;
                return ep?.Address.ToString();
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        static void StartDownloadServer()
        {
            if (_downloadServerRunning) return;

            try
            {
                _downloadServer = new HttpListener();
                _downloadServer.Prefixes.Add($"http://+:{DOWNLOAD_PORT}/");
                _downloadServer.Start();
                _downloadServerRunning = true;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[下载页] 已开启: http://" + _localIp + ":" + DOWNLOAD_PORT);
                Console.WriteLine("[下载页] 手机访问上方地址下载 APK");
                Console.ResetColor();

                Task.Run(() => HandleDownloadRequests());
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[错误] 无法开启下载页: " + ex.Message);
                Console.ResetColor();
                _downloadServerRunning = false;
            }
        }

        static void StopDownloadServer()
        {
            if (!_downloadServerRunning) return;

            _downloadServer?.Stop();
            _downloadServer?.Close();
            _downloadServer = null;
            _downloadServerRunning = false;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[下载页] 已关闭");
            Console.ResetColor();
            RefreshDisplay();
        }

        static void HandleDownloadRequests()
        {
            while (_downloadServerRunning && _downloadServer != null)
            {
                try
                {
                    var context = _downloadServer.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch
                {
                    break;
                }
            }
        }

        static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var response = context.Response;
                var path = context.Request.Url.AbsolutePath;

                if (path == "/" || path == "/index.html")
                {
                    string html = GetDownloadPageHtml();
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else if (path == "/TouchLink.apk")
                {
                    byte[]? apkData = GetEmbeddedApk();
                    if (apkData != null)
                    {
                        response.ContentType = "application/vnd.android.package-archive";
                        response.ContentLength64 = apkData.Length;
                        response.AddHeader("Content-Disposition", "attachment; filename=TouchLink.apk");
                        response.OutputStream.Write(apkData, 0, apkData.Length);
                        Console.WriteLine("[下载] TouchLink.apk (" + (apkData.Length / 1024 / 1024) + "MB) -> " + context.Request.RemoteEndPoint);
                    }
                    else
                    {
                        byte[] err = System.Text.Encoding.UTF8.GetBytes("APK not embedded");
                        response.StatusCode = 404;
                        response.ContentType = "text/plain";
                        response.ContentLength64 = err.Length;
                        response.OutputStream.Write(err, 0, err.Length);
                    }
                }
                else if (path == "/TouchLinkHost.exe")
                {
                    string exePath = AppDomain.CurrentDomain.BaseDirectory;
                    string fullPath = Path.Combine(exePath, "TouchLinkHost.exe");
                    if (File.Exists(fullPath))
                    {
                        byte[] fileBytes = File.ReadAllBytes(fullPath);
                        response.ContentType = "application/octet-stream";
                        response.ContentLength64 = fileBytes.Length;
                        response.AddHeader("Content-Disposition", "attachment; filename=TouchLinkHost.exe");
                        response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
                        Console.WriteLine("[下载] TouchLinkHost.exe -> " + context.Request.RemoteEndPoint);
                    }
                    else
                    {
                        byte[] err = System.Text.Encoding.UTF8.GetBytes("EXE not found");
                        response.StatusCode = 404;
                        response.ContentType = "text/plain";
                        response.ContentLength64 = err.Length;
                        response.OutputStream.Write(err, 0, err.Length);
                    }
                }
                else
                {
                    byte[] err = System.Text.Encoding.UTF8.GetBytes("Not found");
                    response.StatusCode = 404;
                    response.ContentType = "text/plain";
                    response.ContentLength64 = err.Length;
                    response.OutputStream.Write(err, 0, err.Length);
                }

                response.Close();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[错误] 处理请求失败: " + ex.Message);
                Console.ResetColor();
            }
        }

        static byte[]? GetEmbeddedApk()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "TouchLinkHost.TouchLink.apk";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        static string GetDownloadPageHtml()
        {
            return "<!DOCTYPE html><html lang='zh-CN'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>TouchLink 下载</title><style>*{margin:0;padding:0;box-sizing:border-box}body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:linear-gradient(135deg,#1a1a2e,#16213e,#0f3460);min-height:100vh;color:#fff;display:flex;align-items:center;justify-content:center}.container{max-width:500px;width:90%;background:rgba(255,255,255,0.05);border-radius:20px;padding:40px;border:1px solid rgba(255,255,255,0.1)}h1{font-size:2.2em;text-align:center;margin-bottom:8px;background:linear-gradient(90deg,#00d4ff,#7b2ff7);-webkit-background-clip:text;-webkit-text-fill-color:transparent}.subtitle{text-align:center;color:#888;margin-bottom:40px}.card{background:rgba(255,255,255,0.08);border-radius:16px;padding:25px;margin-bottom:20px;border:1px solid rgba(255,255,255,0.1);text-align:center}.card h3{margin-bottom:8px;font-size:1.2em}.card p{color:#888;font-size:0.9em;margin-bottom:15px}.btn{display:inline-block;padding:12px 30px;border-radius:10px;text-decoration:none;font-weight:600;background:linear-gradient(90deg,#00d4ff,#7b2ff7);color:#fff;transition:box-shadow 0.3s}.btn:hover{box-shadow:0 10px 30px rgba(0,212,255,0.4)}.info{text-align:center;margin-top:30px;color:#666;font-size:0.85em}.features{background:rgba(255,255,255,0.05);border-radius:12px;padding:20px;margin:20px 0;text-align:left}.features h4{color:#00d4ff;margin-bottom:10px}.features ul{list-style:none;color:#aaa;font-size:0.9em}.features li{padding:4px 0}.features li::before{content:'✓ ';color:#00d4ff}</style></head><body><div class='container'><h1>TouchLink</h1><p class='subtitle'>Android 远程触控 + Windows 主机</p><div class='features'><h4>功能特点</h4><ul><li>局域网/蓝牙双模式连接</li><li>触控板手势操作</li><li>屏幕实时串流</li><li>剪贴板同步</li><li>三指快捷手势</li></ul></div><div class='card'><h3>📱 Android APK</h3><p>安装到 Android 手机/平板</p><p style='color:#00d4ff;font-size:0.8em'>v2.0 | 14MB</p><a href='TouchLink.apk' class='btn'>下载 APK</a></div><div class='card'><h3>🖥️ Windows EXE</h3><p>双击即可运行，无需安装</p><p style='color:#00d4ff;font-size:0.8em'>48MB (含 APK)</p><a href='TouchLinkHost.exe' class='btn'>下载 EXE</a></div><p class='info'>确保手机和电脑在同一网络</p></div></body></html>";
        }

        static void Shutdown()
        {
            _isRunning = false;
            StopDownloadServer();
            _screenCapture?.StopStreaming();
            _lanService?.Stop();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n程序已退出");
            Console.ResetColor();
            Environment.Exit(0);
        }
    }
}
