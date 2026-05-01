using System.Runtime.InteropServices;
using System.Text;

namespace AccountManager.Services;

public static class NativeClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroinit = 0x0040;

    public static Task<bool> SetTextAsync(string text, int retries = 10, int delayMs = 35)
    {
        return Task.Run(() => Retry(() => TrySetText(text), retries, delayMs));
    }

    public static Task<bool> ClearIfTextEqualsAsync(string expectedText, int retries = 6, int delayMs = 50)
    {
        return Task.Run(() => Retry(() => TryClearIfTextEquals(expectedText), retries, delayMs));
    }

    public static Task<string> GetTextAsync(int retries = 4, int delayMs = 35)
    {
        return Task.Run(() =>
        {
            for (var i = 0; i < retries; i++)
            {
                if (TryGetText(out var text)) return text;
                Thread.Sleep(delayMs);
            }

            return string.Empty;
        });
    }

    private static bool Retry(Func<bool> action, int retries, int delayMs)
    {
        for (var i = 0; i < retries; i++)
        {
            if (action()) return true;
            Thread.Sleep(delayMs);
        }

        return false;
    }

    private static bool TrySetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;

        var handle = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard()) return false;

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            handle = GlobalAlloc(GmemMoveable | GmemZeroinit, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero) return false;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero) return false;

            handle = IntPtr.Zero;
            return true;
        }
        finally
        {
            CloseClipboard();
            if (handle != IntPtr.Zero) GlobalFree(handle);
        }
    }

    private static bool TryGetText(out string text)
    {
        text = string.Empty;
        if (!OpenClipboard(IntPtr.Zero)) return false;

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText)) return true;

            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return false;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;

            try
            {
                text = Marshal.PtrToStringUni(pointer) ?? string.Empty;
                return true;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryClearIfTextEquals(string expectedText)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText)) return true;

            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return false;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;

            string current;
            try
            {
                current = Marshal.PtrToStringUni(pointer) ?? string.Empty;
            }
            finally
            {
                GlobalUnlock(handle);
            }

            return !string.Equals(current, expectedText, StringComparison.Ordinal) || EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
