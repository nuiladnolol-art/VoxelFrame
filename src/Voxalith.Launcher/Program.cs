using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Voxalith.Launcher;

internal static class Program {
    [STAThread]
    private static void Main() {
        IconHelper.EnsureAppIcons();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LauncherForm());
    }
}

public class GameReleaseItem {
    public string DisplayName { get; set; } = "";
    public string Tag { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ChecksumsUrl { get; set; } = "";
    public bool IsLocalDev { get; set; }
    public bool IsInstalled { get; set; }
    public string InstallPath { get; set; } = "";
    public string ExePath { get; set; } = "";

    public override string ToString() => DisplayName;
}

public class LauncherConfig {
    public string Username { get; set; } = "Player";
    public int ScreenResolutionIndex { get; set; } = 0; // 0=1280x720, 1=1600x900, 2=1920x1080, 3=2560x1440
    public bool Fullscreen { get; set; } = false;
    public int RamMb { get; set; } = 4096; // 2048, 4096, 8192, 16384
    public int ThemeIndex { get; set; } = 0; // 0=Voxalith Dark, 1=Monolith Stone, 2=Astral Void
    public string GitHubRepo { get; set; } = "nuiladnolol-art/Voxalith";
}

public class LauncherForm : Form {
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;

    public static readonly string CurrentLauncherVersion = ReadOwnVersion();

    private static string ReadOwnVersion() {
        try {
            var asm = typeof(LauncherForm).Assembly;
            var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            if (info.Length > 0 && info[0] is System.Reflection.AssemblyInformationalVersionAttribute iv && !string.IsNullOrWhiteSpace(iv.InformationalVersion)) {
                return iv.InformationalVersion.Split('+')[0].Trim();
            }
            var v = asm.GetName().Version;
            if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
        } catch { }
        return "1.1.0";
    }

    private static string? ReadExeVersion(string exePath) {
        try {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            string? raw = vi.ProductVersion ?? vi.FileVersion;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Split('+')[0].Trim();
            return raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? raw : "v" + raw;
        } catch {
            return null;
        }
    }

    private LauncherConfig _config = new();
    private TextBox txtNickname;
    private ComboBox cbVersion;
    private Button btnPlay;
    private Button btnRefresh;
    private Button btnSettings;
    private Button btnUpdateNotice;
    private Label lblStatus;
    private ProgressBar pbDownload;
    private Panel headerPanel;
    private PictureBox picAvatar;

    private string _latestTag = "";
    private string _latestName = "";
    private string _latestSetupUrl = "";
    private string _latestZipUrl = "";

    private readonly List<GameReleaseItem> _versions = new();
    private static readonly HttpClient _http = new();
    private CancellationTokenSource? _downloadCts;

    // Theme colors
    private Color _bgMain = Color.FromArgb(20, 22, 30);
    private Color _bgHeader = Color.FromArgb(14, 16, 22);
    private Color _bgCard = Color.FromArgb(26, 30, 42);
    private Color _accentPrimary = Color.FromArgb(0, 210, 255);
    private Color _accentGold = Color.FromArgb(255, 215, 80);
    private Color _borderColor = Color.FromArgb(48, 56, 76);

    public LauncherForm() {
        LoadConfig();
        ApplyThemeColors(_config.ThemeIndex);

        this.Text = "Voxalith Launcher";
        this.Size = new Size(480, 380);
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = _bgMain;
        this.ForeColor = Color.White;
        try { this.Icon = IconHelper.GetLauncherIcon(); } catch { }

        // Top Header (Titlebar + Dragging)
        headerPanel = new Panel {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = _bgHeader
        };
        headerPanel.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Left) {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        };
        this.Controls.Add(headerPanel);

        // Header Title & Logo
        Label lblLogo = new Label {
            Text = "VOXALITH",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = _accentPrimary,
            BackColor = Color.Transparent,
            Location = new Point(14, 11),
            AutoSize = true
        };
        lblLogo.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Left) {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        };
        headerPanel.Controls.Add(lblLogo);

        Label lblBadge = new Label {
            Text = "v" + CurrentLauncherVersion,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 240, 180),
            BackColor = Color.FromArgb(25, 55, 35),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(115, 12),
            Size = new Size(62, 20)
        };
        headerPanel.Controls.Add(lblBadge);

        // Header Right Buttons (Settings, Minimize, Close)
        Button btnClose = new Button {
            Text = "✕",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(180, 190, 205),
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(34, 44),
            Location = new Point(446, 0),
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.MouseEnter += (s, e) => { btnClose.BackColor = Color.FromArgb(220, 40, 40); btnClose.ForeColor = Color.White; };
        btnClose.MouseLeave += (s, e) => { btnClose.BackColor = Color.Transparent; btnClose.ForeColor = Color.FromArgb(180, 190, 205); };
        btnClose.Click += (s, e) => Application.Exit();
        headerPanel.Controls.Add(btnClose);

        Button btnMin = new Button {
            Text = "—",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(180, 190, 205),
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(34, 44),
            Location = new Point(412, 0),
            Cursor = Cursors.Hand
        };
        btnMin.FlatAppearance.BorderSize = 0;
        btnMin.MouseEnter += (s, e) => { btnMin.BackColor = Color.FromArgb(35, 42, 58); };
        btnMin.MouseLeave += (s, e) => { btnMin.BackColor = Color.Transparent; };
        btnMin.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
        headerPanel.Controls.Add(btnMin);

        btnSettings = new Button {
            Text = "⚙️",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(200, 215, 235),
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(36, 44),
            Location = new Point(374, 0),
            Cursor = Cursors.Hand
        };
        btnSettings.FlatAppearance.BorderSize = 0;
        btnSettings.MouseEnter += (s, e) => { btnSettings.BackColor = Color.FromArgb(35, 42, 58); };
        btnSettings.MouseLeave += (s, e) => { btnSettings.BackColor = Color.Transparent; };
        btnSettings.Click += (s, e) => OpenSettingsDialog();
        headerPanel.Controls.Add(btnSettings);

        btnUpdateNotice = new Button {
            Text = "🔔 Обновить",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(210, 110, 25),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(265, 8),
            Size = new Size(100, 28),
            Visible = false
        };
        btnUpdateNotice.FlatAppearance.BorderSize = 0;
        btnUpdateNotice.Click += async (s, e) => {
            var dr = MessageBox.Show(
                $"Доступно обновление лаунчера: {_latestName} ({_latestTag})\n\nЗапустить автоматическое обновление прямо сейчас?",
                "Обновление лаунчера",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (dr == DialogResult.Yes) {
                await AutoUpdateLauncherAsync(_latestTag, _latestSetupUrl, _latestZipUrl);
            }
        };
        headerPanel.Controls.Add(btnUpdateNotice);

        // Content Area
        Panel content = new Panel {
            Location = new Point(16, 56),
            Size = new Size(448, 308),
            BackColor = Color.Transparent
        };
        this.Controls.Add(content);

        // 1. Profile Section (Card)
        Panel profileCard = new Panel {
            Location = new Point(0, 0),
            Size = new Size(448, 62),
            BackColor = _bgCard
        };
        profileCard.Paint += (s, e) => {
            using var pen = new Pen(_borderColor, 1.5f);
            e.Graphics.DrawRectangle(pen, 0, 0, profileCard.Width - 1, profileCard.Height - 1);
        };
        content.Controls.Add(profileCard);

        picAvatar = new PictureBox {
            Location = new Point(10, 9),
            Size = new Size(44, 44),
            BackColor = Color.FromArgb(18, 22, 32),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        picAvatar.Paint += (s, e) => DrawPlayerAvatar(e.Graphics, picAvatar.Width, picAvatar.Height);
        profileCard.Controls.Add(picAvatar);

        Label lblNick = new Label {
            Text = "Имя персонажа:",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 175, 195),
            Location = new Point(64, 8),
            AutoSize = true
        };
        profileCard.Controls.Add(lblNick);

        txtNickname = new TextBox {
            Text = _config.Username,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            BackColor = Color.FromArgb(16, 18, 26),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(66, 28),
            Size = new Size(368, 25)
        };
        txtNickname.TextChanged += (s, e) => {
            _config.Username = txtNickname.Text.Trim();
            SaveConfig();
            picAvatar.Invalidate();
        };
        profileCard.Controls.Add(txtNickname);

        // 2. Version Section
        Label lblVersion = new Label {
            Text = "Версия игры:",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 175, 195),
            Location = new Point(2, 74),
            AutoSize = true
        };
        content.Controls.Add(lblVersion);

        cbVersion = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(24, 28, 40),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(0, 94),
            Size = new Size(360, 28)
        };
        cbVersion.SelectedIndexChanged += (s, e) => UpdatePlayButtonState();
        content.Controls.Add(cbVersion);

        btnRefresh = new Button {
            Text = "🔄 Релизы",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            BackColor = Color.FromArgb(36, 44, 62),
            ForeColor = Color.FromArgb(210, 225, 245),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(366, 93),
            Size = new Size(82, 28),
            Cursor = Cursors.Hand
        };
        btnRefresh.FlatAppearance.BorderColor = _borderColor;
        btnRefresh.Click += async (s, e) => await RefreshReleasesAsync();
        content.Controls.Add(btnRefresh);

        // Download progress bar
        pbDownload = new ProgressBar {
            Location = new Point(0, 128),
            Size = new Size(448, 6),
            Visible = false
        };
        content.Controls.Add(pbDownload);

        lblStatus = new Label {
            Text = "Готов к запуску",
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(120, 200, 140),
            Location = new Point(2, 138),
            Size = new Size(444, 16)
        };
        content.Controls.Add(lblStatus);

        // 3. Play Button
        btnPlay = new Button {
            Text = "▶   В   Б О Й !",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            BackColor = _accentPrimary,
            ForeColor = Color.FromArgb(10, 16, 26),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(0, 162),
            Size = new Size(448, 54),
            Cursor = Cursors.Hand
        };
        btnPlay.FlatAppearance.BorderSize = 0;
        btnPlay.MouseEnter += (s, e) => {
            btnPlay.BackColor = Color.FromArgb(50, 230, 255);
        };
        btnPlay.MouseLeave += (s, e) => {
            btnPlay.BackColor = _accentPrimary;
        };
        btnPlay.Click += async (s, e) => await OnPlayClickedAsync();
        content.Controls.Add(btnPlay);

        // 4. Bottom Utility Links
        int linkY = 230;
        Button btnSaves = CreateLinkButton("📂 Сейвы", 0, linkY, 144, 30);
        btnSaves.Click += (s, e) => OpenDirectory(FindSavesDir() ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saves"));
        content.Controls.Add(btnSaves);

        Button btnScreens = CreateLinkButton("📸 Скриншоты", 152, linkY, 144, 30);
        btnScreens.Click += (s, e) => OpenDirectory(Path.Combine(FindRepoRoot() ?? AppDomain.CurrentDomain.BaseDirectory, "screenshots"));
        content.Controls.Add(btnScreens);

        Button btnVersions = CreateLinkButton("📁 Версии", 304, linkY, 144, 30);
        btnVersions.Click += (s, e) => OpenDirectory(GetVersionsDirectory());
        content.Controls.Add(btnVersions);

        Label lblHint = new Label {
            Text = "WASD — перемещение  •  E — инвентарь  •  F5 — вид  •  F3 — отладка",
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(110, 125, 145),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 272),
            Size = new Size(448, 20)
        };
        content.Controls.Add(lblHint);

        // Initial scan
        PopulateInitialVersions();

        // Check releases in background
        this.Shown += async (s, e) => {
            await CheckForLauncherUpdatesAsync();
            await RefreshReleasesAsync();
        };
    }

    private Button CreateLinkButton(string text, int x, int y, int w, int h) {
        var btn = new Button {
            Text = text,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(28, 33, 46),
            ForeColor = Color.FromArgb(190, 205, 230),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(x, y),
            Size = new Size(w, h),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = _borderColor;
        btn.MouseEnter += (s, e) => { btn.BackColor = Color.FromArgb(42, 50, 70); };
        btn.MouseLeave += (s, e) => { btn.BackColor = Color.FromArgb(28, 33, 46); };
        return btn;
    }

    private void DrawPlayerAvatar(Graphics g, int w, int h) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(32, 40, 58));
        g.FillRectangle(brush, 0, 0, w, h);

        // Draw stylized voxel head
        int headSize = 28;
        int hx = (w - headSize) / 2;
        int hy = (h - headSize) / 2;

        Color skinColor = Color.FromArgb(235, 185, 140);
        Color hairColor = Color.FromArgb(85, 55, 30);
        Color eyesColor = _accentPrimary;

        using (var skinBrush = new SolidBrush(skinColor)) {
            g.FillRectangle(skinBrush, hx, hy, headSize, headSize);
        }
        using (var hairBrush = new SolidBrush(hairColor)) {
            g.FillRectangle(hairBrush, hx, hy, headSize, 8);
            g.FillRectangle(hairBrush, hx, hy + 8, 4, 10);
            g.FillRectangle(hairBrush, hx + headSize - 4, hy + 8, 4, 10);
        }
        using (var eyeBrush = new SolidBrush(eyesColor)) {
            g.FillRectangle(eyeBrush, hx + 5, hy + 13, 5, 4);
            g.FillRectangle(eyeBrush, hx + headSize - 10, hy + 13, 5, 4);
        }

        using var borderPen = new Pen(_accentPrimary, 1.5f);
        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
    }

    private void ApplyThemeColors(int themeIndex) {
        switch (themeIndex) {
            case 1: // Monolith Stone
                _bgMain = Color.FromArgb(32, 35, 42);
                _bgHeader = Color.FromArgb(24, 26, 31);
                _bgCard = Color.FromArgb(40, 44, 54);
                _accentPrimary = Color.FromArgb(220, 225, 235);
                _accentGold = Color.FromArgb(190, 195, 205);
                _borderColor = Color.FromArgb(70, 76, 90);
                break;
            case 2: // Astral Void
                _bgMain = Color.FromArgb(18, 14, 28);
                _bgHeader = Color.FromArgb(12, 9, 18);
                _bgCard = Color.FromArgb(28, 22, 44);
                _accentPrimary = Color.FromArgb(192, 64, 255);
                _accentGold = Color.FromArgb(112, 160, 255);
                _borderColor = Color.FromArgb(75, 55, 110);
                break;
            default: // Voxalith Dark
                _bgMain = Color.FromArgb(20, 22, 30);
                _bgHeader = Color.FromArgb(14, 16, 22);
                _bgCard = Color.FromArgb(26, 30, 42);
                _accentPrimary = Color.FromArgb(0, 210, 255);
                _accentGold = Color.FromArgb(255, 215, 80);
                _borderColor = Color.FromArgb(48, 56, 76);
                break;
        }
        this.BackColor = _bgMain;
        if (headerPanel != null) headerPanel.BackColor = _bgHeader;
        if (btnPlay != null) btnPlay.BackColor = _accentPrimary;
    }

    private void OpenSettingsDialog() {
        using var dlg = new SettingsDialog(_config);
        if (dlg.ShowDialog(this) == DialogResult.OK) {
            _config = dlg.Config;
            SaveConfig();
            ApplyThemeColors(_config.ThemeIndex);
            this.Refresh();
        }
    }

    private void OpenDirectory(string? dir) {
        try {
            if (dir == null) return;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        } catch { }
    }

    private void PopulateInitialVersions() {
        _versions.Clear();

        string versionsDir = GetVersionsDirectory();
        if (Directory.Exists(versionsDir)) {
            foreach (var dir in Directory.GetDirectories(versionsDir)) {
                string name = Path.GetFileName(dir);
                string? exe = ResolveGameExe(dir);
                if (exe != null) {
                    _versions.Add(new GameReleaseItem {
                        DisplayName = $"💾 {name} (Установлена)",
                        Tag = name,
                        IsInstalled = true,
                        InstallPath = dir,
                        ExePath = exe
                    });
                }
            }
        }

        string? gameDir = FindGameDir();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? newestExe = null;

        if (gameDir != null) {
            string? releaseExe = ResolveGameExe(Path.Combine(gameDir, "bin", "Release", "net10.0"));
            string? debugExe = ResolveGameExe(Path.Combine(gameDir, "bin", "Debug", "net10.0"));
            if (releaseExe != null) newestExe = releaseExe;
            else if (debugExe != null) newestExe = debugExe;
        }
        if (newestExe == null) {
            newestExe = ResolveGameExe(baseDir);
        }

        if (newestExe != null && !_versions.Any(v => v.ExePath == newestExe)) {
            string devTag = ReadExeVersion(newestExe) ?? CurrentLauncherVersion;
            _versions.Add(new GameReleaseItem {
                DisplayName = $"🛠️ {devTag} (Dev сборка)",
                Tag = devTag,
                IsInstalled = true,
                InstallPath = Path.GetDirectoryName(newestExe)!,
                ExePath = newestExe
            });
        }

        cbVersion.Items.Clear();
        foreach (var v in _versions) cbVersion.Items.Add(v);
        if (cbVersion.Items.Count > 0) cbVersion.SelectedIndex = 0;
        UpdatePlayButtonState();
    }

    private static string? ResolveGameExe(string dir) {
        if (!Directory.Exists(dir)) return null;
        string[] candidates = { "Voxalith.exe", "Voxalith.Game.exe", "VoxelFrame.Game.exe" };
        foreach (var c in candidates) {
            string p = Path.Combine(dir, c);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private void UpdatePlayButtonState() {
        if (cbVersion.SelectedItem is GameReleaseItem item) {
            if (item.IsInstalled || item.IsLocalDev) {
                btnPlay.Text = "▶   В   Б О Й !";
                lblStatus.Text = $"Версия {item.Tag} готова к запуску";
                lblStatus.ForeColor = Color.FromArgb(120, 220, 140);
            } else {
                btnPlay.Text = "⬇   СКАЧАТЬ И ИГРАТЬ";
                lblStatus.Text = $"Будет загружен релиз {item.Tag}";
                lblStatus.ForeColor = Color.FromArgb(255, 205, 80);
            }
        }
    }

    private async Task OnPlayClickedAsync() {
        if (cbVersion.SelectedItem is not GameReleaseItem item) return;

        string nick = txtNickname.Text.Trim();
        if (string.IsNullOrWhiteSpace(nick)) nick = "Player";
        _config.Username = nick;
        SaveConfig();

        if (!item.IsInstalled && !item.IsLocalDev && !string.IsNullOrEmpty(item.DownloadUrl)) {
            bool ok = await DownloadAndInstallVersionAsync(item);
            if (!ok) return;
        }

        var (fullscreen, w, h) = ResolutionFromConfig();
        LaunchGame(item, nick, w, h, fullscreen, _config.RamMb);
    }

    private (bool Fullscreen, int W, int H) ResolutionFromConfig() {
        int w = 1280, h = 720;
        switch (_config.ScreenResolutionIndex) {
            case 1: w = 1600; h = 900; break;
            case 2: w = 1920; h = 1080; break;
            case 3: w = 2560; h = 1440; break;
            default: w = 1280; h = 720; break;
        }
        return (_config.Fullscreen, w, h);
    }

    private void LaunchGame(GameReleaseItem item, string nickname, int w, int h, bool fullscreen, int ramMb) {
        try {
            var psi = new ProcessStartInfo {
                CreateNoWindow = false,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            if (ramMb > 0) {
                ulong bytes = (ulong)ramMb * 1024UL * 1024UL;
                psi.EnvironmentVariables["DOTNET_GCHeapHardLimit"] = $"0x{bytes:X}";
            }

            string gameArgs = $"--username \"{nickname}\" --width {w} --height {h}{(fullscreen ? " --fullscreen" : "")}";

            if (item.IsLocalDev) {
                string? gameDir = FindGameDir();
                if (gameDir != null) {
                    string? builtExe = ResolveGameExe(Path.Combine(gameDir, "bin", "Release", "net10.0"))
                                    ?? ResolveGameExe(Path.Combine(gameDir, "bin", "Debug", "net10.0"));
                    if (builtExe == null) {
                        lblStatus.Text = "Компиляция проекта Voxalith...";
                        Application.DoEvents();
                        var buildPsi = new ProcessStartInfo {
                            FileName = "dotnet",
                            Arguments = "build -c Release",
                            WorkingDirectory = gameDir,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(buildPsi);
                        p?.WaitForExit();
                        builtExe = ResolveGameExe(Path.Combine(gameDir, "bin", "Release", "net10.0"));
                    }

                    if (builtExe != null && File.Exists(builtExe)) {
                        psi.FileName = builtExe;
                        psi.WorkingDirectory = Path.GetDirectoryName(builtExe) ?? gameDir;
                        psi.Arguments = gameArgs;
                    }
                }
            } else {
                string exePath = !string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath)
                    ? item.ExePath
                    : (ResolveGameExe(AppDomain.CurrentDomain.BaseDirectory) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Voxalith.exe"));

                if (File.Exists(exePath)) {
                    // Страховка: проверяем наличие raylib.dll рядом с exe
                    string? exeDir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(exeDir)) {
                        string raylibTarget = Path.Combine(exeDir, "raylib.dll");
                        if (!File.Exists(raylibTarget)) {
                            string? repoRoot = FindRepoRoot();
                            string fallbackRaylib = repoRoot != null ? Path.Combine(repoRoot, "versions", "v1.1.0-alpha", "raylib.dll") : "";
                            if (File.Exists(fallbackRaylib)) {
                                try { File.Copy(fallbackRaylib, raylibTarget, true); } catch { }
                            }
                        }
                    }

                    psi.FileName = exePath;
                    psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    psi.Arguments = gameArgs;
                } else {
                    MessageBox.Show("Исполняемый файл игры не найден:\n" + exePath, "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            lblStatus.Text = "Запуск Voxalith...";
            var proc = Process.Start(psi);
            if (proc != null) {
                // Если процесс сразу упал (в течение 600мс), сообщаем причину и не закрываем лаунчер
                if (proc.WaitForExit(600) && proc.ExitCode != 0) {
                    MessageBox.Show($"Игра завершилась с ошибкой (код {proc.ExitCode}).\nЗапустите RunVoxalith.bat для просмотра деталей в консоли.", "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    lblStatus.Text = "Ошибка запуска игры";
                    lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                    return;
                }
            }
            this.Close();
        } catch (Exception ex) {
            MessageBox.Show("Ошибка при запуске игры:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<bool> DownloadAndInstallVersionAsync(GameReleaseItem item) {
        string tempZip = Path.Combine(Path.GetTempPath(), $"Voxalith_{item.Tag}_{Guid.NewGuid():N}.zip");
        try {
            lblStatus.Text = $"Загрузка {item.Tag}...";
            lblStatus.ForeColor = Color.FromArgb(255, 215, 80);
            pbDownload.Visible = true;
            pbDownload.Value = 0;
            btnPlay.Enabled = false;

            _downloadCts = new CancellationTokenSource();
            using (var resp = await _http.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token)) {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1L;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buf = new byte[81920];
                long readSoFar = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buf, 0, buf.Length, _downloadCts.Token)) > 0) {
                    await fs.WriteAsync(buf, 0, bytesRead);
                    readSoFar += bytesRead;
                    if (total > 0) {
                        int pct = (int)(readSoFar * 100 / total);
                        pbDownload.Value = Math.Clamp(pct, 0, 100);
                        lblStatus.Text = $"Загрузка {item.Tag}: {readSoFar / (1024 * 1024)} MB из {total / (1024 * 1024)} MB ({pct}%)";
                    }
                }
            }

            lblStatus.Text = "Распаковка релиза...";
            string destDir = Path.Combine(GetVersionsDirectory(), item.Tag);
            Directory.CreateDirectory(destDir);
            ZipFile.ExtractToDirectory(tempZip, destDir, true);

            string? exe = ResolveGameExe(destDir);
            if (exe == null) throw new InvalidDataException("В архиве релиза не найден исполняемый файл игры.");

            item.IsInstalled = true;
            item.InstallPath = destDir;
            item.ExePath = exe;
            item.DisplayName = $"💾 {item.Tag} (Установлена)";

            int idx = cbVersion.SelectedIndex;
            cbVersion.Items[idx] = item;
            UpdatePlayButtonState();

            lblStatus.Text = $"Версия {item.Tag} успешно установлена!";
            lblStatus.ForeColor = Color.FromArgb(100, 220, 120);
            return true;
        } catch (Exception ex) {
            lblStatus.Text = "Ошибка установки: " + ex.Message;
            lblStatus.ForeColor = Color.FromArgb(240, 80, 80);
            MessageBox.Show("Ошибка скачивания/установки: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        } finally {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            pbDownload.Visible = false;
            btnPlay.Enabled = true;
        }
    }

    private async Task RefreshReleasesAsync() {
        btnRefresh.Enabled = false;
        lblStatus.Text = "Проверка обновлений на GitHub...";
        try {
            string repo = _config.GitHubRepo;
            string url = $"https://api.github.com/repos/{repo}/releases";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("VoxalithLauncher", "1.1"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) {
                // Fallback to VoxelFrame repo if Voxalith has no releases yet
                if (repo.Contains("Voxalith", StringComparison.OrdinalIgnoreCase)) {
                    repo = "nuiladnolol-art/VoxelFrame";
                    url = $"https://api.github.com/repos/{repo}/releases";
                    req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.Add(new ProductInfoHeaderValue("VoxalithLauncher", "1.1"));
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    resp = await _http.SendAsync(req);
                }
            }

            if (resp.IsSuccessStatusCode) {
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array) {
                    string versionsDir = GetVersionsDirectory();
                    foreach (var rel in root.EnumerateArray()) {
                        string tag = rel.GetProperty("tag_name").GetString() ?? "";
                        if (string.IsNullOrEmpty(tag)) continue;

                        string downloadUrl = "";
                        string checksumsUrl = "";
                        if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
                            foreach (var a in assets.EnumerateArray()) {
                                string name = a.GetProperty("name").GetString() ?? "";
                                string dl = a.GetProperty("browser_download_url").GetString() ?? "";
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) downloadUrl = dl;
                                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) checksumsUrl = dl;
                            }
                        }

                        string instDir = Path.Combine(versionsDir, tag);
                        string? exePath = ResolveGameExe(instDir);
                        bool installed = exePath != null;

                        var existing = _versions.Find(v => v.Tag == tag);
                        if (existing != null) {
                            existing.DownloadUrl = downloadUrl;
                            existing.ChecksumsUrl = checksumsUrl;
                            if (installed) {
                                existing.IsInstalled = true;
                                existing.InstallPath = instDir;
                                existing.ExePath = exePath!;
                                existing.DisplayName = $"💾 {tag} (Установлена)";
                            }
                        } else {
                            _versions.Add(new GameReleaseItem {
                                DisplayName = installed ? $"💾 {tag} (Установлена)" : $"☁️ {tag} (Доступна)",
                                Tag = tag,
                                DownloadUrl = downloadUrl,
                                ChecksumsUrl = checksumsUrl,
                                IsInstalled = installed,
                                InstallPath = installed ? instDir : "",
                                ExePath = exePath ?? ""
                            });
                        }
                    }

                    cbVersion.Items.Clear();
                    foreach (var v in _versions) cbVersion.Items.Add(v);
                    if (cbVersion.Items.Count > 0 && cbVersion.SelectedIndex < 0) cbVersion.SelectedIndex = 0;
                    UpdatePlayButtonState();
                    lblStatus.Text = "Список релизов обновлён";
                    lblStatus.ForeColor = Color.FromArgb(120, 220, 140);
                }
            } else {
                lblStatus.Text = "Локальный режим (GitHub недоступен)";
            }
        } catch (Exception ex) {
            lblStatus.Text = "Локальный режим: " + ex.Message;
        } finally {
            btnRefresh.Enabled = true;
        }
    }

    private async Task CheckForLauncherUpdatesAsync() {
        try {
            string repo = _config.GitHubRepo;
            string url = $"https://api.github.com/repos/{repo}/releases/latest";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("VoxalithLauncher", "1.1"));
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            _latestTag = root.GetProperty("tag_name").GetString() ?? "";
            _latestName = root.TryGetProperty("name", out var n) ? (n.GetString() ?? _latestTag) : _latestTag;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
                foreach (var a in assets.EnumerateArray()) {
                    string name = a.GetProperty("name").GetString() ?? "";
                    string dl = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        _latestSetupUrl = dl;
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        _latestZipUrl = dl;
                }
            }

            if (!string.IsNullOrEmpty(_latestTag) && IsNewerVersion(_latestTag, CurrentLauncherVersion)) {
                btnUpdateNotice.Visible = true;
            }
        } catch { }
    }

    private static bool IsNewerVersion(string latestTag, string currentVer) {
        try {
            string l = latestTag.TrimStart('v', 'V');
            string c = currentVer.TrimStart('v', 'V');
            if (Version.TryParse(l.Split('-')[0], out var vLatest) && Version.TryParse(c.Split('-')[0], out var vCur)) {
                return vLatest > vCur;
            }
        } catch { }
        return false;
    }

    private async Task AutoUpdateLauncherAsync(string tag, string setupUrl, string zipUrl) {
        try {
            string url = !string.IsNullOrEmpty(setupUrl) ? setupUrl : zipUrl;
            if (string.IsNullOrEmpty(url)) return;
            string tempFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));
            using (var s = await _http.GetStreamAsync(url))
            using (var fs = new FileStream(tempFile, FileMode.Create)) {
                await s.CopyToAsync(fs);
            }
            Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
            Application.Exit();
        } catch (Exception ex) {
            MessageBox.Show("Ошибка обновления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string GetConfigPath() {
        string? root = FindRepoRoot();
        string dir = root != null ? Path.Combine(root, "saves") : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saves");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "launcher.json");
    }

    private void LoadConfig() {
        try {
            string path = GetConfigPath();
            if (File.Exists(path)) {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<LauncherConfig>(json);
                if (loaded != null) _config = loaded;
            }
        } catch { }
    }

    private void SaveConfig() {
        try {
            string path = GetConfigPath();
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        } catch { }
    }

    private static string? FindRepoRoot() {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++) {
            if (File.Exists(Path.Combine(dir, "Voxalith.sln")) || File.Exists(Path.Combine(dir, "Voxalith.slnx")) || Directory.Exists(Path.Combine(dir, "versions")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? FindGameDir() {
        string? root = FindRepoRoot();
        if (root == null) return null;
        string p1 = Path.Combine(root, "src", "Voxalith.Game");
        if (Directory.Exists(p1)) return p1;
        string p2 = Path.Combine(root, "src", "VoxelFrame.Game");
        if (Directory.Exists(p2)) return p2;
        return null;
    }

    private static string? FindSavesDir() {
        string? root = FindRepoRoot();
        if (root == null) return null;
        return Path.Combine(root, "saves");
    }

    private static string GetVersionsDirectory() {
        string? root = FindRepoRoot();
        string dir = root != null ? Path.Combine(root, "versions") : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "versions");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class SettingsDialog : Form {
    public LauncherConfig Config { get; private set; }

    public SettingsDialog(LauncherConfig config) {
        this.Config = new LauncherConfig {
            Username = config.Username,
            ScreenResolutionIndex = config.ScreenResolutionIndex,
            Fullscreen = config.Fullscreen,
            RamMb = config.RamMb,
            ThemeIndex = config.ThemeIndex,
            GitHubRepo = config.GitHubRepo
        };

        this.Text = "Настройки Voxalith";
        this.Size = new Size(380, 360);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(24, 28, 38);
        this.ForeColor = Color.White;

        Font labelFont = new Font("Segoe UI", 9, FontStyle.Bold);
        Font inputFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        int y = 16;
        int w = 330;
        int x = 20;

        // RAM
        Label lblRam = new Label { Text = "Выделение оперативной памяти (RAM):", Location = new Point(x, y), Size = new Size(w, 20), Font = labelFont, ForeColor = Color.FromArgb(200, 215, 235) };
        this.Controls.Add(lblRam);
        y += 24;

        ComboBox cbRam = new ComboBox { Location = new Point(x, y), Size = new Size(w, 28), Font = inputFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(16, 20, 28), ForeColor = Color.White };
        cbRam.Items.AddRange(new object[] { "2 ГБ (Экономичный)", "4 ГБ (Рекомендуется)", "8 ГБ (Высокая производительность)", "16 ГБ (Максимум)" });
        cbRam.SelectedIndex = Config.RamMb switch {
            2048 => 0,
            8192 => 2,
            16384 => 3,
            _ => 1
        };
        this.Controls.Add(cbRam);
        y += 38;

        // Resolution
        Label lblRes = new Label { Text = "Разрешение экрана:", Location = new Point(x, y), Size = new Size(w, 20), Font = labelFont, ForeColor = Color.FromArgb(200, 215, 235) };
        this.Controls.Add(lblRes);
        y += 24;

        ComboBox cbRes = new ComboBox { Location = new Point(x, y), Size = new Size(w, 28), Font = inputFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(16, 20, 28), ForeColor = Color.White };
        cbRes.Items.AddRange(new object[] { "1280 × 720 (HD)", "1600 × 900 (HD+)", "1920 × 1080 (Full HD)", "2560 × 1440 (2K QHD)" });
        cbRes.SelectedIndex = Math.Clamp(Config.ScreenResolutionIndex, 0, 3);
        this.Controls.Add(cbRes);
        y += 38;

        // Fullscreen
        CheckBox chkFullscreen = new CheckBox {
            Text = "Полноэкранный режим (Fullscreen)",
            Location = new Point(x, y),
            Size = new Size(w, 24),
            Font = inputFont,
            ForeColor = Color.FromArgb(220, 230, 245),
            Checked = Config.Fullscreen
        };
        this.Controls.Add(chkFullscreen);
        y += 32;

        // Theme
        Label lblTheme = new Label { Text = "Тема оформления:", Location = new Point(x, y), Size = new Size(w, 20), Font = labelFont, ForeColor = Color.FromArgb(200, 215, 235) };
        this.Controls.Add(lblTheme);
        y += 24;

        ComboBox cbTheme = new ComboBox { Location = new Point(x, y), Size = new Size(w, 28), Font = inputFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(16, 20, 28), ForeColor = Color.White };
        cbTheme.Items.AddRange(new object[] { "Voxalith Dark (Неоновый лазурный)", "Monolith Stone (Монохромный камень)", "Astral Void (Фиолетовая бездна)" });
        cbTheme.SelectedIndex = Math.Clamp(Config.ThemeIndex, 0, 2);
        this.Controls.Add(cbTheme);
        y += 42;

        // Buttons
        Button btnSave = new Button {
            Text = "Сохранить",
            Location = new Point(x, y),
            Size = new Size(160, 36),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 180, 230),
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (s, e) => {
            Config.RamMb = cbRam.SelectedIndex switch {
                0 => 2048,
                2 => 8192,
                3 => 16384,
                _ => 4096
            };
            Config.ScreenResolutionIndex = cbRes.SelectedIndex;
            Config.Fullscreen = chkFullscreen.Checked;
            Config.ThemeIndex = cbTheme.SelectedIndex;
            this.DialogResult = DialogResult.OK;
            this.Close();
        };
        this.Controls.Add(btnSave);

        Button btnCancel = new Button {
            Text = "Отмена",
            Location = new Point(x + 170, y),
            Size = new Size(160, 36),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            BackColor = Color.FromArgb(40, 46, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        };
        this.Controls.Add(btnCancel);
    }
}

public static class IconHelper {
    public static void EnsureAppIcons() {
        try {
            string? root = AppDomain.CurrentDomain.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Voxalith.sln")) && !File.Exists(Path.Combine(root, "Voxalith.slnx"))) {
                root = Path.GetDirectoryName(root);
            }
            if (root == null) return;

            string assetsDir = Path.Combine(root, "assets");
            string iconPath = Path.Combine(assetsDir, "icon.png");
            if (!File.Exists(iconPath)) {
                Directory.CreateDirectory(assetsDir);
                using var bmp = DrawIsometricVoxelBlock(256);
                bmp.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        } catch { }
    }

    public static Icon? GetLauncherIcon() {
        try {
            string? root = AppDomain.CurrentDomain.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Voxalith.sln")) && !File.Exists(Path.Combine(root, "Voxalith.slnx"))) {
                root = Path.GetDirectoryName(root);
            }
            if (root != null) {
                string icoPath = Path.Combine(root, "assets", "icon.ico");
                if (File.Exists(icoPath)) return new Icon(icoPath);
            }
        } catch { }
        return null;
    }

    public static Bitmap DrawIsometricVoxelBlock(int size) {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float cx = size * 0.5f;
        float cy = size * 0.44f;
        float r = size * 0.42f;
        float rx = r * 0.866f;
        float ry = r * 0.500f;

        PointF[] topFace = {
            new PointF(cx, cy - r),
            new PointF(cx + rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx - rx, cy - ry)
        };
        using (var brush = new SolidBrush(Color.FromArgb(76, 174, 62))) {
            g.FillPolygon(brush, topFace);
        }

        PointF[] leftFace = {
            new PointF(cx - rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx, cy + r),
            new PointF(cx - rx, cy + ry)
        };
        using (var brush = new SolidBrush(Color.FromArgb(122, 92, 58))) {
            g.FillPolygon(brush, leftFace);
        }

        PointF[] rightFace = {
            new PointF(cx, cy),
            new PointF(cx + rx, cy - ry),
            new PointF(cx + rx, cy + ry),
            new PointF(cx, cy + r)
        };
        using (var brush = new SolidBrush(Color.FromArgb(98, 70, 42))) {
            g.FillPolygon(brush, rightFace);
        }

        using (var pen = new Pen(Color.FromArgb(30, 20, 15), Math.Max(1.5f, size / 64f))) {
            g.DrawPolygon(pen, topFace);
            g.DrawPolygon(pen, leftFace);
            g.DrawPolygon(pen, rightFace);
        }
        return bmp;
    }
}
