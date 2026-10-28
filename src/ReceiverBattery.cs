using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Battery-query wire formats independently implemented from public protocol descriptions:
// https://github.com/LORQAQ/mchose-tray (legacy 11 06) and
// https://github.com/mactools-app/MacTools (modern 4D / readBasics 0900).
// Only battery/device-info queries are sent; settings are never changed.
internal static class ReceiverBattery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices;
        public ushort OutputButtons, OutputValues, OutputIndices;
        public ushort FeatureButtons, FeatureValues, FeatureIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, ref DeviceData device);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceData device, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint needed, uint flags);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, uint length);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    public static void Populate(List<Device> devices)
    {
        Guid hid;
        HidD_GetHidGuid(out hid);
        IntPtr set = SetupDiGetClassDevs(ref hid, null, IntPtr.Zero, 0x12);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return;
        var queried = new HashSet<Guid>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = (uint)Marshal.SizeOf(typeof(InterfaceData)) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, index, ref data)) break;
                var device = new DeviceData { Size = (uint)Marshal.SizeOf(typeof(DeviceData)) };
                uint needed;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out needed, ref device);
                if (needed < 6 || needed > 65536) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, needed, out needed, ref device)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (String.IsNullOrEmpty(path) ||
                        (path.IndexOf("vid_3837", StringComparison.OrdinalIgnoreCase) < 0 &&
                         path.IndexOf("vid_5253", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    var key = new PropertyKey { Format = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
                    uint type, length;
                    byte[] bytes = new byte[16];
                    if (!SetupDiGetDeviceProperty(set, ref device, ref key, out type, bytes, 16, out length, 0) ||
                        (type & 0xFFF) != 13 || length != 16) continue;
                    Guid container = new Guid(bytes);
                    if (container == Guid.Empty || queried.Contains(container)) continue;
                    List<Device> mice = devices.FindAll(delegate(Device d) {
                        return d.Container == container && d.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase);
                    });
                    if (mice.Count == 0) continue;
                    using (SafeFileHandle handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                    {
                        if (handle.IsInvalid) continue;
                        IntPtr preparsed;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        HidCaps caps;
                        bool legacy, modern;
                        try
                        {
                            if (HidP_GetCaps(preparsed, out caps) != 0x110000) continue;
                            legacy = caps.UsagePage == 0xFF01 && caps.Usage == 1 && caps.FeatureLength >= 65;
                            modern = caps.UsagePage == 0xFF01 && caps.Usage == 1 &&
                                caps.FeatureLength == 64 && caps.InputLength >= 22 && caps.OutputLength >= 9;
                        }
                        finally { HidD_FreePreparsedData(preparsed); }
                        if (!legacy && !modern) continue;
                        byte[] product = new byte[512];
                        string name = HidD_GetProductString(handle, product, (uint)product.Length)
                            ? Encoding.Unicode.GetString(product).TrimEnd('\0') : null;
                        foreach (Device mouse in mice)
                        {
                            if (!String.IsNullOrWhiteSpace(name)) mouse.Name = name;
                            mouse.BatterySource = "MCHOSE HID";
                        }
                        int level, mode;
                        bool charging;
                        bool read = modern
                            ? ReadModern(path, caps.InputLength, caps.OutputLength, out level, out mode, out charging)
                            : ReadBattery(handle, out level, out mode, out charging);
                        if (!read) continue;
                        queried.Add(container);
                        foreach (Device mouse in mice)
                        {
                            mouse.Level = level;
                            mouse.Connection = mode == (modern ? 2 : 1) ? "2.4G 无线" : "HID 连接";
                            mouse.Charging = charging;
                            mouse.BatteryReadAt = DateTime.Now;
                        }
                    }
                }
                catch (IOException) { } // A disconnected/busy interface must not abort other mice.
                catch (UnauthorizedAccessException) { }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static bool ReadModern(string path, int inputLength, int outputLength,
        out int level, out int mode, out bool charging)
    {
        level = -1; mode = -1; charging = false;
        if (inputLength > 4096 || outputLength > 4096) return false;
        using (SafeFileHandle handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
        {
            if (handle.IsInvalid) return false;
            using (var stream = new FileStream(handle, FileAccess.ReadWrite, inputLength, true))
            {
                byte[] query = new byte[outputLength];
                byte[] command = { 0x4D, 0x01, 0x01, 0x00, 0x00, 0x09, 0x00, 0x00, 0x08 };
                Array.Copy(command, query, command.Length);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool sent = false;
                while (watch.ElapsedMilliseconds < 900)
                {
                    byte[] response = new byte[inputLength];
                    IAsyncResult pending = stream.BeginRead(response, 0, response.Length, null, null);
                    using (var ready = pending.AsyncWaitHandle)
                    {
                        bool received = false;
                        try
                        {
                            if (!sent)
                            {
                                // This receiver replies to interrupt OUT writes, not SetOutputReport.
                                stream.Write(query, 0, query.Length);
                                sent = true;
                            }
                            received = ready.WaitOne((int)Math.Max(1, 900 - watch.ElapsedMilliseconds));
                        }
                        finally
                        {
                            if (!received)
                            {
                                CancelIoEx(handle, IntPtr.Zero);
                                // Complete cancellation before releasing the pinned read buffer.
                                try { stream.EndRead(pending); }
                                catch (IOException) { }
                                catch (OperationCanceledException) { }
                            }
                        }
                        if (!received) return false;
                        int count = stream.EndRead(pending);
                        if (count == 0) return false;
                        if (TryDecodeModern(response, count, out level, out mode, out charging)) return true;
                    }
                }
            }
        }
        return false;
    }

    internal static bool TryDecodeModern(byte[] report, int count, out int level, out int mode, out bool charging)
    {
        level = -1; mode = -1; charging = false;
        if (report == null || count > report.Length || count < 22 || report[0] != 0x4D ||
            report[1] != 1 || report[3] < 13 || report[3] + 9 > count) return false;
        int command = report[4] | (report[5] << 8);
        if (command != 0x0900 && command != 0x0010) return false;
        byte checksum = 0;
        for (int i = 2; i < 8 + report[3]; i++) checksum ^= report[i];
        if (checksum != report[8 + report[3]]) return false;
        int vendor = report[8] | (report[9] << 8);
        int product = report[10] | (report[11] << 8);
        int battery = report[20];
        if (vendor != 0x3837 || product == 0 || battery > 100 ||
            (battery == 0 && report[19] == 0)) return false;
        level = battery;
        mode = report[18] & 7;
        charging = report[19] != 0;
        return true;
    }

    private static bool ReadBattery(SafeFileHandle handle, out int level, out int mode, out bool charging)
    {
        level = -1; mode = -1; charging = false;
        byte[] request = new byte[21];
        request[0] = 0x11;
        for (int i = 1; i < request.Length; i++) request[i] = 0xFF;
        request[1] = 0xF9; // Read device info (0x06 XOR 0xFF).
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!HidD_SetFeature(handle, request, request.Length)) continue;
            System.Threading.Thread.Sleep(40);
            byte[] response = new byte[21];
            response[0] = 0x11;
            if (HidD_GetFeature(handle, response, response.Length) &&
                TryDecodeBattery(response, out level, out mode, out charging)) return true;
        }
        return false;
    }

    internal static bool TryDecodeBattery(byte[] report, out int level, out int mode, out bool charging)
    {
        level = -1; mode = -1; charging = false;
        if (report == null || report.Length < 13 || report[0] != 0x11 || report[1] != 0xF9) return false;
        int vendor = (report[2] ^ 0xFF) | ((report[3] ^ 0xFF) << 8);
        int product = (report[4] ^ 0xFF) | ((report[5] ^ 0xFF) << 8);
        int battery = report[11] ^ 0xFF;
        int charge = report[12] ^ 0xFF;
        // Reject empty/lagging frames, invalid percentages, and sleeping-receiver 0% values.
        if ((vendor != 0x3837 && vendor != 0x5253) || product == 0 ||
            battery > 100 || (battery == 0 && charge == 0)) return false;
        level = battery;
        mode = (report[10] ^ 0xFF) & 7;
        charging = charge != 0;
        return true;
    }
}
