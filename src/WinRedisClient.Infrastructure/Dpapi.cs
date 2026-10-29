using System.Runtime.InteropServices;
using System.Security.Cryptography;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Infrastructure;

/// <summary>DPAPI（CurrentUser）加解密，用于本地凭证落盘。加密失败时抛出，绝不回退明文。</summary>
public static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;
    private const string Prefix = "dpapi:v1:";

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException(Loc.T("待加密内容不能为空"), nameof(plaintext));

        var data = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = Transform(data, protect: true);
        return Prefix + Convert.ToBase64String(cipher);
    }

    public static string? TryUnprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        try
        {
            var cipher = Convert.FromBase64String(protectedText[Prefix.Length..]);
            var data = Transform(cipher, protect: false);
            return System.Text.Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var output = default(DataBlob);
        try
        {
            var inputBlob = new DataBlob
            {
                cbData = input.Length,
                pbData = inputHandle.AddrOfPinnedObject(),
            };

            var ok = protect
                ? CryptProtectData(ref inputBlob, "WinRedis credential", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output);

            if (!ok)
                throw new CryptographicException(
                    Loc.F("DPAPI {0}失败，错误码 {1}", (protect ? "加密" : "解密"), System.Runtime.InteropServices.Marshal.GetLastWin32Error()));

            var result = new byte[output.cbData];
            System.Runtime.InteropServices.Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            inputHandle.Free();
            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
