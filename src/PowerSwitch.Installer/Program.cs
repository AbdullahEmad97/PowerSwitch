using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerSwitch.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        if (Environment.GetCommandLineArgs().Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Uninstall();
            return;
        }

        Application.Run(new InstallerForm());
    }

    private static void Uninstall()
    {
        string? installLocation;
        using (var appKey = Registry.CurrentUser.OpenSubKey(@"Software\PowerKeySwitch"))
            installLocation = appKey?.GetValue("InstallLocation") as string;

        using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            runKey?.DeleteValue("PowerKeySwitch", false);
        using (var uninstallKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            uninstallKey?.DeleteSubKeyTree("PowerSwitch", false);
        using (var softwareKey = Registry.CurrentUser.OpenSubKey(@"Software", true))
            softwareKey?.DeleteSubKeyTree("PowerKeySwitch", false);

        foreach (var process in Process.GetProcessesByName("PowerSwitch.App"))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }

        RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PowerSwitch.lnk"));
        RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "PowerSwitch.lnk"));

        if (!string.IsNullOrWhiteSpace(installLocation) &&
            File.Exists(Path.Combine(installLocation, "PowerSwitch.App.exe")))
            ScheduleDirectoryRemoval(installLocation);

        MessageBox.Show("PowerSwitch was uninstalled.", "PowerSwitch",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void ScheduleDirectoryRemoval(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (fullPath.Length < 4 || string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The uninstall folder is invalid.");

        var escapedPath = fullPath.Replace("'", "''");
        var script = $"Start-Sleep -Seconds 2; Remove-Item -LiteralPath '{escapedPath}' -Recurse -Force";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void RemoveShortcut(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox location = new();
    private readonly CheckBox desktopShortcut = new() { Text = "Create Desktop shortcut", Checked = true };
    private readonly CheckBox startMenuShortcut = new() { Text = "Create Start Menu shortcut", Checked = true };
    private readonly CheckBox startup = new() { Text = "Run PowerSwitch at Windows login", Checked = true };
    private readonly CheckBox launch = new() { Text = "Launch PowerSwitch after installation", Checked = true };
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label status = new() { AutoSize = true };

    public InstallerForm()
    {
        Text = "PowerSwitch Installer";
        Size = new Size(620, 390);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "Install PowerSwitch",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 15)
        };
        var locationLabel = new Label { Text = "Installation folder:", AutoSize = true, Location = new Point(20, 65) };
        location.Location = new Point(20, 90);
        location.Width = 470;
        location.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PowerSwitch");

        var browse = new Button { Text = "Browse...", Location = new Point(500, 88), Width = 90 };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the PowerSwitch installation folder",
                SelectedPath = location.Text
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                location.Text = dialog.SelectedPath;
        };

        desktopShortcut.Location = new Point(20, 135);
        desktopShortcut.AutoSize = true;
        startMenuShortcut.Location = new Point(20, 165);
        startMenuShortcut.AutoSize = true;
        startup.Location = new Point(20, 195);
        startup.AutoSize = true;
        launch.Location = new Point(20, 225);
        launch.AutoSize = true;

        progress.Location = new Point(20, 260);
        progress.Width = 570;
        status.Location = new Point(20, 292);

        var install = new Button
        {
            Text = "Install",
            DialogResult = DialogResult.OK,
            Location = new Point(390, 315),
            Width = 90
        };
        install.Click += async (_, _) => await InstallAsync(install);

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(500, 315),
            Width = 90
        };
        cancel.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            title, locationLabel, location, browse, desktopShortcut, startMenuShortcut,
            startup, launch, progress, status, install, cancel
        });
    }

    private async Task InstallAsync(Button installButton)
    {
        installButton.Enabled = false;
        progress.Visible = true;
        status.Text = "Removing previous versions and preparing installation...";
        try
        {
            await Task.Run(() => Install(location.Text.Trim()));
            progress.Visible = false;
            installButton.Enabled = true;
            MessageBox.Show(this, "PowerSwitch was installed successfully.", "Installation complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception exception)
        {
            progress.Visible = false;
            installButton.Enabled = true;
            status.Text = "Installation failed.";
            MessageBox.Show(this, exception.Message, "Installation failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Install(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("Please select an installation folder.");

        destination = Path.GetFullPath(destination);
        var root = Path.GetPathRoot(destination);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("The selected partition does not exist or is unavailable.");
        if (string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a folder below the drive root.");

        var payload = Path.Combine(AppContext.BaseDirectory, "Payload");
        if (!File.Exists(Path.Combine(payload, "PowerSwitch.App.exe")))
            throw new InvalidOperationException("The application payload is missing. Extract the complete release package before running setup.");

        StopExistingInstances();
        RemoveRegistryEntry();
        RemoveKnownOldDirectories(destination);
        Directory.CreateDirectory(destination);

        CopyApplicationPayload(payload, destination);

        var executable = Path.Combine(destination, "PowerSwitch.App.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("The application package is incomplete.");
        var uninstaller = Path.Combine(destination, "PowerSwitch.Uninstaller.exe");
        var currentInstaller = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the installer path.");
        File.Copy(currentInstaller, uninstaller, true);

        RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PowerSwitch.lnk"));
        var startDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");
        RemoveShortcut(Path.Combine(startDirectory, "PowerSwitch.lnk"));

        if (desktopShortcut.Checked)
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PowerSwitch.lnk"), executable, destination);
        if (startMenuShortcut.Checked)
            CreateShortcut(Path.Combine(startDirectory, "PowerSwitch.lnk"), executable, destination);

        if (startup.Checked)
            SetStartupEntry(executable);
        else
            RemoveRegistryEntry();

        SaveInstallLocation(destination);
        RegisterWindowsUninstallEntry(destination, executable);

        if (launch.Checked)
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private void StopExistingInstances()
    {
        foreach (var process in Process.GetProcessesByName("PowerSwitch.App"))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                    process.Kill();
            }
            catch
            {
            }
        }
    }

    private void RemoveKnownOldDirectories(string destination)
    {
        var oldDirectories = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PowerKeySwitch"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PowerSwitch")
        };

        foreach (var directory in oldDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(path => path.Length > 3 &&
                                    !string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)))
        {
            RemoveDirectory(directory);
        }

    }

    private static void RemoveDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }
    }

    private static void CopyApplicationPayload(string payload, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(payload, sourceFile);
            var targetFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
        }
    }

    private static void CreateShortcut(string linkPath, string executable, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("تعذر إنشاء اختصارات Windows.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("تعذر تشغيل خدمة اختصارات Windows.");
        var shortcut = shell.CreateShortcut(linkPath);
        shortcut.TargetPath = executable;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "PowerSwitch keyboard layout translator";
        shortcut.Save();
    }

    private static void RemoveShortcut(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void SetStartupEntry(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key?.SetValue("PowerKeySwitch", $"\"{executable}\"");
    }

    private static void RemoveRegistryEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key?.DeleteValue("PowerKeySwitch", false);
    }

    private static void SaveInstallLocation(string destination)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\PowerKeySwitch", true);
        key?.SetValue("InstallLocation", destination);
    }

    private static void RegisterWindowsUninstallEntry(string destination, string executable)
    {
        var installerPath = Path.Combine(destination, "PowerSwitch.Uninstaller.exe");
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerSwitch", true)
            ?? throw new InvalidOperationException("Could not register PowerSwitch for uninstall.");
        key.SetValue("DisplayName", "PowerSwitch");
        key.SetValue("DisplayIcon", executable);
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("Publisher", "PowerSwitch");
        key.SetValue("InstallLocation", destination);
        key.SetValue("UninstallString", $"\"{installerPath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
