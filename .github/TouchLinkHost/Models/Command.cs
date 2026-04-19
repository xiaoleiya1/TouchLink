using System;

namespace TouchLinkHost.Models
{
    /// <summary>
    /// 指令类型枚举
    /// </summary>
    public enum CommandType : byte
    {
        MouseMove = 0x01,
        MouseLeftDown = 0x02,
        MouseLeftUp = 0x03,
        MouseRightDown = 0x04,
        MouseRightUp = 0x05,
        MouseScroll = 0x06,
        MouseDrag = 0x07,
        KeyboardShortcut = 0x08,
        ClipboardText = 0x09,
        FileTransfer = 0x0A,
        DisplaySelect = 0x0B,
        AudioStream = 0x0C,
        Ping = 0xFE,
        Pong = 0xFF
    }

    /// <summary>
    /// 触控指令结构 (二进制协议)
    /// Header(2) + Type(1) + X(2) + Y(2) + Extra(2) = 9 bytes (+ optional text data)
    /// </summary>
    public struct TouchCommand
    {
        public const byte HEADER1 = 0x54; // 'T'
        public const byte HEADER2 = 0x4C; // 'L'
        public const int PACKET_SIZE = 9;
        public const int MAX_TEXT_SIZE = 4096;

        public CommandType Type { get; set; }
        public short X { get; set; }
        public short Y { get; set; }
        public short Extra { get; set; }
        public string? Text { get; set; }

        public TouchCommand(CommandType type, short x, short y, short extra = 0, string? text = null)
        {
            Type = type;
            X = x;
            Y = y;
            Extra = extra;
            Text = text;
        }

        /// <summary>
        /// 从字节数组解析指令
        /// </summary>
        public static TouchCommand? FromBytes(byte[] data)
        {
            if (data == null || data.Length < PACKET_SIZE)
                return null;

            if (data[0] != HEADER1 || data[1] != HEADER2)
                return null;

            var type = (CommandType)data[2];
            var x = BitConverter.ToInt16(data, 3);
            var y = BitConverter.ToInt16(data, 5);
            var extra = BitConverter.ToInt16(data, 7);

            return new TouchCommand(type, x, y, extra);
        }

        /// <summary>
        /// 序列化为字节数组
        /// </summary>
        public byte[] ToBytes()
        {
            var data = new byte[PACKET_SIZE];
            data[0] = HEADER1;
            data[1] = HEADER2;
            data[2] = (byte)Type;
            var xBytes = BitConverter.GetBytes(X);
            var yBytes = BitConverter.GetBytes(Y);
            var extraBytes = BitConverter.GetBytes(Extra);
            Array.Copy(xBytes, 0, data, 3, 2);
            Array.Copy(yBytes, 0, data, 5, 2);
            Array.Copy(extraBytes, 0, data, 7, 2);
            return data;
        }

        public override string ToString()
        {
            return $"Cmd[{Type}] X={X} Y={Y} Extra={Extra}" + (Text != null ? $" Text={Text.Length}" : "");
        }
    }

    /// <summary>
    /// 快捷键定义
    /// </summary>
    public static class KeyboardShortcuts
    {
        public const short Copy = 0x0001;
        public const short Paste = 0x0002;
        public const short Cut = 0x0003;
        public const short SelectAll = 0x0004;
        public const short Undo = 0x0005;
        public const short Redo = 0x0006;
        public const short Save = 0x0007;
        public const short Close = 0x0008;
        public const short NewTab = 0x0009;
        public const short SwitchTab = 0x000A;
        public const short Refresh = 0x000B;
        public const short FullScreen = 0x000C;
        public const short VolumeUp = 0x0010;
        public const short VolumeDown = 0x0011;
        public const short Mute = 0x0012;
        public const short PlayPause = 0x0013;
        public const short NextTrack = 0x0014;
        public const short PrevTrack = 0x0015;
        public const short ShowDesktop = 0x0020;
        public const short TaskView = 0x0021;
        public const short Lock = 0x0022;
        public const short AltTab = 0x0023;
        public const short CtrlTab = 0x0024;
        public const short Escape = 0x0025;
        public const short Enter = 0x0026;
        
        // Three-finger gestures
        public const short ThreeFingerUp = 0x0030;
        public const short ThreeFingerDown = 0x0031;
        public const short ThreeFingerLeft = 0x0032;
        public const short ThreeFingerRight = 0x0033;
    }

    /// <summary>
    /// 屏幕质量级别
    /// </summary>
    public enum ScreenQuality
    {
        Low,    // 15fps 720p
        Medium, // 30fps 1080p
        High    // 60fps 1080p
    }

    /// <summary>
    /// 显示信息
    /// </summary>
    public class DisplayInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
    }
}
