using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TouchLinkHost.Models;

namespace TouchLinkHost.Services
{
    /// <summary>
    /// 鼠标键盘模拟服务 - 使用 user32.dll P/Invoke
    /// </summary>
    public class MouseKeyboardService : IDisposable
    {
        #region Windows API Imports

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint CF_UNICODETEXT = 0x000D;

        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_ALT = 0x12;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_TAB = 0x09;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_D = 0x44;
        private const byte VK_L = 0x4C;

        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        private bool _disposed = false;
        private int _screenWidth;
        private int _screenHeight;
        private POINT _lastPos;
        private bool _useAbsoluteCoordinates = false;

        // Pointer acceleration
        private double _pointerSpeed = 1.0;

        public MouseKeyboardService()
        {
            _screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            _screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (_screenWidth <= 0) _screenWidth = 1920;
            if (_screenHeight <= 0) _screenHeight = 1080;
            _useAbsoluteCoordinates = _screenWidth > 0 && _screenHeight > 0;
        }

        /// <summary>
        /// 设置指针移动速度
        /// </summary>
        public void SetPointerSpeed(double speed)
        {
            _pointerSpeed = Math.Max(0.5, Math.Min(2.0, speed));
        }

        /// <summary>
        /// 执行指令
        /// </summary>
        public void ExecuteCommand(TouchCommand cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.MouseMove:
                    MoveMouse(cmd.X, cmd.Y);
                    break;
                case CommandType.MouseLeftDown:
                    MouseLeftDown();
                    break;
                case CommandType.MouseLeftUp:
                    MouseLeftUp();
                    break;
                case CommandType.MouseRightDown:
                    MouseRightDown();
                    break;
                case CommandType.MouseRightUp:
                    MouseRightUp();
                    break;
                case CommandType.MouseScroll:
                    MouseScroll(cmd.Extra);
                    break;
                case CommandType.MouseDrag:
                    MouseDrag(cmd.X, cmd.Y);
                    break;
                case CommandType.KeyboardShortcut:
                    ExecuteKeyboardShortcut(cmd.Extra);
                    break;
                case CommandType.ClipboardText:
                    SetClipboardText(cmd.Text);
                    break;
                case CommandType.DisplaySelect:
                    SelectDisplay(cmd.Extra);
                    break;
            }
        }

        /// <summary>
        /// 移动鼠标 (相对移动，带加速)
        /// </summary>
        public void MoveMouse(short deltaX, short deltaY)
        {
            // Apply pointer acceleration curve
            double acceleratedX = deltaX * _pointerSpeed;
            double acceleratedY = deltaY * _pointerSpeed;
            
            GetCursorPos(out _lastPos);
            int newX = _lastPos.X + (int)acceleratedX;
            int newY = _lastPos.Y + (int)acceleratedY;
            
            // Clamp to screen bounds
            newX = Math.Max(0, Math.Min(_screenWidth - 1, newX));
            newY = Math.Max(0, Math.Min(_screenHeight - 1, newY));
            
            SetCursorPos(newX, newY);
        }

        /// <summary>
        /// 鼠标左键按下
        /// </summary>
        public void MouseLeftDown()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        }

        /// <summary>
        /// 鼠标左键抬起
        /// </summary>
        public void MouseLeftUp()
        {
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        /// <summary>
        /// 鼠标右键按下
        /// </summary>
        public void MouseRightDown()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
        }

        /// <summary>
        /// 鼠标右键抬起
        /// </summary>
        public void MouseRightUp()
        {
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        }

        /// <summary>
        /// 鼠标滚轮滚动
        /// </summary>
        public void MouseScroll(short delta)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta * 120, 0);
        }

        /// <summary>
        /// 鼠标拖拽 (按下→移动→抬起)
        /// </summary>
        public void MouseDrag(short deltaX, short deltaY)
        {
            MouseLeftDown();
            Thread.Sleep(10);
            MoveMouse(deltaX, deltaY);
            Thread.Sleep(10);
            MouseLeftUp();
        }

        /// <summary>
        /// 设置剪贴板文本
        /// </summary>
        public void SetClipboardText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            try
            {
                OpenClipboard(IntPtr.Zero);
                if (GetClipboardData(CF_UNICODETEXT) != IntPtr.Zero)
                {
                    // Clear existing
                }
                
                IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)((text.Length + 1) * 2));
                IntPtr pMem = GlobalLock(hMem);
                
                // Copy string to memory
                System.Text.Encoding.Unicode.GetBytes(text + "\0", 0, text.Length + 1, 
                    new byte[(text.Length + 1) * 2], 0);
                
                Marshal.Copy(System.Text.Encoding.Unicode.GetBytes(text + "\0"), 0, pMem, (text.Length + 1) * 2);
                GlobalUnlock(hMem);
                
                SetClipboardData(CF_UNICODETEXT, hMem);
                CloseClipboard();
                
                // Auto-paste after setting clipboard
                Thread.Sleep(50);
                SendCtrlKey(0x56); // V
            }
            catch (Exception)
            {
                // Fallback to .NET clipboard
                try
                {
                    Clipboard.SetText(text);
                    Thread.Sleep(50);
                    SendCtrlKey(0x56);
                }
                catch { }
            }
        }

        /// <summary>
        /// 选择显示器
        /// </summary>
        public void SelectDisplay(short displayId)
        {
            // Display selection is handled by moving mouse to target display
            // This is a placeholder for multi-monitor support
            var screens = Screen.AllScreens;
            if (displayId >= 0 && displayId < screens.Length)
            {
                var bounds = screens[displayId].Bounds;
                SetCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            }
        }

        /// <summary>
        /// 获取所有显示器信息
        /// </summary>
        public DisplayInfo[] GetDisplays()
        {
            var screens = Screen.AllScreens;
            var displays = new DisplayInfo[screens.Length];
            
            for (int i = 0; i < screens.Length; i++)
            {
                displays[i] = new DisplayInfo
                {
                    Id = i,
                    Name = screens[i].DeviceName,
                    Width = screens[i].Bounds.Width,
                    Height = screens[i].Bounds.Height,
                    IsPrimary = screens[i].Primary
                };
            }
            
            return displays;
        }

        /// <summary>
        /// 执行键盘快捷键
        /// </summary>
        public void ExecuteKeyboardShortcut(short shortcut)
        {
            switch (shortcut)
            {
                case KeyboardShortcuts.Copy:
                    SendCtrlKey(0x43); // C
                    break;
                case KeyboardShortcuts.Paste:
                    SendCtrlKey(0x56); // V
                    break;
                case KeyboardShortcuts.Cut:
                    SendCtrlKey(0x58); // X
                    break;
                case KeyboardShortcuts.SelectAll:
                    SendCtrlKey(0x41); // A
                    break;
                case KeyboardShortcuts.Undo:
                    SendCtrlKey(0x5A); // Z
                    break;
                case KeyboardShortcuts.Redo:
                    SendCtrlShiftKey(0x5A); // Ctrl+Shift+Z
                    break;
                case KeyboardShortcuts.Save:
                    SendCtrlKey(0x53); // S
                    break;
                case KeyboardShortcuts.Close:
                    SendAltKey(0x73); // F4
                    break;
                case KeyboardShortcuts.NewTab:
                    SendCtrlKey(0x54); // T
                    break;
                case KeyboardShortcuts.SwitchTab:
                    SendCtrlKey(0x09); // Tab
                    break;
                case KeyboardShortcuts.Refresh:
                    SendCtrlKey(0x52); // R
                    break;
                case KeyboardShortcuts.FullScreen:
                    SendKey(VK_LWIN, 0x36); // Win+F
                    break;
                case KeyboardShortcuts.VolumeUp:
                    SendKey(VK_VOLUME_UP);
                    break;
                case KeyboardShortcuts.VolumeDown:
                    SendKey(VK_VOLUME_DOWN);
                    break;
                case KeyboardShortcuts.Mute:
                    SendKey(VK_VOLUME_MUTE);
                    break;
                case KeyboardShortcuts.PlayPause:
                    SendKey(VK_MEDIA_PLAY_PAUSE);
                    break;
                case KeyboardShortcuts.NextTrack:
                    SendKey(VK_MEDIA_NEXT_TRACK);
                    break;
                case KeyboardShortcuts.PrevTrack:
                    SendKey(VK_MEDIA_PREV_TRACK);
                    break;
                case KeyboardShortcuts.ShowDesktop:
                    SendWinKey(VK_D); // Win+D
                    break;
                case KeyboardShortcuts.TaskView:
                    SendWinKey(VK_TAB); // Win+Tab
                    break;
                case KeyboardShortcuts.Lock:
                    SendCtrlAltKey(VK_ESCAPE); // Ctrl+Alt+Del
                    break;
                case KeyboardShortcuts.AltTab:
                    SendAltKey(VK_TAB);
                    break;
                case KeyboardShortcuts.Escape:
                    SendKey(VK_ESCAPE);
                    break;
                case KeyboardShortcuts.Enter:
                    SendKey(VK_RETURN);
                    break;
                    
                // Three-finger gestures
                case KeyboardShortcuts.ThreeFingerUp:
                    SendWinKey(VK_D); // Show desktop
                    break;
                case KeyboardShortcuts.ThreeFingerDown:
                    SendWinKey(VK_TAB); // Task view
                    break;
                case KeyboardShortcuts.ThreeFingerLeft:
                case KeyboardShortcuts.ThreeFingerRight:
                    SendAltKey(VK_TAB); // Alt+Tab (switch apps)
                    break;
            }
        }

        private void SendCtrlKey(byte key)
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        private void SendAltKey(byte key)
        {
            keybd_event(VK_ALT, 0, 0, 0);
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, 0);
        }

        private void SendCtrlShiftKey(byte key)
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_SHIFT, 0, 0, 0);
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        private void SendCtrlAltKey(byte key)
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_ALT, 0, 0, 0);
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        private void SendWinKey(byte key)
        {
            keybd_event(VK_LWIN, 0, 0, 0);
            keybd_event(key, 0, 0, 0);
            keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
        }

        private void SendKey(byte key, byte scan = 0)
        {
            keybd_event(key, scan, 0, 0);
            keybd_event(key, scan, KEYEVENTF_KEYUP, 0);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
