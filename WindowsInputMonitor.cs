using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace DesktopUsageAnalytics;

public sealed class WindowsInputMonitor : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int LLKHF_INJECTED = 0x10;
    private const int LLMHF_INJECTED = 0x01;

    private readonly LowLevelProc keyboardProc;
    private readonly LowLevelProc mouseProc;
    private readonly Action<InputBucket> onInput;
    private IntPtr keyboardHook;
    private IntPtr mouseHook;

    public WindowsInputMonitor(Action<InputBucket> onInput)
    {
        this.onInput = onInput;
        keyboardProc = KeyboardHookCallback;
        mouseProc = MouseHookCallback;
    }

    public bool IsRunning => keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;

    public string? LastError { get; private set; }

    public bool Start()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module?.ModuleName is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);

        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, moduleHandle, 0);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, moduleHandle, 0);

        if (IsRunning)
        {
            LastError = null;
            return true;
        }

        LastError = $"Unable to install global input hooks. Win32 error: {Marshal.GetLastWin32Error()}";
        Dispose();
        return false;
    }

    public void Dispose()
    {
        if (keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        if (mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((data.Flags & LLKHF_INJECTED) == 0)
            {
                var key = KeyInterop.KeyFromVirtualKey((int)data.VirtualKeyCode);
                var keyName = key == Key.None ? $"VK{data.VirtualKeyCode}" : key.ToString();
                onInput(InputBucket.Key(keyName));
            }
        }

        return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MouseLlHookStruct>(lParam);
            if ((data.Flags & LLMHF_INJECTED) == 0)
            {
                if (wParam == WM_LBUTTONDOWN)
                {
                    onInput(InputBucket.Mouse(MouseButtonBucket.Left));
                }
                else if (wParam == WM_RBUTTONDOWN)
                {
                    onInput(InputBucket.Mouse(MouseButtonBucket.Right));
                }
                else if (wParam == WM_MBUTTONDOWN)
                {
                    onInput(InputBucket.Mouse(MouseButtonBucket.Middle));
                }
            }
        }

        return CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public int Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLlHookStruct
    {
        public Point Point;
        public uint MouseData;
        public int Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
