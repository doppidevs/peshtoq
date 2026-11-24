using Windows.Networking.Connectivity;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Services;

public readonly record struct BatteryState(bool HasBattery, int Percent, bool Charging);
public readonly record struct RamState(double UsedGb, double TotalGb, double Percent);
public readonly record struct NetworkState(string Glyph, string Tooltip, bool Online);

public sealed class SystemInfo
{
    private long _prevIdle, _prevKernel, _prevUser;

    /// <summary>CPU load since the previous call, in percent.</summary>
    public double Cpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        long dIdle = idle - _prevIdle, dKernel = kernel - _prevKernel, dUser = user - _prevUser;
        bool first = _prevKernel == 0;
        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
        long total = dKernel + dUser; // kernel time includes idle time
        if (first || total <= 0) return 0;
        return Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
    }

    public RamState Ram()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return default;
        double total = m.ullTotalPhys / 1073741824.0;
        double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return new RamState(used, total, used / total * 100);
    }

    public BatteryState Battery()
    {
        if (!GetSystemPowerStatus(out var s)) return new BatteryState(false, 0, false);
        bool hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255 && s.BatteryLifePercent <= 100;
        return new BatteryState(hasBattery, Math.Min((int)s.BatteryLifePercent, 100), s.ACLineStatus == 1);
    }

    public static NetworkState Network()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return new NetworkState("", "Internetga ulanmagan", false);

            bool online = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            if (profile.IsWlanConnectionProfile)
            {
                byte bars = profile.GetSignalBars() ?? 5;
                string ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? profile.ProfileName;
                string glyph = bars switch { <= 1 => "", 2 => "", 3 => "", _ => "" };
                return new NetworkState(glyph, online ? $"Wi-Fi · {ssid}" : $"Wi-Fi · {ssid} (internet yo'q)", online);
            }
            return new NetworkState("", online ? $"Ethernet · {profile.ProfileName}" : "Ethernet (internet yo'q)", online);
        }
        catch
        {
            return new NetworkState("", "Tarmoq holati noma'lum", false);
        }
    }
}
