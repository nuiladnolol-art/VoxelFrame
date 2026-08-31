using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Raylib_cs;

namespace VoxelFrame.Game;

public static partial class Screens {
    public static bool InMultiplayerScreen = false;
    public static bool InDirectConnectModal = false;
    public static bool IsConnecting = false;
    public static string ConnectingStatus = "";

    public static string DirectConnectIp = "127.0.0.1";
    public static string DirectConnectPort = "25565";
    public static string PlayerNick = "Player" + Random.Shared.Next(100, 999);

    private static LanDiscovery? _lanDiscovery;
    private static int _activeInputField = 0; // 0: None, 1: Nick, 2: IP, 3: Port
    private static GameSession? _connectedSession;
    private static CancellationTokenSource? _connectCts;
    private static int _selectedServerIndex = -1;
    private static float _scrollOffset = 0f;
    private static float _lastClickTime = 0f;
    private static int _lastClickedIndex = -1;

    public static void OpenMultiplayerMenu() {
        InMultiplayerScreen = true;
        InDirectConnectModal = false;
        IsConnecting = false;
        _connectedSession = null;
        _selectedServerIndex = -1;
        _scrollOffset = 0f;
        _lanDiscovery ??= new LanDiscovery();
        _lanDiscovery.StartListener();
    }

    public static void CloseMultiplayerMenu() {
        InMultiplayerScreen = false;
        InDirectConnectModal = false;
        IsConnecting = false;
        _connectCts?.Cancel();
        _connectCts = null;
        _lanDiscovery?.StopListener();
    }

    public static void DrawMultiplayer(ref GameSession? session) {
        if (_connectedSession != null) {
            session = _connectedSession;
            _connectedSession = null;
            CloseMultiplayerMenu();
            return;
        }

        int sw = Ui.Vw, sh = Ui.Vh;
        _lanDiscovery?.CleanupOldServers();

        // Анимированный фон меню
        DrawMenuBackground(Raylib.GetFrameTime());

        if (IsConnecting) {
            DrawConnectingOverlay(sw, sh);
            return;
        }

        if (InDirectConnectModal) {
            DrawDirectConnectModal(sw, sh);
            return;
        }

        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);

        // ── Заголовок ──────────────────────────────────────────────────────────
        Fonts.DrawTitle3D("СЕТЕВАЯ ИГРА", sw / 2f, 32f, 36f);
        Fonts.DrawCentered("Локальные миры LAN и прямое подключение по IP", sw / 2f, 68f, 14f, new Color(190, 205, 225, 230));

        // ── Верхняя панель: Никнейм игрока ───────────────────────────────────
        int panelW = Math.Min(800, sw - 60);
        int panelX = (sw - panelW) / 2;
        int nickBarY = 88;
        int nickBarH = 40;

        var nickBarRect = new Rectangle(panelX, nickBarY, panelW, nickBarH);
        Raylib.DrawRectangleRounded(nickBarRect, 0.15f, 6, new Color(26, 30, 42, 230));
        Raylib.DrawRectangleRoundedLinesEx(nickBarRect, 0.15f, 6, 1.2f, new Color(55, 65, 88, 220));

        Fonts.Draw("Ваш никнейм:", panelX + 16f, nickBarY + 11f, 15f, new Color(210, 220, 235, 255));

        int nickInputW = 220;
        int nickInputX = panelX + 130;
        var nickInputRect = new Rectangle(nickInputX, nickBarY + 5f, nickInputW, 30f);
        bool nickHov = Raylib.CheckCollisionPointRec(mouse, nickInputRect);

        if (leftClick) {
            if (nickHov) {
                _activeInputField = 1;
            } else if (_activeInputField == 1) {
                _activeInputField = 0;
                SaveSystem.SaveSettings();
            }
        }

        Color nickBg = _activeInputField == 1 ? new Color(42, 50, 68, 255) : (nickHov ? new Color(34, 40, 54, 230) : new Color(20, 24, 34, 220));
        Color nickBorder = _activeInputField == 1 ? new Color(100, 200, 255, 255) : (nickHov ? new Color(80, 95, 125, 230) : new Color(45, 52, 70, 200));
        Raylib.DrawRectangleRounded(nickInputRect, 0.2f, 4, nickBg);
        Raylib.DrawRectangleRoundedLinesEx(nickInputRect, 0.2f, 4, 1.2f, nickBorder);

        bool cursorBlink = ((int)(Raylib.GetTime() * 2.5) % 2) == 0;
        string nickDisplay = PlayerNick + (_activeInputField == 1 && cursorBlink ? "_" : "");
        Fonts.Draw(nickDisplay, nickInputX + 10f, nickBarY + 11f, 15f, Color.White);

        HandleTextInput(ref PlayerNick, 1, maxLen: 16);

        // Подсказка справа в панели ника
        string statusHint = "Миры LAN определяются автоматически";
        Fonts.Draw(statusHint, panelX + panelW - Fonts.Measure(statusHint, 13f) - 16f, nickBarY + 12f, 13f, new Color(140, 160, 185, 200));

        // ── Список найденных серверов ─────────────────────────────────────────
        int listY = nickBarY + nickBarH + 10;
        int bottomH = 96; // Высота блока кнопок внизу
        int listH = sh - listY - bottomH - 12;
        var listRect = new Rectangle(panelX, listY, panelW, listH);

        Raylib.DrawRectangleRounded(listRect, 0.10f, 6, new Color(18, 22, 32, 235));
        Raylib.DrawRectangleRoundedLinesEx(listRect, 0.10f, 6, 1.5f, new Color(48, 58, 80, 240));

        var servers = _lanDiscovery?.FoundServers.OrderByDescending(s => s.LastSeen).ToList() ?? [];

        if (servers.Count == 0) {
            // Анимированный поиск серверов в сети
            float time = (float)Raylib.GetTime();
            int dots = ((int)(time * 2.5f)) % 4;
            string dotStr = new string('.', dots);

            float pulse = 0.5f + 0.5f * MathF.Sin(time * 3f);
            Color radarCol = new Color((byte)(70 + 40 * pulse), (byte)(160 + 50 * pulse), (byte)255, (byte)255);

            Fonts.DrawCentered($"Поиск серверов в локальной сети{dotStr}", sw / 2f, listY + listH / 2f - 32f, 20f, radarCol);
            Fonts.DrawCentered("Откройте мир на другом компьютере через меню: Esc → «Открыть для сети»", sw / 2f, listY + listH / 2f + 2f, 14f, new Color(180, 190, 210, 220));
            Fonts.DrawCentered("Или подключитесь напрямую по IP-адресу кнопкой ниже", sw / 2f, listY + listH / 2f + 26f, 13f, new Color(140, 150, 170, 180));
        } else {
            // Ограничивающий Scissor для списка серверов с масштабированием в реальные экранные пиксели
            int scissorX = (int)((panelX + 4) * Ui.CurrentScale);
            int scissorY = (int)((listY + 4) * Ui.CurrentScale);
            int scissorW = (int)((panelW - 8) * Ui.CurrentScale);
            int scissorH = (int)((listH - 8) * Ui.CurrentScale);

            Raylib.BeginScissorMode(scissorX, scissorY, scissorW, scissorH);

            const int cardH = 68;
            const int cardGap = 8;
            int totalContentH = servers.Count * (cardH + cardGap);

            if (Raylib.CheckCollisionPointRec(mouse, listRect)) {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0) {
                    _scrollOffset = Math.Clamp(_scrollOffset - wheel * 40f, 0f, Math.Max(0f, totalContentH - listH + 16));
                }
            }

            float cardY = listY + 8 - _scrollOffset;

            for (int i = 0; i < servers.Count; i++) {
                var s = servers[i];
                var cardRect = new Rectangle(panelX + 10, cardY, panelW - 20, cardH);
                bool isSelected = _selectedServerIndex == i;
                bool hovered = Raylib.CheckCollisionPointRec(mouse, cardRect);

                if (hovered && leftClick) {
                    float now = (float)Raylib.GetTime();
                    if (_lastClickedIndex == i && now - _lastClickTime < 0.35f) {
                        // Двойной клик — быстрое подключение
                        ConnectToServer(s.Host, s.Port);
                    }
                    _selectedServerIndex = i;
                    _lastClickedIndex = i;
                    _lastClickTime = now;
                }

                Color cardBg = isSelected ? new Color(38, 55, 80, 255) : (hovered ? new Color(30, 38, 54, 240) : new Color(22, 27, 38, 220));
                Color cardBorder = isSelected ? new Color(100, 200, 255, 255) : (hovered ? new Color(70, 110, 160, 230) : new Color(40, 50, 70, 200));

                Raylib.DrawRectangleRounded(cardRect, 0.14f, 4, cardBg);
                Raylib.DrawRectangleRoundedLinesEx(cardRect, 0.14f, 4, 1.2f, cardBorder);

                // Иконка LAN мира
                var iconRect = new Rectangle(cardRect.X + 8, cardRect.Y + 8, 52, 52);
                Raylib.DrawRectangleRounded(iconRect, 0.2f, 4, new Color(18, 90, 55, 255));
                Raylib.DrawRectangleRoundedLinesEx(iconRect, 0.2f, 4, 1f, new Color(50, 190, 110, 255));
                Fonts.DrawCentered("LAN", iconRect.X + iconRect.Width / 2f, iconRect.Y + 16f, 17f, Color.White);

                // Название мира и хост
                Fonts.Draw(s.WorldName, cardRect.X + 70f, cardRect.Y + 8f, 17f, Color.White);
                Fonts.Draw($"Хост: {s.HostPlayer}  ({s.Host}:{s.Port})", cardRect.X + 70f, cardRect.Y + 34f, 13f, new Color(170, 185, 205, 240));

                // Бейдж онлайна игроков
                string countTxt = $"👥 {s.PlayerCount} игр.";
                Fonts.Draw(countTxt, cardRect.X + cardRect.Width - 190f, cardRect.Y + 24f, 14f, new Color(255, 215, 100, 255));

                // Кнопка подключения на карточке
                int joinBtnW = 86, joinBtnH = 32;
                int joinBtnX = (int)(cardRect.X + cardRect.Width - joinBtnW - 10);
                int joinBtnY = (int)(cardRect.Y + 18);
                if (Button(joinBtnX, joinBtnY, joinBtnW, joinBtnH, "Войти", true)) {
                    ConnectToServer(s.Host, s.Port);
                }

                cardY += cardH + cardGap;
            }

            Raylib.EndScissorMode();
        }

        // ── Нижняя панель действий (2 ряда кнопок) ───────────────────────────
        int btnW = (panelW - 16) / 2;
        int btnH = 38;
        int row1Y = sh - bottomH + 6;
        int row2Y = row1Y + btnH + 8;

        // Ряд 1: [ Подключиться к выбранному ] | [ Прямое подключение ]
        bool hasSelection = _selectedServerIndex >= 0 && _selectedServerIndex < servers.Count;
        if (Button(panelX, row1Y, btnW, btnH, "Подключиться", hasSelection)) {
            if (hasSelection) {
                var s = servers[_selectedServerIndex];
                ConnectToServer(s.Host, s.Port);
            }
        }

        if (Button(panelX + btnW + 16, row1Y, btnW, btnH, "Прямое подключение", true)) {
            InDirectConnectModal = true;
            _activeInputField = 2; // Фокус на поле IP
        }

        // Ряд 2: [ Обновить список ] | [ Назад ]
        if (Button(panelX, row2Y, btnW, btnH, "Обновить", true)) {
            _lanDiscovery?.CleanupOldServers();
        }

        if (Button(panelX + btnW + 16, row2Y, btnW, btnH, "Назад", true) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            CloseMultiplayerMenu();
        }
    }

    private static void DrawDirectConnectModal(int sw, int sh) {
        int modalW = 520, modalH = 320;
        int mx = (sw - modalW) / 2, my = (sh - modalH) / 2;
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);

        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 190));

        var modalRect = new Rectangle(mx, my, modalW, modalH);
        Raylib.DrawRectangleRounded(modalRect, 0.08f, 6, new Color(24, 28, 38, 255));
        Raylib.DrawRectangleRoundedLinesEx(modalRect, 0.08f, 6, 2f, new Color(75, 95, 135, 255));

        Fonts.DrawTitle3D("ПРЯМОЕ ПОДКЛЮЧЕНИЕ", sw / 2f, my + 22f, 26f);
        Fonts.DrawCentered("Введите IP-адрес или домен сервера и порт", sw / 2f, my + 56f, 14f, new Color(175, 185, 205, 230));

        int fieldW = modalW - 60;
        int fieldH = 34;
        int fx = mx + 30;

        // 1. Поле ввода IP
        int ipY = my + 82;
        Fonts.Draw("IP-адрес или хост сервера:", fx, ipY, 14f, new Color(210, 220, 235, 255));
        var ipRect = new Rectangle(fx, ipY + 20, fieldW, fieldH);
        bool ipHov = Raylib.CheckCollisionPointRec(mouse, ipRect);

        // 2. Поле ввода Порта
        int portY = ipY + 66;
        Fonts.Draw("Порт сервера (по умолчанию 25565):", fx, portY, 14f, new Color(210, 220, 235, 255));
        var portRect = new Rectangle(fx, portY + 20, fieldW, fieldH);
        bool portHov = Raylib.CheckCollisionPointRec(mouse, portRect);

        if (leftClick) {
            if (ipHov) _activeInputField = 2;
            else if (portHov) _activeInputField = 3;
            else _activeInputField = 0;
        }

        // Переключение по Tab
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) {
            _activeInputField = _activeInputField == 2 ? 3 : 2;
        }

        bool blink = ((int)(Raylib.GetTime() * 2.5) % 2) == 0;

        // Рендер поля IP
        Color ipBg = _activeInputField == 2 ? new Color(38, 46, 62, 255) : new Color(16, 20, 28, 255);
        Color ipBorder = _activeInputField == 2 ? new Color(100, 200, 255, 255) : new Color(60, 70, 95, 230);
        Raylib.DrawRectangleRounded(ipRect, 0.18f, 4, ipBg);
        Raylib.DrawRectangleRoundedLinesEx(ipRect, 0.18f, 4, 1.2f, ipBorder);
        Fonts.Draw(DirectConnectIp + (_activeInputField == 2 && blink ? "_" : ""), fx + 10f, ipY + 27f, 15f, Color.White);

        // Рендер поля Port
        Color portBg = _activeInputField == 3 ? new Color(38, 46, 62, 255) : new Color(16, 20, 28, 255);
        Color portBorder = _activeInputField == 3 ? new Color(100, 200, 255, 255) : new Color(60, 70, 95, 230);
        Raylib.DrawRectangleRounded(portRect, 0.18f, 4, portBg);
        Raylib.DrawRectangleRoundedLinesEx(portRect, 0.18f, 4, 1.2f, portBorder);
        Fonts.Draw(DirectConnectPort + (_activeInputField == 3 && blink ? "_" : ""), fx + 10f, portY + 27f, 15f, Color.White);

        HandleTextInput(ref DirectConnectIp, 2, maxLen: 64);
        HandleTextInput(ref DirectConnectPort, 3, maxLen: 6);

        // Кнопки действий
        int modalBtnW = (fieldW - 16) / 2;
        int modalBtnY = my + modalH - 56;

        bool doConnect = Button(fx, modalBtnY, modalBtnW, 38, "Подключиться", true) || Raylib.IsKeyPressed(KeyboardKey.Enter);
        if (doConnect) {
            if (int.TryParse(DirectConnectPort, out int port) && port > 0 && port <= 65535) {
                SaveSystem.SaveSettings();
                InDirectConnectModal = false;
                ConnectToServer(DirectConnectIp.Trim(), port);
            } else {
                ConnectingStatus = "Неверный номер порта (1..65535)";
            }
        }

        if (Button(fx + modalBtnW + 16, modalBtnY, modalBtnW, 38, "Отмена", true) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            InDirectConnectModal = false;
        }
    }

    private static void DrawConnectingOverlay(int sw, int sh) {
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 200));
        int boxW = 460, boxH = 170;
        int bx = (sw - boxW) / 2, by = (sh - boxH) / 2;

        var boxRect = new Rectangle(bx, by, boxW, boxH);
        Raylib.DrawRectangleRounded(boxRect, 0.12f, 6, new Color(24, 28, 38, 255));
        Raylib.DrawRectangleRoundedLinesEx(boxRect, 0.12f, 6, 2f, new Color(100, 200, 255, 255));

        Fonts.DrawTitle3D("ПОДКЛЮЧЕНИЕ", sw / 2f, by + 22f, 22f);

        float dotTime = (float)Raylib.GetTime();
        int dots = (int)(dotTime * 3f) % 4;
        string anim = new string('.', dots);

        Fonts.DrawCentered(ConnectingStatus + anim, sw / 2f, by + 65f, 16f, Color.White);

        if (Button(bx + (boxW - 140) / 2, by + 108, 140, 38, "Отмена", true) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            _connectCts?.Cancel();
            _connectCts = null;
            IsConnecting = false;
        }
    }

    private static void HandleTextInput(ref string text, int targetField, int maxLen = 32) {
        if (_activeInputField != targetField) return;

        int charCode = Raylib.GetCharPressed();
        while (charCode > 0) {
            if (charCode >= 32 && text.Length < maxLen) {
                text += (char)charCode;
            }
            charCode = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && text.Length > 0) {
            text = text[..^1];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            _activeInputField = 0;
            SaveSystem.SaveSettings();
        }
    }

    private static void ConnectToServer(string host, int port) {
        IsConnecting = true;
        ConnectingStatus = $"Подключение к {host}:{port}";
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource();
        var token = _connectCts.Token;

        Task.Run(async () => {
            try {
                var client = await GameClient.ConnectAsync(host, port, PlayerNick);
                // Ждём Welcome пакет
                int waitMs = 0;
                while (!client.HasReceivedWelcome && waitMs < 5000) {
                    if (token.IsCancellationRequested) {
                        GameClient.Disconnect();
                        return;
                    }
                    await Task.Delay(50, token);
                    waitMs += 50;
                }

                if (!client.HasReceivedWelcome) {
                    ConnectingStatus = "Таймаут подключения к серверу";
                    await Task.Delay(1500, token);
                    IsConnecting = false;
                    return;
                }

                // Инициализируем локальный игровой мир с полученным сидом
                var newSession = GameSession.NewGame(client.ReceivedSeed, headless: false);
                newSession.GameMode = (GameMode)client.ReceivedGamemode;
                newSession.CheatsEnabled = client.ReceivedCheats;
                newSession.DayNight.TimeOfDay = client.ReceivedTimeOfDay;
                newSession.Player.Name = PlayerNick;
                client.BindSession(newSession);

                _connectedSession = newSession;
            } catch (Exception ex) {
                if (!token.IsCancellationRequested) {
                    ConnectingStatus = $"Ошибка: {ex.Message}";
                    await Task.Delay(2000);
                    IsConnecting = false;
                }
            }
        }, token);
    }
}
