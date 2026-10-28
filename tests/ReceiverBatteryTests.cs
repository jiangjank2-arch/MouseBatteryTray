using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

internal static class ReceiverBatteryTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static byte[] ModernFrame()
    {
        // Captured from an A7 V3 Ultra+ using its 2.4 GHz receiver.
        return new byte[] { 0x4D, 1, 1, 0x10, 0, 9, 0, 0, 0x37, 0x38, 0x26, 0x40,
            4, 0, 0, 0, 0, 0x10, 2, 0, 0x64, 0, 8, 0xE4, 0xEF };
    }

    private static void Checksum(byte[] frame)
    {
        byte sum = 0;
        for (int i = 2; i < 8 + frame[3]; i++) sum ^= frame[i];
        frame[8 + frame[3]] = sum;
    }

    [STAThread]
    private static void Main()
    {
        int level, mode;
        bool charging;
        byte[] frame = ModernFrame();
        Check(ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging) &&
            level == 100 && mode == 2 && !charging, "captured 2.4G response = 100%");
        frame[20] = 73; Checksum(frame);
        Check(ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging) && level == 73,
            "percentage comes from the received payload");
        frame[19] = 1; frame[20] = 0; Checksum(frame);
        Check(ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging) &&
            level == 0 && charging, "charging from zero");
        frame[19] = 0; Checksum(frame);
        Check(!ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging),
            "sleeping receiver zero is not a measured empty battery");
        frame = ModernFrame(); frame[20] = 101; Checksum(frame);
        Check(!ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging), "reject out-of-range battery");
        frame = ModernFrame(); frame[24] ^= 1;
        Check(!ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging), "reject bad checksum");
        frame = ModernFrame(); frame[5] = 8; Checksum(frame);
        Check(!ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging), "reject unrelated command");
        frame = ModernFrame(); frame[8] = 0; Checksum(frame);
        Check(!ReceiverBattery.TryDecodeModern(frame, frame.Length, out level, out mode, out charging), "reject different vendor");
        frame = ModernFrame();
        Check(!ReceiverBattery.TryDecodeModern(frame, 21, out level, out mode, out charging) &&
            !ReceiverBattery.TryDecodeModern(frame, 100, out level, out mode, out charging) &&
            !ReceiverBattery.TryDecodeModern(null, 0, out level, out mode, out charging), "reject truncated or invalid reports");

        byte[] legacy = new byte[21];
        for (int i = 1; i < legacy.Length; i++) legacy[i] = 0xFF;
        legacy[0] = 0x11; legacy[1] = 0xF9; legacy[2] = 0xC8; legacy[3] = 0xC7;
        legacy[4] = 0xFE; legacy[10] = 0xFE; legacy[11] = (byte)(42 ^ 0xFF);
        Check(ReceiverBattery.TryDecodeBattery(legacy, out level, out mode, out charging) &&
            level == 42 && mode == 1 && !charging, "legacy receiver percentage");

        Application.EnableVisualStyles();
        using (var monitor = new BatteryMonitor())
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(BatteryMonitor);
            var refreshing = type.GetField("refreshing", flags);
            DateTime deadline = DateTime.UtcNow.AddSeconds(6);
            while ((bool)refreshing.GetValue(monitor) && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Check(!(bool)refreshing.GetValue(monitor), "background scan completes without blocking UI");
            var live = (List<Device>)type.GetField("devices", flags).GetValue(monitor);
            foreach (Device device in live)
                Console.WriteLine("LIVE " + device.Name + " | " + device.Connection + " | " + device.Level + "%");
            Console.WriteLine("TRAY " + ((NotifyIcon)type.GetField("tray", flags).GetValue(monitor)).Text);

            var mouse = new Device { Id = @"HID\VID_3837&PID_1018&MI_00\test", Name = "Mouse",
                DeviceClass = "Mouse", Level = 73, Container = Guid.NewGuid() };
            var touchpad = new Device { Id = @"HID\TOUCHPAD\test", Name = "Touchpad",
                DeviceClass = "Mouse", Container = Guid.NewGuid() };
            type.GetField("pinnedId", flags).SetValue(monitor, null);
            type.GetField("devices", flags).SetValue(monitor, new List<Device> { mouse, touchpad });
            type.GetField("activePath", flags).SetValue(monitor, null);
            var active = type.GetMethod("ActiveDevice", flags);
            Check(Object.ReferenceEquals(active.Invoke(monitor, null), mouse), "startup selects the only battery-capable mouse");
            type.GetField("pinnedId", flags).SetValue(monitor, "disconnected-bluetooth-device");
            Check(Object.ReferenceEquals(active.Invoke(monitor, null), mouse), "disconnected pinned Bluetooth device falls back to receiver");
            type.GetField("pinnedId", flags).SetValue(monitor, null);
            type.GetField("activePath", flags).SetValue(monitor, @"\\?\HID#TOUCHPAD#test#{00000000-0000-0000-0000-000000000000}");
            Check(Object.ReferenceEquals(active.Invoke(monitor, null), touchpad), "touchpad never inherits receiver battery");
            type.GetField("activePath", flags).SetValue(monitor, @"\\?\HID#VID_3837&PID_1018&MI_00#test#{00000000-0000-0000-0000-000000000000}");
            Check(Object.ReferenceEquals(active.Invoke(monitor, null), mouse), "raw input resolves to the matching mouse");
            monitor.Close();
        }
        Console.WriteLine("Passed " + passed + " checks.");
    }
}
