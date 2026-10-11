// Windows 10/11 notification-area indicator for the mouse currently sending input.
// Build from the repository root with build.bat.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool first;
        using (var mutex = new System.Threading.Mutex(true, "Local\\MouseBatteryTray", out first))
        {
            if (!first) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BatteryMonitor());
        }
    }
}

internal sealed class Device
{
    public string Id, Name, DeviceClass;
    public Guid Container;
    public int Level = -1;
}

internal static class Pnp
{
    private const uint DIGCF_PRESENT = 2, DIGCF_ALLCLASSES = 4;
    private const uint SPDRP_DEVICEDESC = 0, SPDRP_CLASS = 7, SPDRP_FRIENDLYNAME = 12;
    private static readonly DEVPROPKEY BatteryKey = new DEVPROPKEY {
        fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2
    };
    private static readonly DEVPROPKEY ContainerKey = new DEVPROPKEY {
        fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info,
        ref DEVPROPKEY key, out uint type, byte[] value, uint capacity, out uint length, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA info,
        uint property, out uint type, byte[] value, uint capacity, out uint length);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA info,
        StringBuilder value, int capacity, out int length);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    private static string GetText(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        uint type, length;
        byte[] bytes = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out type,
                bytes, (uint)bytes.Length, out length)) return null;
        return Encoding.Unicode.GetString(bytes, 0, (int)length).TrimEnd('\0');
    }

    public static List<Device> Scan()
    {
        var devices = new List<Device>();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return devices;
        try
        {
            for (uint i = 0; ; ++i)
            {
                SP_DEVINFO_DATA info = new SP_DEVINFO_DATA();
                info.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) break;
                string cls = GetText(set, ref info, SPDRP_CLASS) ?? "";
                uint type, length;
                byte[] battery = new byte[4];
                DEVPROPKEY key = BatteryKey;
                bool hasBattery = SetupDiGetDeviceProperty(set, ref info, ref key, out type,
                    battery, (uint)battery.Length, out length, 0)
                    && (type & 0xFFF) == 3 && length >= 1 && battery[0] <= 100;
                if (!hasBattery && !cls.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) continue;
                StringBuilder id = new StringBuilder(1024);
                int needed;
                if (!SetupDiGetDeviceInstanceId(set, ref info, id, id.Capacity, out needed)) continue;
                string name = GetText(set, ref info, SPDRP_FRIENDLYNAME);
                if (String.IsNullOrEmpty(name)) name = GetText(set, ref info, SPDRP_DEVICEDESC);
                if (String.IsNullOrEmpty(name)) name = id.ToString();
                byte[] guid = new byte[16];
                key = ContainerKey;
                Guid container = Guid.Empty;
                if (SetupDiGetDeviceProperty(set, ref info, ref key, out type,
                        guid, (uint)guid.Length, out length, 0) && (type & 0xFFF) == 13 && length == 16)
                    container = new Guid(guid);
                devices.Add(new Device {
                    Id = id.ToString(), Name = name, DeviceClass = cls, Container = container,
                    Level = hasBattery ? battery[0] : -1
                });
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }
}

internal static class RawMouse
{
    public const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const uint RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
    private const uint RID_HEADER = 0x10000005, RIDI_DEVICENAME = 0x20000007;
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usagePage, usage;
        public uint flags;
        public IntPtr target;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint structSize);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr input, uint command, byte[] header,
        ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder value, ref uint count);

    public static bool Register(IntPtr hwnd)
    {
        var devices = new[] { new RAWINPUTDEVICE {
            usagePage = 1, usage = 2, flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, target = hwnd
        } };
        return RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
    }

    public static IntPtr DeviceFromMessage(IntPtr lParam)
    {
        uint headerBytes = (uint)(8 + 2 * IntPtr.Size);
        byte[] header = new byte[headerBytes];
        uint size = headerBytes;
        if (GetRawInputData(lParam, RID_HEADER, header, ref size, headerBytes) == UInt32.MaxValue)
            return IntPtr.Zero;
        if (BitConverter.ToUInt32(header, 0) != 0) return IntPtr.Zero;
        return IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(header, 8))
            : new IntPtr(BitConverter.ToInt32(header, 8));
    }

    public static string DevicePath(IntPtr device)
    {
        uint count = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref count);
        if (count == 0 || count > 4096) return null;
        StringBuilder result = new StringBuilder((int)count + 1);
        if (GetRawInputDeviceInfo(device, RIDI_DEVICENAME, result, ref count) == UInt32.MaxValue)
            return null;
        return result.ToString();
    }
}

internal sealed class BatteryMonitor : Form
{
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenu menu = new ContextMenu();
    private readonly Timer timer = new Timer();
    private readonly Dictionary<IntPtr, string> paths = new Dictionary<IntPtr, string>();
    private List<Device> devices = new List<Device>();
    private Icon currentIcon;
    private string activePath;
    private string pinnedId;
    private bool inputRegistered;

    // Keep a message-only purpose HWND; the only visible UI is the native taskbar tray icon.
    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

    public BatteryMonitor()
    {
        ShowInTaskbar = false;
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\MouseBatteryTray"))
            pinnedId = key == null ? null : key.GetValue("PinnedDeviceId") as string;
        menu.MenuItems.Add(new MenuItem("正在检测鼠标…") { Enabled = false });
        menu.MenuItems.Add(new MenuItem("设备选择") { Enabled = false });
        menu.MenuItems.Add("立即刷新", delegate { RefreshNow(); });
        menu.MenuItems.Add("退出", delegate { Close(); });
        tray.ContextMenu = menu;
        tray.DoubleClick += delegate { RefreshNow(); };
        inputRegistered = RawMouse.Register(Handle);
        RefreshNow();
        tray.Visible = true;
        timer.Interval = 30000;
        timer.Tick += delegate { RefreshNow(); };
        timer.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawMouse.WM_INPUT)
        {
            IntPtr device = RawMouse.DeviceFromMessage(m.LParam);
            if (device != IntPtr.Zero)
            {
                string path;
                if (!paths.TryGetValue(device, out path))
                {
                    path = RawMouse.DevicePath(device);
                    if (path != null) paths[device] = path;
                }
                if (path != null && !String.Equals(path, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    activePath = path;
                    RefreshNow();
                }
            }
        }
        else if (m.Msg == RawMouse.WM_INPUT_DEVICE_CHANGE)
        {
            paths.Clear();
            RefreshNow();
        }
        base.WndProc(ref m);
    }

    private Device ActiveDevice()
    {
        if (!String.IsNullOrEmpty(pinnedId))
            return devices.Find(delegate(Device d) { return d.Id == pinnedId; });
        if (String.IsNullOrEmpty(activePath)) return null;
        string id = activePath;
        if (id.StartsWith(@"\\?\", StringComparison.Ordinal)) id = id.Substring(4);
        int end = id.LastIndexOf('#');
        if (end > 0) id = id.Substring(0, end);
        id = id.Replace('#', '\\');
        Device mouse = devices.Find(delegate(Device d) {
            return d.Id.Equals(id, StringComparison.OrdinalIgnoreCase);
        });
        if (mouse == null)
        {
            // HID over Bluetooth and many receiver devices share an identifiable
            // VID/PID prefix with the PnP node, but exact instance names can differ.
            string canonical = id.ToUpperInvariant();
            mouse = devices.Find(delegate(Device d) {
                return d.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
                    && canonical.Contains(d.Id.ToUpperInvariant());
            });
        }
        if (mouse == null) return null;
        if (mouse.Level >= 0) return mouse;
        if (mouse.Container != Guid.Empty)
        {
            Device battery = devices.Find(delegate(Device d) {
                return d.Container == mouse.Container && d.Level >= 0;
            });
            if (battery != null) return battery;
        }
        return mouse;
    }

    private void RefreshNow()
    {
        try { devices = Pnp.Scan(); }
        catch { Display("?", "无法读取鼠标设备信息", Color.Gray); return; }
        BuildDeviceMenu();
        Device active = ActiveDevice();
        if (active == null)
        {
            Display("?", inputRegistered ? "请移动正在使用的鼠标以识别设备" : "无法监听鼠标输入", Color.Gray);
        }
        else if (active.Level >= 0)
        {
            Color color = active.Level <= 15 ? Color.FromArgb(220, 69, 65) :
                active.Level <= 30 ? Color.FromArgb(224, 160, 40) : Color.FromArgb(45, 170, 91);
            Display(active.Level.ToString(), active.Name + "  " + active.Level + "%", color);
        }
        else Display("?", active.Name + "：已识别，Windows 未提供电量", Color.Gray);
    }

    private void BuildDeviceMenu()
    {
        MenuItem selection = menu.MenuItems[1];
        selection.MenuItems.Clear();
        selection.Enabled = true;
        MenuItem automatic = new MenuItem("跟随当前使用的鼠标", delegate {
            pinnedId = null;
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\MouseBatteryTray"))
                key.DeleteValue("PinnedDeviceId", false);
            RefreshNow();
        });
        automatic.Checked = String.IsNullOrEmpty(pinnedId);
        selection.MenuItems.Add(automatic);
        foreach (Device device in devices)
        {
            if (device.Level < 0 && !device.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) continue;
            Device choice = device;
            MenuItem item = new MenuItem(device.Name + "  " +
                (device.Level >= 0 ? device.Level + "%" : "电量未知"), delegate {
                pinnedId = choice.Id;
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\MouseBatteryTray"))
                    key.SetValue("PinnedDeviceId", pinnedId);
                RefreshNow();
            });
            item.Checked = pinnedId == device.Id;
            selection.MenuItems.Add(item);
        }
    }

    private void Display(string text, string tooltip, Color color)
    {
        tray.Text = tooltip.Length <= 63 ? tooltip : tooltip.Substring(0, 63);
        menu.MenuItems[0].Text = tooltip;
        Icon next = MakeIcon(text, color);
        Icon old = currentIcon;
        currentIcon = next;
        tray.Icon = next;
        if (old != null) old.Dispose();
    }

    private static Icon MakeIcon(string number, Color color)
    {
        using (var bitmap = new Bitmap(48, 48))
        using (var g = Graphics.FromImage(bitmap))
        using (var body = RoundedRect(new RectangleF(2.5F, 8.5F, 39F, 31F), 7F))
        using (var cap = RoundedRect(new RectangleF(41.5F, 18F, 4.5F, 12F), 2F))
        using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(2, 8, 40, 32), color,
            Color.FromArgb((int)(color.R * 0.72F), (int)(color.G * 0.72F),
                (int)(color.B * 0.72F)), 90F))
        using (var outline = new Pen(Color.FromArgb(218, 255, 255, 255), 1.4F))
        using (var font = new Font("Segoe UI", number.Length == 3 ? 17F : 21F,
            FontStyle.Bold, GraphicsUnit.Pixel))
        using (var layout = new StringFormat())
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            g.FillPath(fill, body);
            using (var tip = new SolidBrush(color)) g.FillPath(tip, cap);
            g.DrawPath(outline, body);
            layout.Alignment = StringAlignment.Center;
            layout.LineAlignment = StringAlignment.Center;
            g.DrawString(number, font, Brushes.White,
                new RectangleF(4F, 9F, 36F, 27F), layout);
            int level;
            if (Int32.TryParse(number, out level))
            {
                using (var track = new SolidBrush(Color.FromArgb(85, 255, 255, 255)))
                    g.FillRectangle(track, 8F, 35F, 28F, 2F);
                using (var progress = new SolidBrush(Color.White))
                    g.FillRectangle(progress, 8F, 35F, 28F * level / 100F, 2F);
            }
            IntPtr handle = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = radius * 2F;
        path.AddArc(r.X, r.Y, d, d, 180F, 90F);
        path.AddArc(r.Right - d, r.Y, d, d, 270F, 90F);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0F, 90F);
        path.AddArc(r.X, r.Bottom - d, d, d, 90F, 90F);
        path.CloseFigure();
        return path;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        tray.Visible = false;
        tray.Dispose();
        if (currentIcon != null) currentIcon.Dispose();
        menu.Dispose();
        timer.Dispose();
        base.OnFormClosed(e);
    }
}
