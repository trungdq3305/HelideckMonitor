using Microsoft.Win32;

namespace HelideckVer2.Services
{
    /// <summary>
    /// Scans the Windows registry for a connected Xsens MTi USB device
    /// (Vendor ID 0x2639) and returns the virtual COM port it was assigned.
    /// No extra packages required — uses built-in Microsoft.Win32.Registry.
    /// </summary>
    public static class XsensDetector
    {
        private const string XsensVid = "VID_2639";

        /// <summary>
        /// Returns the COM port name (e.g. "COM6") of the first connected Xsens
        /// MTi USB device, or null if none is found.
        /// </summary>
        public static string FindPort()
        {
            try
            {
                using var usbKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB");
                if (usbKey == null) return null;

                foreach (var vidPid in usbKey.GetSubKeyNames())
                {
                    if (!vidPid.StartsWith(XsensVid, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var vidKey = usbKey.OpenSubKey(vidPid);
                    if (vidKey == null) continue;

                    foreach (var instance in vidKey.GetSubKeyNames())
                    {
                        using var instKey = vidKey.OpenSubKey(instance);
                        using var devParams = instKey?.OpenSubKey("Device Parameters");
                        if (devParams == null) continue;

                        string port = devParams.GetValue("PortName") as string;
                        if (!string.IsNullOrEmpty(port))
                            return port.Trim();
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
