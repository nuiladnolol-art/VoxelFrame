using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoxelFrame.Installer;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        // Самообновление установщика: новая версия, запущенная с "--upgrade <старый exe> <pid>",
        // дожидается выхода старого процесса, заменяет его собой и перезапускает.
        if (args.Length >= 3 && args[0] == "--upgrade") {
            int oldPid = int.TryParse(args[2], out int p) ? p : -1;
            if (oldPid > 0) {
                try { Process.GetProcessById(oldPid)?.WaitForExit(); } catch { }
            }
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string oldExe = args[1];
            try {
                File.Copy(currentExe, oldExe, true);
                Process.Start(new ProcessStartInfo(oldExe) {
                    WorkingDirectory = Path.GetDirectoryName(oldExe) ?? ""
                });
            } catch { }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
    }
}

public class InstallerForm : Form {
    private TextBox txtPath;
    private Button btnBrowse;
    private CheckBox chkDesktopShortcut;
    private CheckBox chkStartMenuShortcut;
    private CheckBox chkLaunchAfter;
    private ProgressBar progressBar;
    private Label lblStatus;
    private Button btnInstall;
    private Button btnCancel;
    private Panel headerPanel;
    private ComboBox cmbVersion;
    private Button btnUpdateInstaller;
    private readonly List<ReleaseInfo> _releases = new();
    private readonly HttpClient _http = new();

    private sealed class ReleaseInfo {
        public string Tag = "";
        public string Name = "";
        public string GameZipUrl = "";
        public string InstallerUrl = "";
    }

    /// <summary>Версия установщика (берётся из csproj: &lt;Version&gt;).</summary>
    private static readonly Version ThisVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    private static string ThisVersionTag => $"v{ThisVersion.Major}.{ThisVersion.Minor}.{ThisVersion.Build}";

    private static int CompareVersions(string a, string b) => ParseTag(a).CompareTo(ParseTag(b));

    private static Version ParseTag(string tag) {
        string t = tag.TrimStart('v', 'V');
        var parts = t.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        return new Version(major, minor, build);
    }

    public InstallerForm() {
        this.Text = "Установка VoxelFrame Launcher";
        this.Size = new Size(620, 600);
        this.MinimumSize = new Size(580, 540);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(24, 26, 32);
        this.ForeColor = Color.White;
        try {
            string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(ico)) this.Icon = new Icon(ico);
        } catch { }

        Font titleFont = new Font("Segoe UI", 20, FontStyle.Bold);
        Font subTitleFont = new Font("Segoe UI", 10, FontStyle.Regular);
        Font labelFont = new Font("Segoe UI", 10, FontStyle.Bold);
        Font inputFont = new Font("Segoe UI", 10, FontStyle.Regular);

        // Header Panel
        headerPanel = new Panel {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(18, 20, 26)
        };
        this.Controls.Add(headerPanel);

        Label lblTitle = new Label {
            Text = "VOXELFRAME SETUP",
            Font = titleFont,
            ForeColor = Color.FromArgb(255, 215, 90),
            BackColor = Color.Transparent,
            Location = new Point(20, 14),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblTitle);

        Label lblSubTitle = new Label {
            Text = "Мастер установки VoxelFrame Launcher для Windows",
            Font = subTitleFont,
            ForeColor = Color.FromArgb(160, 170, 185),
            BackColor = Color.Transparent,
            Location = new Point(22, 50),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblSubTitle);

        int contentX = 35;
        int contentW = 530;

        // Тёмная подложка для основного контента
        Panel contentPanel = new Panel {
            Location = new Point(0, 85),
            Size = new Size(620, 515),
            BackColor = Color.FromArgb(24, 26, 32)
        };
        this.Controls.Add(contentPanel);

        // Папка установки
        Label lblFolder = new Label {
            Text = "Папка для установки:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            BackColor = Color.Transparent,
            Location = new Point(contentX, 20),
            Size = new Size(contentW, 22)
        };
        contentPanel.Controls.Add(lblFolder);

        string defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "VoxelFrame");

        txtPath = new TextBox {
            Text = defaultInstallDir,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(contentX, 45),
            Size = new Size(contentW - 110, 28)
        };
        contentPanel.Controls.Add(txtPath);

        btnBrowse = new Button {
            Text = "Обзор...",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            BackColor = Color.FromArgb(45, 52, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 100, 44),
            Size = new Size(100, 29)
        };
        btnBrowse.FlatAppearance.BorderSize = 0;
        btnBrowse.Click += (s, e) => {
            using var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = txtPath.Text;
            if (fbd.ShowDialog() == DialogResult.OK) {
                txtPath.Text = fbd.SelectedPath;
            }
        };
        contentPanel.Controls.Add(btnBrowse);

        // Опции
        Label lblOptions = new Label {
            Text = "Дополнительные параметры:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            BackColor = Color.Transparent,
            Location = new Point(contentX, 90),
            Size = new Size(contentW, 22)
        };
        contentPanel.Controls.Add(lblOptions);

        chkDesktopShortcut = new CheckBox {
            Text = "Создать ярлык на Рабочем столе",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            BackColor = Color.Transparent,
            Checked = true,
            Location = new Point(contentX + 10, 115),
            Size = new Size(contentW, 26)
        };
        contentPanel.Controls.Add(chkDesktopShortcut);

        chkStartMenuShortcut = new CheckBox {
            Text = "Создать ярлык в меню «Пуск»",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            BackColor = Color.Transparent,
            Checked = true,
            Location = new Point(contentX + 10, 145),
            Size = new Size(contentW, 26)
        };
        contentPanel.Controls.Add(chkStartMenuShortcut);

        chkLaunchAfter = new CheckBox {
            Text = "Запустить VoxelFrame Launcher после завершения",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            BackColor = Color.Transparent,
            Checked = true,
            Location = new Point(contentX + 10, 175),
            Size = new Size(contentW, 26)
        };
        contentPanel.Controls.Add(chkLaunchAfter);

        // Версия для установки (автопроверка GitHub)
        Label lblVersion = new Label {
            Text = "Версия для установки (GitHub):",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            BackColor = Color.Transparent,
            Location = new Point(contentX, 213),
            Size = new Size(contentW, 22)
        };
        contentPanel.Controls.Add(lblVersion);

        cmbVersion = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            Location = new Point(contentX, 238),
            Size = new Size(contentW - 190, 26)
        };
        contentPanel.Controls.Add(cmbVersion);

        btnUpdateInstaller = new Button {
            Text = "Обновить установщик",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(210, 155, 45),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 185, 237),
            Size = new Size(185, 27),
            Visible = false
        };
        btnUpdateInstaller.FlatAppearance.BorderSize = 0;
        btnUpdateInstaller.Click += async (s, e) => await SelfUpdateAsync();
        contentPanel.Controls.Add(btnUpdateInstaller);

        // Прогресс-бар
        progressBar = new ProgressBar {
            Location = new Point(contentX, 278),
            Size = new Size(contentW, 18),
            Visible = false
        };
        contentPanel.Controls.Add(progressBar);

        // Статус
        lblStatus = new Label {
            Text = "Проверка обновлений на GitHub...",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(160, 175, 195),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(contentX, 308),
            Size = new Size(contentW, 22)
        };
        contentPanel.Controls.Add(lblStatus);

        // Кнопка Установить
        btnInstall = new Button {
            Text = "▶  УСТАНОВИТЬ",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            BackColor = Color.FromArgb(50, 160, 75),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX, 345),
            Size = new Size(contentW - 140, 50)
        };
        btnInstall.FlatAppearance.BorderSize = 0;
        btnInstall.Click += async (s, e) => await StartInstallationAsync();
        contentPanel.Controls.Add(btnInstall);

        // Кнопка Отмена / Закрыть
        btnCancel = new Button {
            Text = "Отмена",
            Font = new Font("Segoe UI", 11, FontStyle.Regular),
            BackColor = Color.FromArgb(45, 50, 60),
            ForeColor = Color.FromArgb(200, 210, 225),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 130, 345),
            Size = new Size(130, 50)
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => this.Close();
        contentPanel.Controls.Add(btnCancel);

        // Автопроверка обновлений при запуске
        this.Shown += async (s, e) => await CheckForUpdatesAsync();
    }

    /// <summary>Запрашивает список релизов с GitHub, заполняет выбор версии и проверяет обновление установщика.</summary>
    private async Task CheckForUpdatesAsync() {
        try {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("VoxelFrame-Setup/1.0");
            string json = await _http.GetStringAsync(
                "https://api.github.com/repos/nuiladnolol-art/VoxelFrame/releases?per_page=8");
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            _releases.Clear();
            foreach (var rel in doc.RootElement.EnumerateArray()) {
                if ((rel.TryGetProperty("draft", out var d) && d.GetBoolean()) ||
                    (rel.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()))
                    continue;
                var info = new ReleaseInfo {
                    Tag = rel.TryGetProperty("tag_name", out var tg) ? tg.GetString() ?? "" : "",
                    Name = rel.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                };
                if (rel.TryGetProperty("assets", out var assets)) {
                    foreach (var asset in assets.EnumerateArray()) {
                        string an = asset.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";
                        string au = asset.TryGetProperty("browser_download_url", out var u2) ? u2.GetString() ?? "" : "";
                        if (an.StartsWith("VoxelFrame-", StringComparison.OrdinalIgnoreCase) && an.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            info.GameZipUrl = au;
                        else if (an.StartsWith("VoxelFrame-Setup", StringComparison.OrdinalIgnoreCase) && an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            info.InstallerUrl = au;
                    }
                }
                _releases.Add(info);
            }

            if (_releases.Count == 0) {
                lblStatus.Text = "Не удалось получить список версий. Нажмите «Установить».";
                return;
            }

            _releases.Sort((a, b) => CompareVersions(b.Tag, a.Tag));

            cmbVersion.Items.Clear();
            foreach (var r in _releases)
                cmbVersion.Items.Add(string.IsNullOrEmpty(r.Name) ? r.Tag : $"{r.Tag} — {r.Name}");
            cmbVersion.SelectedIndex = 0;

            var latest = _releases[0];
            lblStatus.Text = $"Готово. Последняя версия: {latest.Tag}";
            lblStatus.ForeColor = Color.FromArgb(100, 220, 120);

            // Предложение обновить сам установщик, если вышла новая версия
            if (!string.IsNullOrEmpty(latest.InstallerUrl) && CompareVersions(latest.Tag, ThisVersionTag) > 0) {
                btnUpdateInstaller.Text = $"Обновить установщик до {latest.Tag}";
                btnUpdateInstaller.Visible = true;
            }
        } catch {
            lblStatus.Text = "Нет соединения с GitHub. Будет установлена встроенная версия.";
            lblStatus.ForeColor = Color.FromArgb(235, 200, 90);
        }
    }

    /// <summary>
    /// Возвращает ссылку на ZIP выбранной версии, если её нужно скачивать с GitHub.
    /// Встроенный payload соответствует версии установщика — его качать не нужно.
    /// </summary>
    private string? GetSelectedGithubUrl() {
        if (_releases.Count == 0 || cmbVersion.SelectedIndex < 0) return null;
        var sel = _releases[cmbVersion.SelectedIndex];
        if (string.IsNullOrEmpty(sel.GameZipUrl)) return null;
        if (CompareVersions(sel.Tag, ThisVersionTag) == 0) return null;
        return sel.GameZipUrl;
    }

    /// <summary>Скачивает и распаковывает игру в целевую папку; возвращает успех.</summary>
    private async Task<bool> DownloadAndInstallAsync(string url, string targetDir) {
        try {
            string tempZip = Path.Combine(Path.GetTempPath(), "VoxelFrame_Install.zip");
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var fs = File.Create(tempZip))
            await using (var contentStream = await response.Content.ReadAsStreamAsync()) {
                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    downloaded += bytesRead;
                    if (totalBytes > 0) {
                        int progress = Math.Clamp(15 + (int)((downloaded * 60) / totalBytes), 15, 80);
                        progressBar.Invoke(() => progressBar.Value = progress);
                    }
                }
            }
            lblStatus.Invoke(() => lblStatus.Text = "Распаковка загруженных файлов...");
            progressBar.Invoke(() => progressBar.Value = 85);
            ZipFile.ExtractToDirectory(tempZip, targetDir, true);
            try { File.Delete(tempZip); } catch { }
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>Скачивает новую версию установщика и подменяет текущий exe (самообновление).</summary>
    private async Task SelfUpdateAsync() {
        var latest = _releases.FirstOrDefault(r => !string.IsNullOrEmpty(r.InstallerUrl));
        if (latest == null) return;
        if (MessageBox.Show($"Доступна новая версия установщика ({latest.Tag}).\nОбновить сейчас?",
                "Обновление установщика", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try {
            lblStatus.Text = $"Скачивание установщика {latest.Tag}...";
            btnUpdateInstaller.Enabled = false;
            string temp = Path.Combine(Path.GetTempPath(), "VoxelFrame-Setup-update.exe");
            var resp = await _http.GetAsync(latest.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(temp))
            await using (var cs = await resp.Content.ReadAsStreamAsync())
                await cs.CopyToAsync(fs);

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            int pid = Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo(temp) {
                Arguments = $"\"--upgrade\" \"{currentExe}\" {pid}"
            });
            Application.Exit();
        } catch (Exception ex) {
            MessageBox.Show("Не удалось обновить установщик:\n" + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            btnUpdateInstaller.Enabled = true;
        }
    }

    private async Task StartInstallationAsync() {
        string targetDir = txtPath.Text.Trim();
        if (string.IsNullOrEmpty(targetDir)) {
            MessageBox.Show("Пожалуйста, укажите папку для установки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnInstall.Enabled = false;
        btnBrowse.Enabled = false;
        txtPath.Enabled = false;
        chkDesktopShortcut.Enabled = false;
        chkStartMenuShortcut.Enabled = false;
        progressBar.Visible = true;
        progressBar.Value = 10;
        lblStatus.Text = "Подготовка файлов...";
        lblStatus.ForeColor = Color.FromArgb(200, 220, 255);

        // Ссылка на выбранную версию с GitHub (если она не совпадает со встроенной)
        string? githubUrl = GetSelectedGithubUrl();
        string githubTag = (_releases.Count > 0 && cmbVersion.SelectedIndex >= 0)
            ? _releases[cmbVersion.SelectedIndex].Tag : "";

        try {
            await Task.Run(async () => {
                Directory.CreateDirectory(targetDir);
                bool installed = false;

                // 0. Автозагрузка выбранной версии с GitHub (свежие версии подтягиваются сами)
                if (!string.IsNullOrEmpty(githubUrl)) {
                    lblStatus.Invoke(() => lblStatus.Text = $"Скачивание {githubTag} с GitHub...");
                    progressBar.Invoke(() => progressBar.Value = 15);
                    installed = await DownloadAndInstallAsync(githubUrl, targetDir);
                }

                // 1. Попытка извлечь встроенный payload.zip (если GitHub недоступен)
                var assembly = Assembly.GetExecutingAssembly();
                string[] resNames = assembly.GetManifestResourceNames();
                string? payloadRes = Array.Find(resNames, r => r.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
                if (payloadRes != null) {
                    lblStatus.Invoke(() => lblStatus.Text = "Распаковка встроенных файлов...");
                    using var stream = assembly.GetManifestResourceStream(payloadRes);
                    if (stream != null) {
                        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                        int total = archive.Entries.Count;
                        int current = 0;
                        foreach (var entry in archive.Entries) {
                            if (string.IsNullOrEmpty(entry.Name)) {
                                Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                                continue;
                            }
                            string dest = Path.Combine(targetDir, entry.FullName);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            entry.ExtractToFile(dest, true);
                            current++;
                            int progress = Math.Clamp(10 + (current * 80) / Math.Max(1, total), 10, 95);
                            progressBar.Invoke(() => progressBar.Value = progress);
                        }
                        installed = true;
                    }
                }

                // 2. Попытка скопировать из локальной папки сборки или файлов рядом
                if (!installed) {
                    string? sourceDir = FindSourceDir();
                    if (sourceDir != null && Directory.Exists(sourceDir)) {
                        lblStatus.Invoke(() => lblStatus.Text = "Копирование локальных файлов...");
                        CopyDirectory(sourceDir, targetDir, progressBar);
                        installed = true;
                    } else {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string[] allowedFiles = { "VoxelFrame.Launcher.exe", "VoxelFrame.Game.exe", "launcher.ico", "app.ico" };
                        bool copiedAny = false;
                        foreach (var fname in allowedFiles) {
                            string src = Path.Combine(baseDir, fname);
                            if (File.Exists(src)) {
                                File.Copy(src, Path.Combine(targetDir, fname), true);
                                copiedAny = true;
                            }
                        }
                        string srcAssets = Path.Combine(baseDir, "assets");
                        if (Directory.Exists(srcAssets)) {
                            CopyDirectory(srcAssets, Path.Combine(targetDir, "assets"), progressBar);
                        }
                        if (copiedAny) installed = true;
                    }
                }

                // 3. Последний fallback: запрос последней версии с GitHub Releases
                if (!installed) {
                    string fallbackUrl = "";
                    try {
                        string apiUrl = "https://api.github.com/repos/nuiladnolol-art/VoxelFrame/releases/latest";
                        string json = await _http.GetStringAsync(apiUrl);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("assets", out var assetsElem)) {
                            foreach (var asset in assetsElem.EnumerateArray()) {
                                string name = asset.GetProperty("name").GetString() ?? "";
                                // Игнорируем автогенерируемый GitHub'ом "Source code (zip)"
                                if (name.Contains("VoxelFrame", StringComparison.OrdinalIgnoreCase)
                                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                                    fallbackUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                    break;
                                }
                            }
                        }
                    } catch { }

                    if (!string.IsNullOrEmpty(fallbackUrl)) {
                        lblStatus.Invoke(() => lblStatus.Text = "Загрузка последней версии с GitHub Releases...");
                        progressBar.Invoke(() => progressBar.Value = 20);
                        installed = await DownloadAndInstallAsync(fallbackUrl, targetDir);
                    }
                }

                // Создание деинсталлятора
                CreateUninstallerScript(targetDir);

                // Создание ярлыков Windows
                string exePath = Path.Combine(targetDir, "VoxelFrame.Launcher.exe");
                if (!File.Exists(exePath)) {
                    exePath = Path.Combine(targetDir, "VoxelFrame.Game.exe");
                }

                if (chkDesktopShortcut.Checked && File.Exists(exePath)) {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktop, "VoxelFrame Launcher.lnk"), exePath, targetDir);
                }

                if (chkStartMenuShortcut.Checked && File.Exists(exePath)) {
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "VoxelFrame");
                    Directory.CreateDirectory(startMenu);
                    CreateShortcut(Path.Combine(startMenu, "VoxelFrame Launcher.lnk"), exePath, targetDir);
                }
            });

            progressBar.Value = 100;
            lblStatus.Text = "Установка успешно завершена!";
            lblStatus.ForeColor = Color.FromArgb(100, 220, 120);
            btnInstall.Text = "✓  ГОТОВО";
            btnInstall.BackColor = Color.FromArgb(40, 140, 65);
            btnInstall.Enabled = true;
            btnCancel.Visible = false;
            btnInstall.Click -= async (s, e) => await StartInstallationAsync();
            btnInstall.Click += (s, e) => {
                if (chkLaunchAfter.Checked) {
                    string exePath = Path.Combine(targetDir, "VoxelFrame.Launcher.exe");
                    if (File.Exists(exePath)) {
                        Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir });
                    }
                }
                this.Close();
            };
        } catch (Exception ex) {
            lblStatus.Text = "Ошибка установки: " + ex.Message;
            lblStatus.ForeColor = Color.FromArgb(235, 80, 80);
            MessageBox.Show("Произошла ошибка при установке:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            btnInstall.Enabled = true;
            btnBrowse.Enabled = true;
            txtPath.Enabled = true;
        }
    }

    private static string? FindSourceDir() {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? cur = baseDir;
        for (int i = 0; i < 6 && cur != null; i++) {
            string candidate = Path.Combine(cur, "src", "VoxelFrame.Launcher", "bin", "Release", "net10.0-windows", "win-x64");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "VoxelFrame.Launcher.exe")))
                return candidate;

            string debugCandidate = Path.Combine(cur, "src", "VoxelFrame.Launcher", "bin", "Debug", "net10.0-windows");
            if (Directory.Exists(debugCandidate) && File.Exists(Path.Combine(debugCandidate, "VoxelFrame.Launcher.exe")))
                return debugCandidate;

            string distCandidate = Path.Combine(cur, "dist");
            if (Directory.Exists(distCandidate)) {
                var dirs = Directory.GetDirectories(distCandidate);
                if (dirs.Length > 0) return dirs[0];
            }

            cur = Path.GetDirectoryName(cur);
        }
        return null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, ProgressBar pb) {
        var dir = new DirectoryInfo(sourceDir);
        var dirs = dir.GetDirectories("*", SearchOption.AllDirectories);
        var files = dir.GetFiles("*", SearchOption.AllDirectories);

        foreach (var subDir in dirs) {
            string rel = Path.GetRelativePath(sourceDir, subDir.FullName);
            Directory.CreateDirectory(Path.Combine(destinationDir, rel));
        }

        int count = 0;
        foreach (var file in files) {
            string rel = Path.GetRelativePath(sourceDir, file.FullName);
            string dest = Path.Combine(destinationDir, rel);
            file.CopyTo(dest, true);
            count++;
            int progress = Math.Clamp(10 + (count * 80) / Math.Max(1, files.Length), 10, 95);
            pb.Invoke(() => pb.Value = progress);
        }
    }

    private static void CreateUninstallerScript(string targetDir) {
        string cleanTargetDir = targetDir.TrimEnd('\\');
        string uninstBat = Path.Combine(cleanTargetDir, "uninstall.bat");
        string batContent = $"""
            @echo off
            echo Удаление VoxelFrame Launcher...
            taskkill /f /im VoxelFrame.Launcher.exe 2>nul
            taskkill /f /im VoxelFrame.Game.exe 2>nul
            timeout /t 1 /nobreak >nul
            del "%userprofile%\Desktop\VoxelFrame Launcher.lnk" 2>nul
            rmdir /s /q "%appdata%\Microsoft\Windows\Start Menu\Programs\VoxelFrame" 2>nul
            cd /d "%temp%"
            rmdir /s /q "{cleanTargetDir}" 2>nul
            echo VoxelFrame успешно удален.
            pause
            """;
        File.WriteAllText(uninstBat, batContent);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir) {
        try {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null) {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null) {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDir;
                    shortcut.Description = "VoxelFrame Official Launcher";
                    if (File.Exists(targetPath)) {
                        shortcut.IconLocation = $"{targetPath},0";
                    }
                    shortcut.Save();
                }
            }
        } catch { }
    }
}
