using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerSwitch.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox location = new();
    private readonly CheckBox desktopShortcut = new() { Text = "إنشاء اختصار على سطح المكتب", Checked = true };
    private readonly CheckBox startMenuShortcut = new() { Text = "إنشاء اختصار في قائمة البداية", Checked = true };
    private readonly CheckBox startup = new() { Text = "تشغيل PowerSwitch عند تسجيل الدخول", Checked = true };
    private readonly CheckBox launch = new() { Text = "تشغيل التطبيق بعد التنصيب", Checked = true };
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label status = new() { AutoSize = true };

    public InstallerForm()
    {
        Text = "مثبت PowerSwitch";
        Size = new Size(620, 390);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "تنصيب PowerSwitch",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 15)
        };
        var locationLabel = new Label { Text = "مكان التنصيب:", AutoSize = true, Location = new Point(20, 65) };
        location.Location = new Point(20, 90);
        location.Width = 470;
        location.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PowerSwitch");

        var browse = new Button { Text = "تحديد المجلد...", Location = new Point(500, 88), Width = 90 };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "اختر مجلد تنصيب PowerSwitch",
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
            Text = "تنصيب",
            DialogResult = DialogResult.OK,
            Location = new Point(390, 315),
            Width = 90
        };
        install.Click += async (_, _) => await InstallAsync(install);

        var cancel = new Button
        {
            Text = "إلغاء",
            DialogResult = DialogResult.Cancel,
            Location = new Point(500, 315),
            Width = 90
        };

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
        status.Text = "جارٍ إزالة النسخ السابقة وتجهيز التنصيب...";
        try
        {
            await Task.Run(() => Install(location.Text.Trim()));
            progress.Visible = false;
            installButton.Enabled = true;
            MessageBox.Show(this, "تم تنصيب PowerSwitch بنجاح.", "اكتمل التنصيب",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception exception)
        {
            progress.Visible = false;
            installButton.Enabled = true;
            status.Text = "تعذر التنصيب.";
            MessageBox.Show(this, exception.Message, "فشل التنصيب",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Install(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("يرجى تحديد مكان التنصيب.");

        StopExistingInstances();
        RemoveRegistryEntry();
        RemoveKnownOldDirectories(destination);
        Directory.CreateDirectory(destination);

        var solutionRoot = FindRepositoryRoot();
        var appProject = Path.Combine(solutionRoot, "src", "PowerSwitch.App", "PowerSwitch.App.csproj");
        RunDotnetPublish(appProject, destination);

        var executable = Path.Combine(destination, "PowerSwitch.App.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("تعذر العثور على التطبيق المنشور.");

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

        if (Directory.Exists(destination))
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(destination))
            {
                if (Directory.Exists(child))
                    Directory.Delete(child, true);
                else
                    File.Delete(child);
            }
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

    private static void RunDotnetPublish(string project, string destination)
    {
        var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "publish", project,
                "-c", "Release",
                "-r", "win-x64",
                "--self-contained", "true",
                "/p:PublishSingleFile=true",
                "-o", destination
            },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("تعذر تشغيل dotnet.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"فشل نشر التطبيق:\n{error}\n{output}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerKeySwitch.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("تعذر العثور على مجلد المشروع.");
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
}
