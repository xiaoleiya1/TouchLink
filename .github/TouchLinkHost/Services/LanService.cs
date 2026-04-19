using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TouchLinkHost.Models;

namespace TouchLinkHost.Services
{
    /// <summary>
    /// 局域网通信服务 - TCP指令通道 + UDP屏幕流
    /// </summary>
    public class LanService : IDisposable
    {
        public const int TCP_PORT = 7891;
        public const int UDP_SCREEN_PORT = 7892;
        public const int UDP_DISCOVERY_PORT = 7893;

        private TcpListener? _tcpListener;
        private UdpClient? _udpDiscovery;
        private TcpClient? _connectedClient;
        private NetworkStream? _clientStream;
        private CancellationTokenSource? _cts;
        private Task? _tcpTask;
        private Task? _udpDiscoveryTask;

        private readonly MouseKeyboardService _mouseKeyboard;
        private readonly ScreenCaptureService _screenCapture;

        public event Action<string>? OnClientConnected;
        public event Action<string>? OnClientDisconnected;
        public event Action<string>? OnError;
        public event Action<string>? OnInfo;

        public bool IsConnected => _connectedClient?.Connected ?? false;
        public string? ConnectedClientIP { get; private set; }

        public LanService(MouseKeyboardService mouseKeyboard, ScreenCaptureService screenCapture)
        {
            _mouseKeyboard = mouseKeyboard;
            _screenCapture = screenCapture;
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();

            // 启动 TCP 指令监听
            _tcpListener = new TcpListener(IPAddress.Any, TCP_PORT);
            _tcpListener.Start();
            OnInfo?.Invoke($"TCP listening on port {TCP_PORT}");

            // 启动 UDP 设备发现
            StartDiscovery();

            // 启动 TCP 连接接受循环
            _tcpTask = Task.Run(() => AcceptLoop(_cts.Token));
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _tcpListener?.Stop();
            _tcpListener = null;
            _udpDiscovery?.Close();
            _udpDiscovery = null;
            DisconnectClient();
            OnInfo?.Invoke("LAN service stopped");
        }

        private void StartDiscovery()
        {
            try
            {
                _udpDiscovery = new UdpClient(UDP_DISCOVERY_PORT);
                _udpDiscovery.EnableBroadcast = true;
                _udpDiscoveryTask = Task.Run(() => DiscoveryLoop(_cts!.Token));
                OnInfo?.Invoke($"UDP discovery listening on port {UDP_DISCOVERY_PORT}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to start discovery: {ex.Message}");
            }
        }

        /// <summary>
        /// UDP 设备发现循环
        /// </summary>
        private async Task DiscoveryLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpDiscovery!.ReceiveAsync(ct);
                    var request = System.Text.Encoding.UTF8.GetString(result.Buffer);
                    
                    if (request == "TOUCHLINK_DISCOVER")
                    {
                        // 回复设备信息
                        var response = $"TOUCHLINK_HOST|{Environment.MachineName}|{GetLocalIP()}|{TCP_PORT}";
                        var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);
                        await _udpDiscovery.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                        OnInfo?.Invoke($"Discovery response sent to {result.RemoteEndPoint}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Discovery error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// TCP 连接接受循环
        /// </summary>
        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                    var remoteEP = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address;
                    if (_connectedClient != null && _connectedClient.Connected)
                    {
                        // 已有一个连接，拒绝新的
                        client.Close();
                        continue;
                    }

                    _connectedClient = client;
                    _clientStream = client.GetStream();
                    ConnectedClientIP = remoteEP?.ToString();
                    OnClientConnected?.Invoke(ConnectedClientIP ?? "unknown");
                    OnInfo?.Invoke($"Client connected: {ConnectedClientIP}");

                    // 启动画面串流
                    if (remoteEP != null)
                    {
                        _screenCapture.StartStreaming(remoteEP.ToString(), UDP_SCREEN_PORT);
                    }

                    // 处理指令
                    _ = Task.Run(() => CommandLoop(_cts!.Token));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// TCP 指令处理循环
        /// </summary>
        private async Task CommandLoop(CancellationToken ct)
        {
            var buffer = new byte[TouchCommand.PACKET_SIZE];

            while (!ct.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var bytesRead = await _clientStream!.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0)
                    {
                        // 连接断开
                        break;
                    }

                    var cmd = TouchCommand.FromBytes(buffer);
                    if (cmd.HasValue)
                    {
                        _mouseKeyboard.ExecuteCommand(cmd.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Command read error: {ex.Message}");
                    break;
                }
            }

            DisconnectClient();
        }

        private void DisconnectClient()
        {
            if (_connectedClient != null)
            {
                _screenCapture.StopStreaming();
                _connectedClient.Close();
                _connectedClient = null;
                _clientStream = null;
                OnClientDisconnected?.Invoke(ConnectedClientIP ?? "unknown");
                ConnectedClientIP = null;
            }
        }

        /// <summary>
        /// 获取本机局域网IP
        /// </summary>
        private static string GetLocalIP()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
