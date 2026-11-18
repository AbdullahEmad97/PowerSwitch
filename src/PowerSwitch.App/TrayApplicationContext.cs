using Microsoft.Win32;
using PowerSwitch.Core.Layouts;
using PowerSwitch.Core.Translation;
using System.Runtime.InteropServices;

namespace PowerSwitch.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint RegisterHotkeyNoRepeat = 0x4000;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;
    private const ushort VkV = 0x56;
    private const int ClipboardTimeoutMs = 1500;
    private const int RestoreDelayMs = 350;

    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem startupMenuItem;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly KeyboardTranslationEngine translationEngine = new();
    private bool isConverting;
    private bool updatingStartupMenu;
    private bool disposed;

    public TrayApplicationContext()
    {
        trayIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "PowerKeySwitch - Ctrl+Q"
        };

        startupMenuItem = new ToolStripMenuItem("تشغيل عند تسجيل الدخول")
        {
            CheckOnClick = true
        };
        startupMenuItem.Checked = StartupManager.IsEnabled;
        startupMenuItem.CheckedChanged += StartupMenuItemCheckedChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add("طريقة الاستخدام: حدد النص ثم Ctrl+Q", null, (_, _) =>
            trayIcon.ShowBalloonTip(3000, "PowerKeySwitch", "حدد النص في أي تطبيق ثم اضغط Ctrl+Q.", ToolTipIcon.Info));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupMenuItem);
        menu.Items.Add("خروج", null, (_, _) => ExitThread());
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += async (_, _) => await ConvertSelectionAsync();
        trayIcon.Visible = true;

        hotkeyWindow = new HotkeyWindow(HandleHotkey);
        if (!NativeMethods.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, ModControl | RegisterHotkeyNoRepeat, (uint)Keys.Q))
        {
            ShowError("تعذر تسجيل الاختصار Ctrl+Q. قد يكون مستخدمًا من برنامج آخر.");
        }

        try
        {
            StartupManager.EnsureDefaultEnabled();
            startupMenuItem.Checked = StartupManager.IsEnabled;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ShowError($"تعذر تفعيل التشغيل عند تسجيل الدخول: {exception.Message}");
        }
    }

    private void HandleHotkey()
    {
        _ = ConvertSelectionAsync();
    }

    private async Task ConvertSelectionAsync()
    {
        if (isConverting)
        {
            return;
        }

        isConverting = true;
        object? previousClipboard = null;
        var clipboardSnapshotTaken = false;
        uint sequenceToRestore = 0;
        try
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero || foregroundWindow == hotkeyWindow.Handle)
            {
                ShowError("لم أتمكن من تحديد نافذة النص. أعد تحديد النص وحاول مرة أخرى.");
                return;
            }

            var layouts = GetAvailableLayouts();
            var sourceHandle = GetForegroundKeyboardLayout(foregroundWindow);
            var source = KeyboardLayoutInfo.FromHandle(sourceHandle);
            var sourceIndex = layouts.FindIndex(layout => layout.Kind == source.Kind);
            if (sourceIndex < 0 || layouts.Count < 2)
            {
                ShowError("يلزم تثبيت تخطيطي لوحة مفاتيح مدعومين على الأقل واختيار أحدهما في النافذة الحالية.");
                return;
            }

            var target = layouts[(sourceIndex + 1) % layouts.Count];
            previousClipboard = Clipboard.GetDataObject();
            clipboardSnapshotTaken = true;
            var sequenceBeforeCopy = NativeMethods.GetClipboardSequenceNumber();

            SendShortcut(VkC);
            if (!await WaitForClipboardChangeAsync(sequenceBeforeCopy))
            {
                ShowError("لم يصل النص المحدد إلى الحافظة. تأكد من تحديد نص في التطبيق.");
                return;
            }

            sequenceToRestore = NativeMethods.GetClipboardSequenceNumber();
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                ShowError("تغيرت النافذة النشطة أثناء التحويل؛ لم ألصق النص في نافذة أخرى.");
                return;
            }

            if (!Clipboard.ContainsText())
            {
                ShowError("التحديد لا يحتوي على نص قابل للقراءة.");
                return;
            }

            var selectedText = Clipboard.GetText();
            if (selectedText.Length == 0)
            {
                ShowError("التحديد فارغ.");
                return;
            }

            var result = translationEngine.Translate(selectedText, source, target);
            if (!result.WasChanged)
            {
                trayIcon.ShowBalloonTip(2500, "PowerKeySwitch", "لم يتغير النص؛ تحقق من تخطيط الكتابة المحدد.", ToolTipIcon.Info);
                return;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                ShowError("تغيرت النافذة النشطة أثناء التحويل؛ لم ألصق النص في نافذة أخرى.");
                return;
            }

            Clipboard.SetText(result.Text);
            sequenceToRestore = NativeMethods.GetClipboardSequenceNumber();
            SendShortcut(VkV);
            await Task.Delay(RestoreDelayMs);

            if (NativeMethods.GetClipboardSequenceNumber() == sequenceToRestore && previousClipboard is not null)
            {
                Clipboard.SetDataObject(previousClipboard, true);
                previousClipboard = null;
            }

            var unmappedMessage = result.UnmappedCharacters.Count == 0
                ? string.Empty
                : $" أحرف غير مدعومة بقيت كما هي: {result.UnmappedCharacters.Count}.";
            trayIcon.ShowBalloonTip(2500, "PowerKeySwitch", $"تم التحويل إلى {target.LanguageTag}.{unmappedMessage}", ToolTipIcon.Info);
        }
        catch (ExternalException exception)
        {
            ShowError($"تعذر الوصول إلى الحافظة أو إرسال النص: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            ShowError($"تعذر إكمال التحويل: {exception.Message}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError($"تعذر استعادة الحافظة أو تحديث إعداد التشغيل: {exception.Message}");
        }
        finally
        {
            if (clipboardSnapshotTaken)
            {
                try
                {
                    if (sequenceToRestore != 0 &&
                        NativeMethods.GetClipboardSequenceNumber() == sequenceToRestore)
                    {
                        if (previousClipboard is null)
                        {
                            Clipboard.Clear();
                        }
                        else
                        {
                            Clipboard.SetDataObject(previousClipboard, true);
                        }
                    }
                }
                catch (ExternalException exception)
                {
                    ShowError($"لم أتمكن من استعادة الحافظة: {exception.Message}");
                }
            }

            isConverting = false;
        }
    }

    private static List<KeyboardLayoutInfo> GetAvailableLayouts()
    {
        var supported = new[]
        {
            KeyboardLayoutKind.Arabic101,
            KeyboardLayoutKind.Arabic102,
            KeyboardLayoutKind.EnglishQwerty,
            KeyboardLayoutKind.EnglishQwertyUk,
            KeyboardLayoutKind.FrenchAzerty,
            KeyboardLayoutKind.SpanishQwerty
        };
        return KeyboardLayoutCatalog.GetInstalledLayouts()
            .Where(layout => supported.Contains(layout.Kind))
            .GroupBy(layout => layout.Kind)
            .Select(group => group.First())
            .ToList();
    }

    private static IntPtr GetForegroundKeyboardLayout(IntPtr foregroundWindow)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        return threadId == 0 ? IntPtr.Zero : NativeMethods.GetKeyboardLayout(threadId);
    }

    private static async Task<bool> WaitForClipboardChangeAsync(uint previousSequence)
    {
        var deadline = Environment.TickCount64 + ClipboardTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (NativeMethods.GetClipboardSequenceNumber() != previousSequence)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static void SendShortcut(ushort key)
    {
        var inputs = new[]
        {
            NativeMethods.CreateKeyInput(VkControl, 0),
            NativeMethods.CreateKeyInput(key, 0),
            NativeMethods.CreateKeyInput(key, KeyeventfKeyup),
            NativeMethods.CreateKeyInput(VkControl, KeyeventfKeyup)
        };
        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) != (uint)inputs.Length)
        {
            throw new ExternalException("تعذر إرسال اختصار لوحة المفاتيح.", Marshal.GetLastWin32Error());
        }
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            updatingStartupMenu = true;
            startupMenuItem.Checked = StartupManager.IsEnabled;
            updatingStartupMenu = false;
            ShowError($"تعذر تحديث إعداد التشغيل: {exception.Message}");
        }
    }

    private void StartupMenuItemCheckedChanged(object? sender, EventArgs e)
    {
        if (!updatingStartupMenu)
        {
            SetStartup(startupMenuItem.Checked);
        }
    }

    private void ShowError(string message)
    {
        trayIcon.ShowBalloonTip(5000, "PowerKeySwitch", message, ToolTipIcon.Error);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(TrayApplicationContext).Assembly
            .GetManifestResourceStream("PowerSwitch.App.PowerKeySwitch.ico")
            ?? throw new InvalidOperationException("تعذر تحميل أيقونة PowerKeySwitch.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    protected override void ExitThreadCore()
    {
        if (!disposed)
        {
            disposed = true;
            NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
            hotkeyWindow.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        base.ExitThreadCore();
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            this.onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                onHotkey();
            }
            else
            {
                base.WndProc(ref message);
            }
        }

        public void Dispose() => DestroyHandle();
    }

    private static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string PreferencesKey = @"Software\PowerKeySwitch";
        private const string ValueName = "PowerKeySwitch";
        private const string PreferenceValueName = "PowerKeySwitchStartupConfigured";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string value &&
                    string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new IOException("تعذر فتح مفتاح بدء تشغيل المستخدم.");
            if (enabled)
            {
                key.SetValue(ValueName, GetCommand());
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true)
                ?? throw new IOException("تعذر حفظ إعداد بدء التشغيل.");
            preferences.SetValue(PreferenceValueName, 1, RegistryValueKind.DWord);
        }

        public static void EnsureDefaultEnabled()
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new IOException("تعذر فتح مفتاح بدء تشغيل المستخدم.");
            using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true)
                ?? throw new IOException("تعذر فتح إعداد بدء التشغيل.");
            var preference = preferences.GetValue(PreferenceValueName);
            if (preference is null)
            {
                runKey.SetValue(ValueName, GetCommand());
                preferences.SetValue(PreferenceValueName, 1, RegistryValueKind.DWord);
            }
            else if (Convert.ToInt32(preference) == 1)
            {
                runKey.SetValue(ValueName, GetCommand());
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        private static string GetCommand()
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("تعذر تحديد مسار التطبيق الحالي.");
            return $"\"{executable}\"";
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)]
            internal KeyboardInput Keyboard;

            [FieldOffset(0)]
            internal MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            internal int X;
            internal int Y;
            internal uint Data;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, Input[] inputs, int inputSize);

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

        internal static Input CreateKeyInput(ushort virtualKey, uint flags) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }
}
