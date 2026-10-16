using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pinkyu;

static class Program
{
    private const string AppName = "Pinkyu";
    private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Handle command-line arguments for CLI startup management
        if (args.Length > 0)
        {
            string cmd = args[0].ToLowerInvariant();
            if (cmd is "--install-startup" or "-install" or "/install")
            {
                SetStartup(true);
                MessageBox.Show("Pinkyu has been successfully added to Windows Startup!", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (cmd is "--uninstall-startup" or "-uninstall" or "/uninstall")
            {
                SetStartup(false);
                MessageBox.Show("Pinkyu has been removed from Windows Startup.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        // Single instance check
        _singleInstanceMutex = new Mutex(true, "Pinkyu_SingleInstance_App_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Pinkyu is already running in the System Tray (near Date & Time)!", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string initialTarget = args.Length > 0 && !args[0].StartsWith("-") ? args[0] : "8.8.8.8";

        Application.Run(new PinkyuApplicationContext(initialTarget));
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to configure startup: {ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

public class PinkyuApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly System.Windows.Forms.Timer _pingTimer;

    private string _targetHost = "8.8.8.8";
    private int _pingIntervalMs = 3000;
    private int _pingTimeoutMs = 2000;

    private bool _lastPingSuccess = false;
    private long _lastRoundtripMs = 0;
    private bool _hasReceivedFirstPing = false;
    private bool _isPinging = false;
    private bool _blinkPhase = false;

    // Cached pre-rendered icons to avoid GDI handle leaks
    private readonly Icon _iconGreenBright;
    private readonly Icon _iconGreenDim;
    private readonly Icon _iconRedBright;
    private readonly Icon _iconRedDim;
    private readonly Icon _iconGray;

    // Context menu items
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _targetMenuItem;
    private readonly ToolStripMenuItem _startupMenuItem;

    public PinkyuApplicationContext(string targetHost)
    {
        _targetHost = targetHost;

        // Generate high-resolution icons (32x32)
        _iconGreenBright = IconGenerator.CreateCircleIcon(Color.FromArgb(34, 197, 94), Color.FromArgb(187, 247, 208), Color.FromArgb(20, 83, 45));
        _iconGreenDim = IconGenerator.CreateCircleIcon(Color.FromArgb(21, 128, 61), Color.FromArgb(34, 197, 94), Color.FromArgb(10, 40, 20), isDim: true);
        _iconRedBright = IconGenerator.CreateCircleIcon(Color.FromArgb(239, 68, 68), Color.FromArgb(254, 202, 202), Color.FromArgb(127, 29, 29));
        _iconRedDim = IconGenerator.CreateCircleIcon(Color.FromArgb(153, 27, 27), Color.FromArgb(220, 38, 38), Color.FromArgb(69, 10, 10), isDim: true);
        _iconGray = IconGenerator.CreateCircleIcon(Color.FromArgb(156, 163, 175), Color.FromArgb(229, 231, 235), Color.FromArgb(75, 85, 99));

        // Create Context Menu
        var contextMenu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem("Pinkyu - Ping Monitor")
        {
            Enabled = false,
            Font = new Font(contextMenu.Font, FontStyle.Bold)
        };
        contextMenu.Items.Add(titleItem);

        _statusMenuItem = new ToolStripMenuItem("Status: Initializing...") { Enabled = false };
        contextMenu.Items.Add(_statusMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _targetMenuItem = new ToolStripMenuItem($"Target: {_targetHost}");
        var targetGoogle = new ToolStripMenuItem("8.8.8.8 (Google DNS)", null, (s, e) => ChangeTarget("8.8.8.8"));
        var targetCloudflare = new ToolStripMenuItem("1.1.1.1 (Cloudflare DNS)", null, (s, e) => ChangeTarget("1.1.1.1"));
        var targetCustom = new ToolStripMenuItem("Custom IP / Host...", null, (s, e) => PromptCustomTarget());
        _targetMenuItem.DropDownItems.Add(targetGoogle);
        _targetMenuItem.DropDownItems.Add(targetCloudflare);
        _targetMenuItem.DropDownItems.Add(new ToolStripSeparator());
        _targetMenuItem.DropDownItems.Add(targetCustom);
        contextMenu.Items.Add(_targetMenuItem);

        var pingNowItem = new ToolStripMenuItem("Ping Now", null, async (s, e) => await TriggerPingAsync());
        contextMenu.Items.Add(pingNowItem);

        _startupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = Program.IsStartupEnabled()
        };
        _startupMenuItem.Click += (s, e) =>
        {
            bool newState = !_startupMenuItem.Checked;
            Program.SetStartup(newState);
            _startupMenuItem.Checked = Program.IsStartupEnabled();
        };
        contextMenu.Items.Add(_startupMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApp());
        contextMenu.Items.Add(exitItem);

        // NotifyIcon Setup
        _notifyIcon = new NotifyIcon
        {
            Icon = _iconGray,
            Text = $"Pinkyu - Connecting to {_targetHost}...",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        // Animation Timer (controls the blinking effect)
        _animTimer = new System.Windows.Forms.Timer { Interval = 450 };
        _animTimer.Tick += OnAnimationTick;
        _animTimer.Start();

        // Ping Timer (every 3 seconds)
        _pingTimer = new System.Windows.Forms.Timer { Interval = _pingIntervalMs };
        _pingTimer.Tick += async (s, e) => await TriggerPingAsync();
        _pingTimer.Start();

        // Fire initial ping immediately
        _ = TriggerPingAsync();
    }

    private void ChangeTarget(string newTarget)
    {
        if (string.IsNullOrWhiteSpace(newTarget)) return;
        _targetHost = newTarget.Trim();
        _targetMenuItem.Text = $"Target: {_targetHost}";
        _hasReceivedFirstPing = false;
        _notifyIcon.Text = TruncateTooltip($"Pinkyu: Switching to {_targetHost}...");
        _ = TriggerPingAsync();
    }

    private void PromptCustomTarget()
    {
        using var form = new Form
        {
            Width = 320,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Change Ping Target",
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label { Left = 20, Top = 15, Text = "Enter target IP or hostname:", Width = 260 };
        var textBox = new TextBox { Left = 20, Top = 40, Width = 260, Text = _targetHost };
        var okButton = new Button { Text = "OK", Left = 120, Width = 75, Top = 80, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 205, Width = 75, Top = 80, DialogResult = DialogResult.Cancel };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            ChangeTarget(textBox.Text.Trim());
        }
    }

    private async Task TriggerPingAsync()
    {
        if (_isPinging) return;
        _isPinging = true;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_targetHost, _pingTimeoutMs);

            _hasReceivedFirstPing = true;
            if (reply.Status == IPStatus.Success)
            {
                _lastPingSuccess = true;
                _lastRoundtripMs = reply.RoundtripTime;
                _statusMenuItem.Text = $"Status: Connected ({_lastRoundtripMs} ms)";
                _notifyIcon.Text = TruncateTooltip($"Pinkyu: Connected ({_lastRoundtripMs} ms) - {_targetHost}");
            }
            else
            {
                _lastPingSuccess = false;
                _statusMenuItem.Text = $"Status: Failed ({reply.Status})";
                _notifyIcon.Text = TruncateTooltip($"Pinkyu: Timeout/Failed ({reply.Status}) - {_targetHost}");
            }
        }
        catch (Exception)
        {
            _hasReceivedFirstPing = true;
            _lastPingSuccess = false;
            _statusMenuItem.Text = $"Status: Error";
            _notifyIcon.Text = TruncateTooltip($"Pinkyu: Timeout/Error - {_targetHost}");
        }
        finally
        {
            _isPinging = false;
            UpdateIconVisual();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _blinkPhase = !_blinkPhase;
        UpdateIconVisual();
    }

    private void UpdateIconVisual()
    {
        if (!_hasReceivedFirstPing)
        {
            _notifyIcon.Icon = _iconGray;
            return;
        }

        if (_lastPingSuccess)
        {
            // Kedap-kedip hijau (alternating bright green and soft dim green)
            _notifyIcon.Icon = _blinkPhase ? _iconGreenBright : _iconGreenDim;
        }
        else
        {
            // Berkedip-kedip merah (alternating bright alert red and dark/dim red)
            _notifyIcon.Icon = _blinkPhase ? _iconRedBright : _iconRedDim;
        }
    }

    private static string TruncateTooltip(string text)
    {
        // NotifyIcon.Text max length is 63 chars in older shells, 127 in newer
        if (text.Length > 63)
        {
            return text.Substring(0, 60) + "...";
        }
        return text;
    }

    private void ExitApp()
    {
        _animTimer.Stop();
        _pingTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _iconGreenBright.Dispose();
        _iconGreenDim.Dispose();
        _iconRedBright.Dispose();
        _iconRedDim.Dispose();
        _iconGray.Dispose();

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer?.Dispose();
            _pingTimer?.Dispose();
            _notifyIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class IconGenerator
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateCircleIcon(Color mainColor, Color highlightColor, Color borderColor, bool isDim = false)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Outer circle / border
            int margin = 3;
            int diameter = size - (margin * 2);
            var rect = new Rectangle(margin, margin, diameter, diameter);

            // Draw shadow/border
            using (var borderPen = new Pen(borderColor, 2f))
            {
                g.DrawEllipse(borderPen, rect);
            }

            // Fill body
            if (isDim)
            {
                using var brush = new SolidBrush(Color.FromArgb(160, mainColor));
                g.FillEllipse(brush, rect);
            }
            else
            {
                using var path = new GraphicsPath();
                path.AddEllipse(rect);
                using var lgb = new PathGradientBrush(path)
                {
                    CenterColor = highlightColor,
                    SurroundColors = new[] { mainColor },
                    CenterPoint = new PointF(size * 0.38f, size * 0.38f)
                };
                g.FillPath(lgb, path);

                // Add a small specular highlight for a polished 3D LED look
                using var highlightBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                g.FillEllipse(highlightBrush, new Rectangle(margin + 4, margin + 4, 6, 4));
            }
        }

        IntPtr hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }
}