using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoxelFrame.Launcher;

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
    public bool IsLocalDev { get; set; }
    public bool IsInstalled { get; set; }
    public string InstallPath { get; set; } = "";
    public string ExePath { get; set; } = "";

    public override string ToString() => DisplayName;
}

public class LauncherForm : Form {
    private TextBox txtNickname;
    private ComboBox cbVersion;
    private ComboBox cbResolution;
    private ComboBox cbRam;
    private Button btnPlay;
    private Button btnRefresh;
    private Label lblStatus;
    private ProgressBar pbDownload;

    private readonly List<GameReleaseItem> _versions = new();
    private static readonly HttpClient _http = new();
    private CancellationTokenSource? _downloadCts;

    public LauncherForm() {
        // Setup Form
        this.Text = "VoxelFrame Launcher — Менеджер версий (GitHub Releases)";
        this.Size = new Size(640, 560);
        this.MinimumSize = new Size(600, 520);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(24, 26, 32);
        this.ForeColor = Color.White;
        try {
            this.Icon = IconHelper.GetLauncherIcon();
        } catch { }

        Font titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
        Font subTitleFont = new Font("Segoe UI", 10, FontStyle.Regular);
        Font labelFont = new Font("Segoe UI", 10, FontStyle.Bold);
        Font inputFont = new Font("Segoe UI", 11, FontStyle.Regular);

        // Header Panel
        Panel headerPanel = new Panel {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(18, 20, 26)
        };
        this.Controls.Add(headerPanel);

        Label lblTitle = new Label {
            Text = "VOXELFRAME",
            Font = titleFont,
            ForeColor = Color.FromArgb(255, 215, 90),
            Location = new Point(20, 12),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblTitle);

        Label lblBadge = new Label {
            Text = "ALPHA 0.9.2",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 220, 120),
            BackColor = Color.FromArgb(30, 60, 40),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(245, 22),
            Size = new Size(100, 24)
        };
        headerPanel.Controls.Add(lblBadge);

        Label lblSubTitle = new Label {
            Text = "Официальный менеджер релизов · Поддержка GitHub Releases",
            Font = subTitleFont,
            ForeColor = Color.FromArgb(160, 170, 185),
            Location = new Point(22, 52),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblSubTitle);

        int contentX = 35;
        int contentW = 550;

        // 1. Версия игры (Менеджер версий)
        Label lblVer = new Label {
            Text = "Версия игры:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX, 100),
            Size = new Size(contentW - 130, 22)
        };
        this.Controls.Add(lblVer);

        btnRefresh = new Button {
            Text = "🔄 Проверить GitHub",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            BackColor = Color.FromArgb(45, 52, 68),
            ForeColor = Color.FromArgb(210, 220, 240),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 145, 96),
            Size = new Size(145, 26)
        };
        btnRefresh.FlatAppearance.BorderSize = 0;
        btnRefresh.Click += async (s, e) => await CheckGitHubReleasesAsync();
        this.Controls.Add(btnRefresh);

        cbVersion = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(contentX, 126),
            Size = new Size(contentW, 30)
        };
        cbVersion.SelectedIndexChanged += (s, e) => UpdatePlayButtonState();
        this.Controls.Add(cbVersion);

        // 2. Имя игрока (Никнейм)
        Label lblNickname = new Label {
            Text = "Имя игрока (Никнейм):",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX, 170),
            Size = new Size(contentW, 22)
        };
        this.Controls.Add(lblNickname);

        txtNickname = new TextBox {
            Text = LoadSavedNickname(),
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(contentX, 195),
            Size = new Size(contentW, 30)
        };
        this.Controls.Add(txtNickname);

        // 3. Режим и разрешение
        Label lblResolution = new Label {
            Text = "Режим экрана и разрешение:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX, 240),
            Size = new Size(265, 22)
        };
        this.Controls.Add(lblResolution);

        cbResolution = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(contentX, 265),
            Size = new Size(265, 30)
        };
        cbResolution.Items.AddRange(new object[] {
            "Окно 1280×720 (720p)",
            "Окно 1600×900 (900p)",
            "Окно 1920×1080 (1080p)",
            "Полный экран 1280×720",
            "Полный экран 1920×1080",
            "Полный экран 2560×1440"
        });
        cbResolution.SelectedIndex = LoadSavedScreenMode();
        this.Controls.Add(cbResolution);

        // 4. Память (RAM)
        Label lblRamTitle = new Label {
            Text = "Выделение памяти:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX + 285, 240),
            Size = new Size(265, 22)
        };
        this.Controls.Add(lblRamTitle);

        cbRam = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(contentX + 285, 265),
            Size = new Size(265, 30)
        };
        cbRam.Items.AddRange(new object[] {
            "2 ГБ RAM (Стандарт)",
            "4 ГБ RAM (Оптимально)",
            "8 ГБ RAM (Максимум)"
        });
        cbRam.SelectedIndex = 1;
        this.Controls.Add(cbRam);

        // Прогресс бар скачивания
        pbDownload = new ProgressBar {
            Location = new Point(contentX, 315),
            Size = new Size(contentW, 14),
            Visible = false
        };
        this.Controls.Add(pbDownload);

        // Статус
        lblStatus = new Label {
            Text = "Готов к запуску без открытия терминалов",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(140, 150, 165),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(contentX, 335),
            Size = new Size(contentW, 22)
        };
        this.Controls.Add(lblStatus);

        // Кнопка Запуска
        btnPlay = new Button {
            Text = "▶  ИГРАТЬ (VOXELFRAME BETA)",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            BackColor = Color.FromArgb(50, 160, 75),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX, 365),
            Size = new Size(contentW, 55)
        };
        btnPlay.FlatAppearance.BorderSize = 0;
        btnPlay.MouseEnter += (s, e) => {
            if (btnPlay.Enabled) btnPlay.BackColor = Color.FromArgb(65, 185, 95);
        };
        btnPlay.MouseLeave += (s, e) => {
            if (btnPlay.Enabled) btnPlay.BackColor = Color.FromArgb(50, 160, 75);
        };
        btnPlay.Click += async (s, e) => await HandlePlayClickAsync();
        this.Controls.Add(btnPlay);

        PopulateInitialVersions();
    }

    private static string GetVersionsDirectory() {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? gameDir = FindGameDir();
        string root = gameDir != null ? Path.GetFullPath(Path.Combine(gameDir, "..", "..")) : baseDir;
        string versionsDir = Path.Combine(root, "versions");
        Directory.CreateDirectory(versionsDir);
        return versionsDir;
    }

    private void PopulateInitialVersions() {
        _versions.Clear();

        string? gameDir = FindGameDir();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? newestExe = null;

        if (gameDir != null) {
            string debugExe = Path.Combine(gameDir, "bin", "Debug", "net10.0", "VoxelFrame.Game.exe");
            string releaseExe = Path.Combine(gameDir, "bin", "Release", "net10.0", "VoxelFrame.Game.exe");
            if (File.Exists(debugExe)) newestExe = debugExe;
            else if (File.Exists(releaseExe)) newestExe = releaseExe;
        }
        if (newestExe == null) {
            string localExe = Path.Combine(baseDir, "VoxelFrame.Game.exe");
            if (File.Exists(localExe)) newestExe = localExe;
        }

        if (newestExe != null) {
            _versions.Add(new GameReleaseItem {
                DisplayName = "⚡ VoxelFrame (Актуальная версия)",
                Tag = "v0.9.2",
                IsInstalled = true,
                InstallPath = Path.GetDirectoryName(newestExe)!,
                ExePath = newestExe
            });
        } else if (gameDir != null) {
            _versions.Add(new GameReleaseItem {
                DisplayName = "⚡ VoxelFrame (Актуальная версия)",
                Tag = "v0.9.2",
                IsLocalDev = true,
                IsInstalled = true,
                InstallPath = gameDir
            });
        }

        string versionsDir = GetVersionsDirectory();
        if (Directory.Exists(versionsDir)) {
            foreach (var dir in Directory.GetDirectories(versionsDir)) {
                string name = Path.GetFileName(dir);
                string exe = Path.Combine(dir, "VoxelFrame.Game.exe");
                if (File.Exists(exe)) {
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

        cbVersion.Items.Clear();
        foreach (var v in _versions) cbVersion.Items.Add(v);
        if (cbVersion.Items.Count > 0) cbVersion.SelectedIndex = 0;
        UpdatePlayButtonState();
    }

    private async Task CheckGitHubReleasesAsync() {
        btnRefresh.Enabled = false;
        lblStatus.Text = "Проверка обновлений на GitHub Releases...";
        lblStatus.ForeColor = Color.FromArgb(200, 220, 255);

        try {
            string repo = LoadSavedGitHubRepo();
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VoxelFrameLauncher", "1.0"));

            string url = $"https://api.github.com/repos/{repo}/releases";
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array) {
                    string versionsDir = GetVersionsDirectory();
                    int count = 0;
                    foreach (var rel in root.EnumerateArray()) {
                        string tag = rel.GetProperty("tag_name").GetString() ?? "";
                        string name = rel.TryGetProperty("name", out var np) ? np.GetString() ?? tag : tag;
                        string zipUrl = "";

                        if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
                            foreach (var asset in assets.EnumerateArray()) {
                                string aName = asset.GetProperty("name").GetString() ?? "";
                                if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                                    zipUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        string instDir = Path.Combine(versionsDir, tag);
                        string exePath = Path.Combine(instDir, "VoxelFrame.Game.exe");
                        bool installed = File.Exists(exePath);

                        var existing = _versions.Find(v => v.Tag == tag);
                        if (existing != null) {
                            existing.DownloadUrl = zipUrl;
                            existing.IsInstalled = installed;
                            existing.InstallPath = instDir;
                            existing.ExePath = exePath;
                        } else {
                            _versions.Add(new GameReleaseItem {
                                DisplayName = $"🌐 {name} ({tag}) {(installed ? "✓" : "")}",
                                Tag = tag,
                                DownloadUrl = zipUrl,
                                IsInstalled = installed,
                                InstallPath = instDir,
                                ExePath = exePath
                            });
                        }
                        count++;
                    }

                    cbVersion.Items.Clear();
                    foreach (var v in _versions) cbVersion.Items.Add(v);
                    if (cbVersion.Items.Count > 0) cbVersion.SelectedIndex = 0;

                    if (count > 0) {
                        lblStatus.Text = $"Найдено релизов на GitHub: {count}. Готово к игре";
                        lblStatus.ForeColor = Color.FromArgb(100, 220, 120);
                    } else {
                        lblStatus.Text = "На GitHub пока нет релизов. Доступна локальная сборка";
                        lblStatus.ForeColor = Color.FromArgb(180, 200, 235);
                    }
                }
            } else if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                lblStatus.Text = "Репозиторий ещё не опубликован на GitHub (доступна локальная версия)";
                lblStatus.ForeColor = Color.FromArgb(200, 185, 130);
            } else {
                lblStatus.Text = $"GitHub статус: {response.StatusCode} (используются локальные версии)";
                lblStatus.ForeColor = Color.FromArgb(200, 185, 130);
            }
        } catch {
            lblStatus.Text = "Режим оффлайн. Доступны локальные версии игры";
            lblStatus.ForeColor = Color.FromArgb(180, 185, 195);
        } finally {
            btnRefresh.Enabled = true;
            UpdatePlayButtonState();
        }
    }

    private void UpdatePlayButtonState() {
        if (cbVersion.SelectedItem is not GameReleaseItem item) return;

        if (item.IsLocalDev || item.IsInstalled) {
            btnPlay.Text = $"▶  ИГРАТЬ ({item.Tag})";
            btnPlay.BackColor = Color.FromArgb(50, 160, 75);
        } else if (!string.IsNullOrEmpty(item.DownloadUrl)) {
            btnPlay.Text = $"⬇  СКАЧАТЬ И ИГРАТЬ ({item.Tag})";
            btnPlay.BackColor = Color.FromArgb(40, 120, 210);
        } else {
            btnPlay.Text = $"▶  ИГРАТЬ ({item.Tag})";
            btnPlay.BackColor = Color.FromArgb(50, 160, 75);
        }
    }

    private async Task HandlePlayClickAsync() {
        if (cbVersion.SelectedItem is not GameReleaseItem item) return;

        string nickname = txtNickname.Text.Trim();
        if (string.IsNullOrEmpty(nickname)) nickname = "Player";
        int screenMode = cbResolution.SelectedIndex;
        var (fullscreen, w, h) = ResolutionFromIndex(screenMode);

        SaveConfig(nickname, screenMode);

        // Если не установлена и есть URL — сначала скачиваем
        if (!item.IsInstalled && !item.IsLocalDev && !string.IsNullOrEmpty(item.DownloadUrl)) {
            bool ok = await DownloadAndInstallVersionAsync(item);
            if (!ok) return;
        }

        LaunchGame(item, nickname, w, h, fullscreen);
    }

    private async Task<bool> DownloadAndInstallVersionAsync(GameReleaseItem item) {
        btnPlay.Enabled = false;
        cbVersion.Enabled = false;
        pbDownload.Visible = true;
        pbDownload.Value = 0;
        lblStatus.Text = $"Загрузка релиза {item.Tag} с GitHub...";
        lblStatus.ForeColor = Color.FromArgb(100, 200, 255);

        string tempZip = Path.Combine(Path.GetTempPath(), $"VoxelFrame_{item.Tag}_{Guid.NewGuid():N}.zip");
        _downloadCts = new CancellationTokenSource();

        try {
            using var resp = await _http.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? -1;
            using var stream = await resp.Content.ReadAsStreamAsync(_downloadCts.Token);
            using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, _downloadCts.Token)) > 0) {
                await fs.WriteAsync(buffer, 0, read, _downloadCts.Token);
                totalRead += read;
                if (totalBytes > 0) {
                    int percent = (int)((totalRead * 100) / totalBytes);
                    pbDownload.Value = Math.Clamp(percent, 0, 100);
                    lblStatus.Text = $"Загрузка {item.Tag}: {totalRead / 1024 / 1024} МБ / {totalBytes / 1024 / 1024} МБ ({percent}%)";
                }
            }
            fs.Close();

            lblStatus.Text = "Распаковка релиза...";
            string destDir = Path.Combine(GetVersionsDirectory(), item.Tag);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.CreateDirectory(destDir);

            ZipFile.ExtractToDirectory(tempZip, destDir, true);

            item.IsInstalled = true;
            item.InstallPath = destDir;
            item.ExePath = Path.Combine(destDir, "VoxelFrame.Game.exe");

            lblStatus.Text = $"Релиз {item.Tag} успешно установлен!";
            lblStatus.ForeColor = Color.FromArgb(100, 220, 120);
            return true;
        } catch (Exception ex) {
            lblStatus.Text = "Ошибка при загрузке релиза: " + ex.Message;
            lblStatus.ForeColor = Color.FromArgb(235, 80, 80);
            MessageBox.Show("Не удалось скачать релиз с GitHub:\n" + ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        } finally {
            if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
            pbDownload.Visible = false;
            btnPlay.Enabled = true;
            cbVersion.Enabled = true;
            UpdatePlayButtonState();
        }
    }

    private void LaunchGame(GameReleaseItem item, string nickname, int w, int h, bool fullscreen) {
        try {
            var psi = new ProcessStartInfo {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            string gameArgs = $"--username \"{nickname}\" --width {w} --height {h}{(fullscreen ? " --fullscreen" : "")}";

            if (item.IsLocalDev) {
                string? gameDir = FindGameDir();
                if (gameDir != null) {
                    string builtExe = Path.Combine(gameDir, "bin", "Debug", "net10.0", "VoxelFrame.Game.exe");
                    if (!File.Exists(builtExe))
                        builtExe = Path.Combine(gameDir, "bin", "Release", "net10.0", "VoxelFrame.Game.exe");

                    if (File.Exists(builtExe)) {
                        psi.FileName = builtExe;
                        psi.Arguments = gameArgs;
                        psi.WorkingDirectory = Path.GetDirectoryName(builtExe)!;
                    } else {
                        string csproj = Path.Combine(gameDir, "VoxelFrame.Game.csproj");
                        psi.FileName = "dotnet";
                        psi.Arguments = $"run --project \"{csproj}\" -- {gameArgs}";
                        psi.WorkingDirectory = gameDir;
                    }
                }
            } else {
                string exePath = !string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath)
                    ? item.ExePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoxelFrame.Game.exe");

                if (File.Exists(exePath)) {
                    psi.FileName = exePath;
                    psi.Arguments = gameArgs;
                    psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                } else {
                    string? gameDir = FindGameDir();
                    if (gameDir != null) {
                        string builtExe = Path.Combine(gameDir, "bin", "Debug", "net10.0", "VoxelFrame.Game.exe");
                        if (!File.Exists(builtExe))
                            builtExe = Path.Combine(gameDir, "bin", "Release", "net10.0", "VoxelFrame.Game.exe");

                        if (File.Exists(builtExe)) {
                            psi.FileName = builtExe;
                            psi.Arguments = gameArgs;
                            psi.WorkingDirectory = Path.GetDirectoryName(builtExe)!;
                        } else {
                            string csproj = Path.Combine(gameDir, "VoxelFrame.Game.csproj");
                            psi.FileName = "dotnet";
                            psi.Arguments = $"run --project \"{csproj}\" -- {gameArgs}";
                            psi.WorkingDirectory = gameDir;
                        }
                    } else {
                        MessageBox.Show("Исполняемый файл игры не найден!\n\nОжидался:\n" + exePath,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }

            lblStatus.Text = "Запуск игры...";
            Process.Start(psi);
            this.Close();
        } catch (Exception ex) {
            MessageBox.Show("Ошибка при запуске игры:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? FindGameDir() {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++) {
            string candidate = Path.Combine(dir, "src", "VoxelFrame.Game");
            if (File.Exists(Path.Combine(candidate, "VoxelFrame.Game.csproj")))
                return Path.GetFullPath(candidate);
            string sibling = Path.Combine(dir, "VoxelFrame.Game");
            if (File.Exists(Path.Combine(sibling, "VoxelFrame.Game.csproj")))
                return Path.GetFullPath(sibling);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? FindSavesDir() {
        string? gameDir = FindGameDir();
        if (gameDir == null) return null;
        string root = Path.GetFullPath(Path.Combine(gameDir, "..", ".."));
        return Path.Combine(root, "saves");
    }

    private string GetConfigPath() {
        string? savesDir = FindSavesDir();
        if (savesDir == null) savesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saves");
        Directory.CreateDirectory(savesDir);
        return Path.Combine(savesDir, "launcher.json");
    }

    private static (bool Fullscreen, int W, int H) ResolutionFromIndex(int idx) => idx switch {
        0 => (false, 1280, 720),
        1 => (false, 1600, 900),
        2 => (false, 1920, 1080),
        3 => (true,  1280, 720),
        4 => (true,  1920, 1080),
        5 => (true,  2560, 1440),
        _ => (false, 1280, 720),
    };

    private int LoadSavedScreenMode() {
        try {
            string path = GetConfigPath();
            if (File.Exists(path)) {
                string text = File.ReadAllText(path);
                if (text.Contains("\"ScreenMode\":")) {
                    int s = text.IndexOf("\"ScreenMode\":") + 13;
                    int e = text.IndexOf(",", s);
                    if (e == -1) e = text.IndexOf("}", s);
                    if (int.TryParse(text.Substring(s, e - s).Trim(), out int mode))
                        return Math.Clamp(mode, 0, 5);
                }
            }
        } catch { }
        return 0;
    }

    private string LoadSavedNickname() {
        try {
            string path = GetConfigPath();
            if (File.Exists(path)) {
                string text = File.ReadAllText(path);
                if (text.Contains("\"Username\":")) {
                    int start = text.IndexOf("\"Username\":") + 11;
                    int end = text.IndexOf(",", start);
                    if (end == -1) end = text.IndexOf("}", start);
                    return text.Substring(start, end - start).Replace("\"", "").Trim();
                }
            }
        } catch { }
        return "Player";
    }

    private string LoadSavedGitHubRepo() {
        try {
            string path = GetConfigPath();
            if (File.Exists(path)) {
                string text = File.ReadAllText(path);
                if (text.Contains("\"GitHubRepo\":")) {
                    int start = text.IndexOf("\"GitHubRepo\":") + 13;
                    int end = text.IndexOf(",", start);
                    if (end == -1) end = text.IndexOf("}", start);
                    string repo = text.Substring(start, end - start).Replace("\"", "").Trim();
                    if (!string.IsNullOrEmpty(repo)) return repo;
                }
            }
        } catch { }
        return "nuiladnolol-art/VoxelFrame";
    }

    private void SaveConfig(string nickname, int screenMode) {
        try {
            string path = GetConfigPath();
            string repo = LoadSavedGitHubRepo();
            string json = $"{{\n  \"Username\": \"{nickname}\",\n  \"ScreenMode\": {screenMode},\n  \"GitHubRepo\": \"{repo}\"\n}}";
            File.WriteAllText(path, json);
        } catch { }
    }
}

public static class IconHelper {
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon GetLauncherIcon() {
        try {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localIco = Path.Combine(baseDir, "launcher.ico");
            if (File.Exists(localIco)) {
                return new Icon(localIco);
            }
            using var bmp = RenderVoxelBlockIcon(64);
            IntPtr hIcon = bmp.GetHicon();
            try {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            } finally {
                DestroyIcon(hIcon);
            }
        } catch {
            return SystemIcons.Application;
        }
    }

    public static void EnsureAppIcons() {
        try {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? root = FindProjectRoot();
            if (root == null) return;

            string assetsDir = Path.Combine(root, "assets");
            Directory.CreateDirectory(assetsDir);

            int[] sizes = new[] { 16, 32, 48, 64, 128, 256 };
            List<Bitmap> bitmaps = new();
            foreach (int s in sizes) {
                bitmaps.Add(RenderVoxelBlockIcon(s));
            }

            // Save assets/icon.png
            string pngPath = Path.Combine(assetsDir, "icon.png");
            if (!File.Exists(pngPath)) {
                bitmaps[^1].Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            // Save multi-size .ico files
            string[] targetIcos = new[] {
                Path.Combine(assetsDir, "icon.ico"),
                Path.Combine(root, "src", "VoxelFrame.Game", "app.ico"),
                Path.Combine(root, "src", "VoxelFrame.Launcher", "launcher.ico"),
                Path.Combine(root, "src", "VoxelFrame.Installer", "app.ico"),
                Path.Combine(baseDir, "launcher.ico")
            };

            foreach (var path in targetIcos) {
                try {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    SaveMultiSizeIco(bitmaps, path);
                } catch { }
            }

            foreach (var bmp in bitmaps) bmp.Dispose();
        } catch { }
    }

    private static string? FindProjectRoot() {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++) {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "assets")))
                return Path.GetFullPath(dir);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static Bitmap RenderVoxelBlockIcon(int size) {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        float cx = size / 2f;
        float cy = size / 2f - size * 0.03f;
        float r = size * 0.44f;
        float rx = r * 0.866f;
        float ry = r * 0.5f;

        // Top face (Grass)
        PointF[] topFace = new[] {
            new PointF(cx, cy - r),
            new PointF(cx + rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx - rx, cy - ry)
        };
        using (var topBrush = new SolidBrush(Color.FromArgb(105, 185, 60))) {
            g.FillPolygon(topBrush, topFace);
        }

        // Left face (Dirt with grass overhang)
        PointF[] leftFace = new[] {
            new PointF(cx - rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx, cy + r),
            new PointF(cx - rx, cy + ry)
        };
        using (var leftBrush = new SolidBrush(Color.FromArgb(135, 95, 55))) {
            g.FillPolygon(leftBrush, leftFace);
        }
        float overhangH = r * 0.32f;
        PointF[] leftGrass = new[] {
            new PointF(cx - rx, cy - ry),
            new PointF(cx, cy),
            new PointF(cx, cy + overhangH),
            new PointF(cx - rx * 0.45f, cy - ry + overhangH * 1.3f),
            new PointF(cx - rx, cy - ry + overhangH)
        };
        using (var leftGrassBrush = new SolidBrush(Color.FromArgb(88, 162, 48))) {
            g.FillPolygon(leftGrassBrush, leftGrass);
        }

        // Right face (Dirt shaded with grass overhang)
        PointF[] rightFace = new[] {
            new PointF(cx, cy),
            new PointF(cx + rx, cy - ry),
            new PointF(cx + rx, cy + ry),
            new PointF(cx, cy + r)
        };
        using (var rightBrush = new SolidBrush(Color.FromArgb(98, 68, 38))) {
            g.FillPolygon(rightBrush, rightFace);
        }
        PointF[] rightGrass = new[] {
            new PointF(cx, cy),
            new PointF(cx + rx, cy - ry),
            new PointF(cx + rx, cy - ry + overhangH),
            new PointF(cx + rx * 0.45f, cy - ry + overhangH * 1.3f),
            new PointF(cx, cy + overhangH)
        };
        using (var rightGrassBrush = new SolidBrush(Color.FromArgb(68, 132, 36))) {
            g.FillPolygon(rightGrassBrush, rightGrass);
        }

        // 3D Isometric Outline
        using (var pen = new Pen(Color.FromArgb(35, 22, 14), Math.Max(1.2f, size / 48f))) {
            g.DrawPolygon(pen, topFace);
            g.DrawPolygon(pen, leftFace);
            g.DrawPolygon(pen, rightFace);
        }

        return bmp;
    }

    public static void SaveMultiSizeIco(List<Bitmap> bitmaps, string filePath) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType (1 = Icon)
        bw.Write((ushort)bitmaps.Count); // idCount

        int offset = 6 + 16 * bitmaps.Count;
        List<byte[]> pngs = new();
        foreach (var bmp in bitmaps) {
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            byte[] pngData = pngMs.ToArray();
            pngs.Add(pngData);

            bw.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));
            bw.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height));
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)pngData.Length); // Size of image data
            bw.Write((uint)offset); // Offset of image data
            offset += pngData.Length;
        }

        foreach (var png in pngs) {
            bw.Write(png);
        }

        bw.Flush();
        File.WriteAllBytes(filePath, ms.ToArray());
    }
}

