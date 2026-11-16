using System;
using System.IO;
using System.Text;

namespace LiveBackground
{
    // Windows exposes the lock-screen image only through WinRT. Referencing WinRT
    // from csc needs a merged Windows.winmd that a plain .NET 4.8 machine does not
    // have, so this uses the same WinRT calls from PowerShell instead. It runs only
    // at install/start/uninstall, so the ~1 s cost does not matter.
    public static class WinRtLockScreen
    {
        private const string Header = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asTaskAction = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function AwaitOp($op, $type) {
  $m = $asTaskGeneric.MakeGenericMethod($type)
  $t = $m.Invoke($null, @($op))
  $t.Wait(-1) | Out-Null
  return $t.Result
}
function AwaitAction($op) {
  $t = $asTaskAction.Invoke($null, @($op))
  $t.Wait(-1) | Out-Null
}
";

        public static bool TryBackup(string destinationBase, out string actualPath, out string error)
        {
            actualPath = "";
            error = "";
            string temp = destinationBase + ".bin";
            Util.SafeDelete(temp);
            string dir = Path.GetDirectoryName(temp);
            if (!string.IsNullOrEmpty(dir)) { try { Directory.CreateDirectory(dir); } catch { /* best-effort: the folder is created if missing */ } }

            string script = Header + @"
$stream = [Windows.System.UserProfile.LockScreen]::GetImageStream()
if ($null -eq $stream) { Write-Output 'NONE'; exit 3 }
$net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
$fs = [System.IO.File]::Create(" + PsString(temp) + @")
try { $net.CopyTo($fs) } finally { $fs.Dispose(); $net.Dispose(); $stream.Dispose() }
Write-Output ('OK ' + (Get-Item " + PsString(temp) + @").Length)
";

            string output;
            if (!Run(script, out output, out error)) return false;
            if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                error = "lock-screen backup was empty";
                return false;
            }

            string extension = Util.DetectImageExtension(temp);
            string target = destinationBase + extension;
            try
            {
                Util.SafeDelete(target);
                File.Move(temp, target);
            }
            catch (Exception ex)
            {
                error = "could not save lock-screen backup: " + ex.Message;
                return false;
            }
            actualPath = target;
            return true;
        }

        public static bool TrySet(string imagePath, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                error = "lock-screen image not found: " + imagePath;
                return false;
            }
            string script = Header + @"
$file = AwaitOp ([Windows.Storage.StorageFile]::GetFileFromPathAsync(" + PsString(imagePath) + @")) ([Windows.Storage.StorageFile])
if ($null -eq $file) { Write-Output 'NONE'; exit 4 }
AwaitAction ([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($file))
Write-Output 'OK'
";
            string output;
            return Run(script, out output, out error);
        }

        private static string PsString(string value)
        {
            return "'" + (value == null ? "" : value.Replace("'", "''")) + "'";
        }

        private static bool Run(string script, out string output, out string error)
        {
            output = "";
            error = "";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";

            string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            ProcessResult result = ProcessRunner.Run(powershell, args, null, 60000);
            output = (result.StandardOutput ?? "").Trim();
            if (result.ExitCode == 0 && output.StartsWith("OK", StringComparison.OrdinalIgnoreCase)) return true;

            string stderr = (result.StandardError ?? "").Trim();
            if (stderr.Length > 500) stderr = stderr.Substring(0, 500);
            error = "lock-screen helper failed (exit " + result.ExitCode + "): " + stderr;
            if (output.Length > 0) error += " [" + output + "]";
            Log.Warn(error);
            return false;
        }
    }
}
