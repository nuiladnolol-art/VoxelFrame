using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoxelFrame.Installer;

internal static class Program {
    [STAThread]
    private static void Main() {
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

    public InstallerForm() {
        this.Text = "Установка VoxelFrame Launcher";
        this.Size = new Size(620, 520);
        this.MinimumSize = new Size(580, 480);
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
            Location = new Point(20, 14),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblTitle);

        Label lblSubTitle = new Label {
            Text = "Мастер установки VoxelFrame Launcher для Windows",
            Font = subTitleFont,
            ForeColor = Color.FromArgb(160, 170, 185),
            Location = new Point(22, 50),
            AutoSize = true
        };
        headerPanel.Controls.Add(lblSubTitle);

        int contentX = 35;
        int contentW = 530;

        // Папка установки
        Label lblFolder = new Label {
            Text = "Папка для установки:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX, 105),
            Size = new Size(contentW, 22)
        };
        this.Controls.Add(lblFolder);

        string defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "VoxelFrame");

        txtPath = new TextBox {
            Text = defaultInstallDir,
            Font = inputFont,
            BackColor = Color.FromArgb(36, 40, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(contentX, 130),
            Size = new Size(contentW - 110, 28)
        };
        this.Controls.Add(txtPath);

        btnBrowse = new Button {
            Text = "Обзор...",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            BackColor = Color.FromArgb(45, 52, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 100, 129),
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
        this.Controls.Add(btnBrowse);

        // Опции
        Label lblOptions = new Label {
            Text = "Дополнительные параметры:",
            Font = labelFont,
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(contentX, 180),
            Size = new Size(contentW, 22)
        };
        this.Controls.Add(lblOptions);

        chkDesktopShortcut = new CheckBox {
            Text = "Создать ярлык на Рабочем столе",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            Checked = true,
            Location = new Point(contentX + 10, 205),
            Size = new Size(contentW, 26)
        };
        this.Controls.Add(chkDesktopShortcut);

        chkStartMenuShortcut = new CheckBox {
            Text = "Создать ярлык в меню «Пуск»",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            Checked = true,
            Location = new Point(contentX + 10, 235),
            Size = new Size(contentW, 26)
        };
        this.Controls.Add(chkStartMenuShortcut);

        chkLaunchAfter = new CheckBox {
            Text = "Запустить VoxelFrame Launcher после завершения",
            Font = inputFont,
            ForeColor = Color.FromArgb(210, 220, 235),
            Checked = true,
            Location = new Point(contentX + 10, 265),
            Size = new Size(contentW, 26)
        };
        this.Controls.Add(chkLaunchAfter);

        // Прогресс-бар
        progressBar = new ProgressBar {
            Location = new Point(contentX, 315),
            Size = new Size(contentW, 18),
            Visible = false
        };
        this.Controls.Add(progressBar);

        // Статус
        lblStatus = new Label {
            Text = "Нажмите «Установить» для начала копирования файлов",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(140, 150, 165),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(contentX, 345),
            Size = new Size(contentW, 22)
        };
        this.Controls.Add(lblStatus);

        // Кнопка Установить
        btnInstall = new Button {
            Text = "▶  УСТАНОВИТЬ",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            BackColor = Color.FromArgb(50, 160, 75),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX, 385),
            Size = new Size(contentW - 140, 50)
        };
        btnInstall.FlatAppearance.BorderSize = 0;
        btnInstall.Click += async (s, e) => await StartInstallationAsync();
        this.Controls.Add(btnInstall);

        // Кнопка Отмена / Закрыть
        btnCancel = new Button {
            Text = "Отмена",
            Font = new Font("Segoe UI", 11, FontStyle.Regular),
            BackColor = Color.FromArgb(45, 50, 60),
            ForeColor = Color.FromArgb(200, 210, 225),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Location = new Point(contentX + contentW - 130, 385),
            Size = new Size(130, 50)
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => this.Close();
        this.Controls.Add(btnCancel);
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

        try {
            await Task.Run(async () => {
                Directory.CreateDirectory(targetDir);
                bool installed = false;

                // 1. Попытка извлечь встроенный payload.zip (автономный инсталлятор)
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

                // 3. Если файлов нет — загрузка последней версии с GitHub Releases
                if (!installed) {
                    lblStatus.Invoke(() => lblStatus.Text = "Загрузка компонентов с GitHub Releases...");
                    progressBar.Invoke(() => progressBar.Value = 20);
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("VoxelFrame-Setup/1.0");
                    string downloadUrl = "https://github.com/nuiladnolol-art/VoxelFrame/releases/download/v0.9.4/VoxelFrame-v0.9.4-win-x64.zip";

                    try {
                        string apiUrl = "https://api.github.com/repos/nuiladnolol-art/VoxelFrame/releases/latest";
                        string json = await http.GetStringAsync(apiUrl);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("assets", out var assetsElem)) {
                            foreach (var asset in assetsElem.EnumerateArray()) {
                                string name = asset.GetProperty("name").GetString() ?? "";
                                // Игнорируем автогенерируемый GitHub'ом "Source code (zip)" (именуется по тегу, напр. v0.9.3.zip)
                                if (name.Contains("VoxelFrame", StringComparison.OrdinalIgnoreCase)
                                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? downloadUrl;
                                    break;
                                }
                            }
                        }
                    } catch { }

                    string tempZip = Path.Combine(Path.GetTempPath(), "VoxelFrame_Install.zip");
                    var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
                                int progress = Math.Clamp(20 + (int)((downloaded * 60) / totalBytes), 20, 80);
                                progressBar.Invoke(() => progressBar.Value = progress);
                            }
                        }
                    }

                    lblStatus.Invoke(() => lblStatus.Text = "Распаковка загруженных файлов...");
                    progressBar.Invoke(() => progressBar.Value = 85);
                    ZipFile.ExtractToDirectory(tempZip, targetDir, true);
                    try { File.Delete(tempZip); } catch { }
                    installed = true;
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
