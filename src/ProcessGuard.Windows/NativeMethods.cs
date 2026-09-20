using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ProcessGuard.Windows;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsProcessCritical(SafeProcessHandle handle, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll")]
    internal static extern uint GetActiveProcessorCount(ushort group);
}
