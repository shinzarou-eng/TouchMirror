using System;
using System.Diagnostics;
using System.IO;
using TouchMirror.Services;

namespace TouchMirror.AirPlay;

internal interface IFairPlayDecrypt
{
    byte[]? Decrypt(byte[] keyMsg164, byte[] eKey72);
}

internal sealed class NullFairPlayDecrypt : IFairPlayDecrypt
{
    public byte[]? Decrypt(byte[] keyMsg164, byte[] eKey72)
    {
        AppLogger.Write("AirPlay: ekey recu mais aucun decrypteur FairPlay configure — flux impossible a dechiffrer");
        return null;
    }
}

internal sealed class HelperFairPlayDecrypt : IFairPlayDecrypt
{
    private readonly string _helperPath;

    public HelperFairPlayDecrypt(string helperPath) => _helperPath = helperPath;

    public static IFairPlayDecrypt Create()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "FairPlayHelper.exe");
        return File.Exists(path) ? new HelperFairPlayDecrypt(path) : (IFairPlayDecrypt)new NullFairPlayDecrypt();
    }

    public byte[]? Decrypt(byte[] keyMsg164, byte[] eKey72)
    {
        try
        {
            var input = new byte[keyMsg164.Length + eKey72.Length];
            Array.Copy(keyMsg164, input, keyMsg164.Length);
            Array.Copy(eKey72, 0, input, keyMsg164.Length, eKey72.Length);

            var psi = new ProcessStartInfo(_helperPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return null;
            proc.StandardInput.BaseStream.Write(input, 0, input.Length);
            proc.StandardInput.Close();
            var key = new byte[16];
            var read = proc.StandardOutput.BaseStream.Read(key, 0, 16);
            proc.WaitForExit(5000);
            return read == 16 && proc.ExitCode == 0 ? key : null;
        }
        catch (Exception ex)
        {
            AppLogger.Write($"AirPlay: FairPlayHelper a echoue: {ex.Message}");
            return null;
        }
    }
}
