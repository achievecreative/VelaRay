using System.Runtime.InteropServices;
using VelaRay.Core.Platform;
using VelaRay.Maui.Localization;

namespace VelaRay.Maui.Services;

/// <summary>Native Windows Shell_NotifyIcon tray; avoids mixing WinForms with MAUI/WinUI.</summary>
public sealed class WindowsTrayService : ITrayService
{
    private const uint WM_USER = 0x0400, WM_TRAY = WM_USER + 20, WM_REFRESH = WM_USER + 21;
    private const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 256;
    private const uint MF_STRING = 0, MF_GRAYED = 0x0001, MF_SEPARATOR = 0x800;
    private const int ShowCmd = 1001, ToggleCmd = 1002, ExitCmd = 1003, ConnectionInfoCmd = 1004;
    private Thread? _thread;
    private IntPtr _hwnd;
    private readonly object _gate = new();
    private bool _connected;
    private string _profile = "";
    private readonly string _className = "VelaRay.Tray." + Guid.NewGuid().ToString("N");
    private WndProc? _callback;
    private uint _taskbarCreated;
    private IntPtr _icon;
    private bool _disposed;
    private bool _iconLoadedFromFile;

    public event Action? ShowRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;

    public void Start()
    {
        if (_thread != null || _disposed) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "VelaRay Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void SetStatus(bool connected, string profile)
    {
        lock (_gate) { _connected = connected; _profile = profile; }
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero);
    }

    private string ExtractTrayIcon()
    {
        // The icon is embedded so the Windows single-file publish has no icon sidecar.
        var root = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        var dir = Path.Combine(root, "settings", "assets");
        Directory.CreateDirectory(dir);
        var iconPath = Path.Combine(dir, "velaray.ico");
        using var stream = typeof(WindowsTrayService).Assembly.GetManifestResourceStream("VelaRay.Assets.velaray.ico");
        if (stream is null) return iconPath;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var content = memory.ToArray();
        if (!File.Exists(iconPath) || !File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(content))
            File.WriteAllBytes(iconPath, content);
        _iconLoadedFromFile = true;
        return iconPath;
    }

    private void Run()
    {
        _callback = WindowProc;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        var hInstance = GetModuleHandleW(null);
        var wc = new WndClass { lpfnWndProc = _callback, hInstance = hInstance, lpszClassName = _className };
        if (RegisterClassW(ref wc) == 0) return;
        _hwnd = CreateWindowExW(0, _className, "VelaRay Tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;
        string originalIcon;
        try { originalIcon = ExtractTrayIcon(); }
        catch { originalIcon = ""; } // Fall back to the built-in Windows icon.
        _icon = File.Exists(originalIcon)
            ? LoadImageW(IntPtr.Zero, originalIcon, 1, 0, 0, 0x10 | 0x40)
            : IntPtr.Zero;
        _iconLoadedFromFile = _icon != IntPtr.Zero;
        if (_icon == IntPtr.Zero) _icon = LoadIconW(IntPtr.Zero, new IntPtr(32512));
        UpdateIcon(NIM_ADD);
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreated) { UpdateIcon(NIM_ADD); return IntPtr.Zero; }
        if (message == WM_TRAY)
        {
            var eventCode = unchecked((uint)lParam.ToInt64());
            if (eventCode is WM_LBUTTONUP or WM_LBUTTONDBLCLK) ShowRequested?.Invoke();
            else if (eventCode == WM_RBUTTONUP) ShowMenu(hwnd);
            return IntPtr.Zero;
        }
        if (message == WM_REFRESH) { UpdateIcon(NIM_MODIFY); return IntPtr.Zero; }
        if (message == WM_CLOSE) { DestroyWindow(hwnd); return IntPtr.Zero; }
        if (message == WM_DESTROY)
        {
            UpdateIcon(NIM_DELETE);
            if (_icon != IntPtr.Zero && _iconLoadedFromFile) DestroyIcon(_icon);
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void UpdateIcon(uint action)
    {
        if (_hwnd == IntPtr.Zero) return;
        string tip;
        lock (_gate) tip = $"VelaRay - {UiStrings.Instance.Translate(_connected ? "已连接" : "未连接")} {_profile}";
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = _hwnd, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_TRAY,
            hIcon = _icon, szTip = tip.Length > 127 ? tip[..127] : tip,
            szInfo = "", szInfoTitle = ""
        };
        Shell_NotifyIconW(action, ref data);
    }

    private void ShowMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            bool online;
            string profile;
            lock (_gate) { online = _connected; profile = _profile; }
            // A non-clickable status row displays the actual running profile snapshot,
            // not whichever profile happens to be selected in the editor.
            var status = online
                ? UiStrings.Instance["TrayConnectedServer"] + ": " + profile
                : UiStrings.Instance["TrayDisconnected"];
            if (status.Length > 78) status = status[..77] + "…";
            AppendMenuW(menu, MF_STRING | MF_GRAYED, ConnectionInfoCmd, status);
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, ShowCmd, UiStrings.Instance.Translate("打开 VelaRay"));
            AppendMenuW(menu, MF_STRING, ToggleCmd, UiStrings.Instance.Translate(online ? "断开代理" : "连接代理"));
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, ExitCmd, UiStrings.Instance.Translate("退出 VelaRay"));
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var selected = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.X, point.Y, 0, hwnd, IntPtr.Zero);
            if (selected == ShowCmd) ShowRequested?.Invoke();
            if (selected == ToggleCmd) ToggleRequested?.Invoke();
            if (selected == ExitCmd) ExitRequested?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hWnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WndClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string value);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exit);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr hInstance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr owner, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
}
