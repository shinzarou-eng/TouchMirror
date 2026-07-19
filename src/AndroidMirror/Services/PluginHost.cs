using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchMirror.Services;

/// <summary>
/// Script utilisateur lancé par l'app (dossier plugins/, .ps1 uniquement —
/// un seul format lisible et auditable).
/// Reçoit TOUCHMIRROR_API_URL / TOUCHMIRROR_API_TOKEN en variables
/// d'environnement quand l'API locale est active.
/// </summary>
public partial class PluginInstance : ObservableObject
{
    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public bool IsVerified { get; }
    [ObservableProperty] private bool _running;

    private Process? _proc;

    public event Action<string>? Output;

    public PluginInstance(string path)
    {
        FilePath = path;
        IsVerified = ComputeIsVerified();
    }

    private bool ComputeIsVerified()
    {
        try
        {
            using var fs = File.OpenRead(FilePath);
            var hash = Convert.ToHexString(SHA256.HashData(fs));
            return VerifiedPlugins.Hashes.Contains(hash);
        }
        catch { return false; }
    }

    public void Start(string? apiUrl, string? apiToken)
    {
        if (_proc != null)
            return;
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{FilePath}\"");
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.WorkingDirectory = Path.GetDirectoryName(FilePath)!;
        if (apiUrl != null) psi.Environment["TOUCHMIRROR_API_URL"] = apiUrl;
        if (apiToken != null) psi.Environment["TOUCHMIRROR_API_TOKEN"] = apiToken;
        try
        {
            var proc = Process.Start(psi);
            if (proc == null)
                return;
            _proc = proc;
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
            proc.Exited += (_, _) =>
            {
                _proc = null;
                Application.Current?.Dispatcher.Invoke(() => Running = false);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            Running = true;
        }
        catch (Exception ex)
        {
            _proc = null;
            Output?.Invoke($"lancement impossible : {ex.Message}");
        }
    }

    public void Stop()
    {
        var proc = _proc;
        _proc = null;
        Running = false;
        try { proc?.Kill(entireProcessTree: true); } catch { }
        try { proc?.Dispose(); } catch { }
    }
}
