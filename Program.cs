using System;
using Raylib_cs;
using AbiogenesisEngine.Core;
using AbiogenesisEngine.Systems;
using System.IO;

namespace AbiogenesisEngine;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine(" Abiogenesis Engine v1.0");
        Console.WriteLine("Изолированная симуляция первичного бульона\n");

        int seed = (int)(DateTime.Now.Ticks & 0x7FFFFFFF);
        int initialCells = 300;
        int maxTicks = 20000;
        int mapWidth = 900;
        int targetSpecies = 15;
        bool noVisual = false;
        bool fullscreen = false;

        const int MAP_HEIGHT = 900;
        const int PANEL_H = 180;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--seed":
                case "-s":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int s)) { seed = s; i++; }
                    break;
                case "--cells":
                case "-c":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int c)) { initialCells = c; i++; }
                    break;
                case "--ticks":
                case "-t":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int t)) { maxTicks = t; i++; }
                    break;
                case "--map-size":
                case "-m":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) { mapWidth = m; i++; }
                    break;
                case "--fw":
                case "-fw":
                    fullscreen = true;
                    mapWidth = 1920;
                    break;
                case "--species":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int sp)) { targetSpecies = sp; i++; }
                    break;
                case "--no-visual":
                case "-nv":
                    noVisual = true;
                    break;
            }
        }

        Console.WriteLine($"[PARAMS] Seed: {seed}");
        Console.WriteLine($"[PARAMS] Initial Cells: {initialCells}");
        Console.WriteLine($"[PARAMS] Max Ticks: {maxTicks}");
        Console.WriteLine($"[PARAMS] Map Size: {mapWidth}x{MAP_HEIGHT}");
        Console.WriteLine($"[PARAMS] Target Species: {targetSpecies}");
        Console.WriteLine($"[PARAMS] Visual: {!noVisual} | FullScreen: {fullscreen}\n");

        var simulation = new Simulation(seed, initialCells, maxTicks, targetSpecies, mapWidth, MAP_HEIGHT);

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\n[🛑 EMERGENCY SHUTDOWN] Перехвачен сигнал Ctrl+C! Экспорт результатов...");
            e.Cancel = true;
            simulation.ExportResults();
            simulation.PrintDeathStatistics(); // <-- ДОБАВЛЯЕМ ЭТУ СТРОКУ
            SoupLogger.Shutdown();
            Console.WriteLine("[✅ SAFE SHUTDOWN COMPLETE] Выход из ядра.");
            Environment.Exit(0);
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            simulation.ExportResults();
            simulation.PrintDeathStatistics(); // <-- ДОБАВЛЯЕМ ЭТУ СТРОКУ
            SoupLogger.Shutdown();
        };

        try
        {
            if (!noVisual)
                RunWithVisual(simulation, mapWidth, MAP_HEIGHT, PANEL_H, fullscreen);
            else
                simulation.Run();

            Console.WriteLine("\nСимуляция полностью завершила весь цикл тактов. Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[💥 КРИТИЧЕСКИЙ КРАШ] {ex.Message}");
            Console.WriteLine("Пытаемся спасти данные о сформированных видах перед закрытием...");
        }
        finally
        {
            // Этот блок выполнится ВСЕГДА: и при нормальном выходе, и при Ctrl+C, и при краше
            simulation.ExportResults();
            simulation.PrintDeathStatistics(); // <-- ДОБАВЛЯЕМ ЭТУ СТРОКУ
            SoupLogger.Shutdown();
        }
    }

    static void RunWithVisual(Simulation simulation, int mapWidth, int mapHeight, int panelHeight, bool fullscreen)
    {
        int windowWidth = mapWidth;
        int windowHeight = mapHeight + panelHeight;
        float scale = 1.0f;
        // В Program.cs, внутри RunWithVisual, сразу после Raylib.InitWindow:
        Raylib.InitWindow(windowWidth, windowHeight, "Abiogenesis Engine | Full Screen");
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning); // <-- ДОБАВИТЬ ЭТУ СТРОКУ (скрывает INFO спам)
        if (fullscreen)
        {
            Raylib.SetWindowPosition(0, 0);
        }
        Raylib.SetTargetFPS(60);
        Console.WriteLine($"[VISUAL] Окно {windowWidth}x{windowHeight} | Масштаб {scale:F2} | FullScreen {fullscreen}\n");

        int tickCounter = simulation.GetTick();
        int speedMultiplier = 1;
        int mapRenderHeight = mapHeight;

        var map = simulation.GetMap();
        var vents = map.GetVentSites();

        // === ИНИЦИАЛИЗАЦИЯ ФОНА (ОДИН РАЗ) ===
        Image bgImage = Raylib.GenImageColor(windowWidth, mapRenderHeight, Color.Black);
        Texture2D bgTexture = Raylib.LoadTextureFromImage(bgImage);

        // Параметры для плавного полосного обновления (30 полос = полное обновление за 0.5 сек при 60 FPS)
        int numStrips = 30;
        int stripHeight = mapRenderHeight / numStrips;
        int currentStrip = 0;
        bool bgUpdateActive = true;
        // ===== КАДРОВЫЙ ЦИКЛ =====
        while (!Raylib.WindowShouldClose())
        {
            if (Raylib.IsKeyPressed(KeyboardKey.One)) speedMultiplier = 1;
            if (Raylib.IsKeyPressed(KeyboardKey.Two)) speedMultiplier = 5;
            if (Raylib.IsKeyPressed(KeyboardKey.Three)) speedMultiplier = 10;
            if (Raylib.IsKeyPressed(KeyboardKey.Space)) speedMultiplier = speedMultiplier == 0 ? 1 : 0;

            for (int i = 0; i < speedMultiplier; i++)
            {
                simulation.RunSingleTick();
                tickCounter = simulation.GetTick();
            }
            if (map.IsClimateMoving() && !bgUpdateActive)
            {
                bgUpdateActive = true;
                currentStrip = 0;
            }
            if (bgUpdateActive)
            {
                // === ПЛАВНОЕ ИНКРЕМЕНТАЛЬНОЕ ОБНОВЛЕНИЕ ФОНА ===

                int startY = currentStrip * stripHeight;
                int endY = Math.Min(startY + stripHeight, mapRenderHeight);

                for (int py = startY; py < endY; py++)
                {
                    for (int px = 0; px < windowWidth; px++)
                    {
                        float mapX = px / scale;
                        float mapY = py / scale;
                        if (mapX >= map.Width || mapY >= map.Height) continue;

                        float light = map.GetLightAt(mapX, mapY);
                        float toxin = map.GetToxinAt(mapX, mapY);

                        byte r = (byte)(10 + light * 40);
                        byte g = (byte)(30 + light * 100);
                        byte b = (byte)(80 + light * 120);

                        if (toxin > 0.05f)
                        {
                            float tk = Math.Min(1f, toxin);
                            float keep = 1f - tk * 0.72f;
                            r = (byte)(r * keep + tk * 38f);
                            g = (byte)(g * keep + tk * 22f);
                            b = (byte)(b * keep + tk * 14f);
                        }

                        float temp = map.GetTemperatureAt(mapX, mapY);

                        float r_blue = 15f, g_blue = 35f, b_blue = 140f;
                        float r_yellow = 230f, g_yellow = 200f, b_yellow = 50f;
                        float r_pink = 235f, g_pink = 60f, b_pink = 160f;
                        float r_brown = 135f, g_brown = 35f, b_brown = 25f;
                        float r_red = 255f, g_red = 15f, b_red = 15f;

                        float tempR = r_blue, tempG = g_blue, tempB = b_blue;

                        if (temp <= 0.20f)
                        {
                            tempR = r_blue; tempG = g_blue; tempB = b_blue;
                        }
                        else if (temp <= 0.40f)
                        {
                            float t = (temp - 0.20f) / 0.20f;
                            tempR = r_blue + (r_yellow - r_blue) * t;
                            tempG = g_blue + (g_yellow - g_blue) * t;
                            tempB = b_blue + (b_yellow - b_blue) * t;
                        }
                        else if (temp <= 0.51f)
                        {
                            float t = (temp - 0.40f) / 0.11f;
                            tempR = r_yellow + (r_pink - r_yellow) * t;
                            tempG = g_yellow + (g_pink - g_yellow) * t;
                            tempB = b_yellow + (b_pink - b_yellow) * t;
                        }
                        else if (temp <= 0.70f)
                        {
                            tempR = r_pink; tempG = g_pink; tempB = b_pink;
                        }
                        else if (temp <= 0.90f)
                        {
                            float t = (temp - 0.70f) / 0.20f;
                            tempR = r_pink + (r_brown - r_pink) * t;
                            tempG = g_pink + (g_brown - g_pink) * t;
                            tempB = b_pink + (b_brown - b_pink) * t;
                        }
                        else
                        {
                            float t = (temp - 0.90f) / 0.10f;
                            tempR = r_brown + (r_red - r_brown) * t;
                            tempG = g_brown + (g_red - g_brown) * t;
                            tempB = b_brown + (b_red - b_brown) * t;
                        }

                        r = (byte)Math.Clamp(tempR * (0.6f + light * 0.4f), 0f, 255f);
                        g = (byte)Math.Clamp(tempG * (0.6f + light * 0.4f), 0f, 255f);
                        b = (byte)Math.Clamp(tempB * (0.6f + light * 0.4f), 0f, 255f);

                        for (int vi = 0; vi < vents.Count; vi++)
                        {
                            var v = vents[vi];
                            float h = v.Y - mapY;
                            if (h < 0f || h > v.PlumeHeight) continue;
                            float half = v.CoreHalfBase + v.Slope * h;
                            float adx = MathF.Abs(mapX - v.X);
                            if (adx > half) continue;

                            float fade = 1f - h / v.PlumeHeight;
                            float across = 1f - (adx / MathF.Max(1f, half)) * 0.8f;
                            float keep = 1f - (fade * across) * 0.85f;
                            r = (byte)(r * keep);
                            g = (byte)(g * keep);
                            b = (byte)(b * keep);
                        }

                        int seabedTop = mapHeight - 18 - (int)(9f * MathF.Sin(px * 0.045f)) - (int)(5f * MathF.Sin(px * 0.11f + 1.3f));
                        if (mapY >= seabedTop)
                        {
                            byte rock = (byte)(34 + ((px * 7 + py * 13) & 7) * 4);
                            r = rock; g = (byte)(rock - 6); b = (byte)(rock - 2);
                        }

                        // Явное приведение всех аргументов к byte
                        Raylib.ImageDrawPixel(ref bgImage, px, py, new Color(r, g, b, (byte)255));
                    }
                }

                // МГНОВЕННОЕ обновление текстуры на GPU (принимаем сам объект Image, а не .data)
                Raylib.UnloadTexture(bgTexture);
                bgTexture = Raylib.LoadTextureFromImage(bgImage);

                currentStrip++;
                if (currentStrip >= numStrips)
                {
                    currentStrip = 0;
                    // Полный круг сделан. Климат встал — гасим. Ещё ползёт — крутим следующий круг.
                    if (!map.IsClimateMoving())
                        bgUpdateActive = false;
                }
            }
          

            // === КОНЕЦ ОБНОВЛЕНИЯ ФОНА ===

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Raylib.DrawTexture(bgTexture, 0, 0, Color.White);
            
            var freeCells = simulation.GetFreeCells();

            // Еда (2 типа)
            // Еда (Теперь 3 типа с учетом розового мяса)
            foreach (var n in map.GetNutrients())
            {
                if (n.Amount <= 0) continue;
                int px = (int)(n.X * scale);
                int py = (int)(n.Y * scale);
                if (px < 0 || px >= windowWidth || py < 0 || py >= mapRenderHeight) continue;

                // ИСПРАВЛЕНО: Даем каждому типу нутриента свой честный цвет
                Color color;
                if (n.Type == NutrientType.MineralSalt)
                {
                    color = new Color((byte)100, (byte)180, (byte)255, (byte)255); // Голубые минералы
                }
                else if (n.Type == NutrientType.OrganicSludge)
                {
                    color = new Color((byte)50, (byte)255, (byte)50, (byte)255);   // Зеленая органика
                }
                else
                {
                    color = new Color((byte)255, (byte)105, (byte)180, (byte)255); // Розовое мясо (MeatScraps)
                }

                Raylib.DrawRectangle(px - 1, py - 1, 3, 3, color);
            }


            // Клетки
            foreach (var cell in freeCells)
            {
                int px = (int)(cell.X * scale);
                int py = (int)(cell.Y * scale);
                if (px < 0 || px >= windowWidth || py < 0 || py >= mapRenderHeight) continue;

                Color color = GetCellColor(cell.Genome);
                if (cell.IsDying) color = Color.White; // Мигание белым при критическом состоянии

                Raylib.DrawPixel(px, py, color);
            }

            // 1. Собираем данные об Организмах (большие квадраты)
            var organismData = new Dictionary<uint, (float SumX, float SumY, int Count, AbiogenesisGenome FirstGenome)>();
            var organismOrder = new List<uint>();
            var organismSeen = new HashSet<uint>();

            for (int ci = 0; ci < freeCells.Count; ci++)
            {
                var cell = freeCells[ci];
                if (!cell.OrganismId.HasValue) continue;
                uint oid = cell.OrganismId.Value;
                if (organismSeen.Add(oid)) organismOrder.Add(oid);

                if (organismData.TryGetValue(oid, out var od))
                    organismData[oid] = (od.SumX + cell.X, od.SumY + cell.Y, od.Count + 1, od.FirstGenome);
                else
                    organismData[oid] = (cell.X, cell.Y, 1, cell.Genome);
            }
            organismOrder.Sort();

            for (int ci = 0; ci < organismOrder.Count; ci++)
            {
                uint oid = organismOrder[ci];
                var d = organismData[oid];
                int px = (int)((d.SumX / d.Count) * scale);
                int py = (int)((d.SumY / d.Count) * scale);
                if (px < 0 || px >= windowWidth || py < 0 || py >= mapRenderHeight) continue;

                // Большой квадрат для организма
                int size = Math.Max(8, (int)(MathF.Sqrt(d.Count) * scale * 1.5f));
                Raylib.DrawRectangleLines(px - size / 2, py - size / 2, size, size, Color.White);
            }

            // 2. Собираем данные об Органах (маленькие квадраты внутри)
            var organData = new Dictionary<uint, (float SumX, float SumY, int Count, AbiogenesisGenome FirstGenome)>();
            var organOrder = new List<uint>();
            var organSeen = new HashSet<uint>();

            for (int ci = 0; ci < freeCells.Count; ci++)
            {
                var cell = freeCells[ci];
                if (!cell.OrganId.HasValue) continue;
                uint oid = cell.OrganId.Value;
                if (organSeen.Add(oid)) organOrder.Add(oid);

                if (organData.TryGetValue(oid, out var od))
                    organData[oid] = (od.SumX + cell.X, od.SumY + cell.Y, od.Count + 1, od.FirstGenome);
                else
                    organData[oid] = (cell.X, cell.Y, 1, cell.Genome);
            }
            organOrder.Sort();

            for (int ci = 0; ci < organOrder.Count; ci++)
            {
                uint oid = organOrder[ci];
                var d = organData[oid];
                int px = (int)((d.SumX / d.Count) * scale);
                int py = (int)((d.SumY / d.Count) * scale);
                if (px < 0 || px >= windowWidth || py < 0 || py >= mapRenderHeight) continue;

                // Маленький квадрат для органа
                int size = Math.Max(4, (int)(MathF.Sqrt(d.Count) * scale * 0.8f));
                Raylib.DrawRectangleLines(px - size / 2, py - size / 2, size, size, GetCellColor(d.FirstGenome));
            }

            // Метеориты
            foreach (var m in simulation.GetMeteoroids())
            {
                float force = 0.6f; // Можно раскомментировать force = m.ImpactForce; если добавил поле

                if (!m.Exploded)
                {
                    int headPx = (int)(m.X * scale);
                    int headPy = (int)(m.Y * scale);

                    int tailLength = 12;
                    for (int t = 0; t < tailLength; t++)
                    {
                        float trailX = m.X - m.VX * t * 1.5f;
                        float trailY = m.Y - m.VY * t * 1.5f;
                        int px1 = (int)(trailX * scale);
                        int py1 = (int)(trailY * scale);
                        int px2 = (int)((trailX - m.VX * 1.5f) * scale);
                        int py2 = (int)((trailY - m.VY * 1.5f) * scale);

                        if ((px1 < 0 && px2 < 0) || (px1 >= windowWidth && px2 >= windowWidth) ||
                            (py1 < 0 && py2 < 0) || (py1 >= mapRenderHeight && py2 >= mapRenderHeight)) continue;

                        float fade = 1f - (t / (float)tailLength);
                        byte alpha = (byte)(fade * 220f);
                        Color tailColor;
                        if (t < 2) tailColor = new Color((byte)255, (byte)255, (byte)240, alpha);
                        else if (t < 5) tailColor = new Color((byte)255, (byte)240, (byte)150, alpha);
                        else if (t < 8) tailColor = new Color((byte)255, (byte)180, (byte)80, alpha);
                        else tailColor = new Color((byte)255, (byte)120, (byte)40, alpha);

                        Raylib.DrawLine(px1, py1, px2, py2, tailColor);
                    }

                    for (int s = 0; s < 5; s++)
                    {
                        float angle = s * 1.25f + (m.X * 0.1f);
                        float sparkDist = 4f + (s % 2) * 3f;
                        float sparkX = m.X + MathF.Cos(angle) * sparkDist;
                        float sparkY = m.Y + MathF.Sin(angle) * sparkDist;
                        int spx = (int)(sparkX * scale);
                        int spy = (int)(sparkY * scale);
                        if (spx >= 0 && spx < windowWidth && spy >= 0 && spy < mapRenderHeight)
                        {
                            byte sparkAlpha = (byte)(150 + (s * 20));
                            Raylib.DrawPixel(spx, spy, new Color((byte)255, (byte)220, (byte)100, sparkAlpha));
                        }
                    }

                    if (headPx >= 0 && headPx < windowWidth && headPy >= 0 && headPy < mapRenderHeight)
                    {
                        float headRadius = Math.Max(2f, 4f * scale);
                        Raylib.DrawCircle(headPx, headPy, headRadius * 1.5f, new Color((byte)255, (byte)200, (byte)80, (byte)180));
                        Raylib.DrawCircle(headPx, headPy, headRadius, Color.White);
                    }
                }
                else
                {
                    int px = (int)(m.X * scale);
                    int py = (int)(m.Y * scale);
                    if (px < 0 || px >= windowWidth || py < 0 || py >= mapRenderHeight) continue;

                    float maxFlash = 10f + force * 12f;
                    float progress = 1f - (m.ImpactFlash / maxFlash);

                    if (force >= 0.9f && progress < 0.15f)
                    {
                        float flashIntensity = 1f - (progress / 0.15f);
                        byte flashAlpha = (byte)(flashIntensity * 180f);
                        Raylib.DrawRectangle(0, 0, windowWidth, mapRenderHeight, new Color((byte)255, (byte)255, (byte)255, flashAlpha));
                    }

                    for (int ring = 0; ring < 3; ring++)
                    {
                        float ringDelay = ring * 0.15f;
                        float ringProgress = Math.Max(0f, progress - ringDelay) / Math.Max(0.01f, 1f - ringDelay);
                        if (ringProgress <= 0f) continue;

                        float baseRadius = 10f + ring * 15f;
                        float maxRadius = 40f + ring * 30f + force * 50f;
                        float radius = (baseRadius + ringProgress * (maxRadius - baseRadius)) * scale;

                        byte alpha = (byte)(200 - ringProgress * 180f);
                        Color ringColor;
                        if (ring == 0) ringColor = new Color((byte)255, (byte)255, (byte)255, alpha);
                        else if (ring == 1) ringColor = new Color((byte)255, (byte)200, (byte)100, alpha);
                        else ringColor = new Color((byte)255, (byte)120, (byte)50, alpha);

                        Raylib.DrawCircleLines(px, py, (int)radius, ringColor);
                    }

                    float coreRadius = Math.Max(2f, (5f + force * 8f - progress * 10f) * scale);
                    if (coreRadius > 0)
                    {
                        byte coreAlpha = (byte)(255 - progress * 200f);
                        Raylib.DrawCircle(px, py, (int)coreRadius, new Color((byte)255, (byte)255, (byte)240, coreAlpha));
                    }
                }
            }

            // ===== ПАНЕЛЬ СТАТИСТИКИ (ДВЕ КОЛОНКИ) =====
            int uiY = mapRenderHeight + 16;

            var colonyIds = new HashSet<uint>();
            var organIds = new HashSet<uint>();
            var organismIds = new HashSet<uint>();
            int loneCellsCount = 0;

            // Подсчет уникальных сущностей
            for (int i = 0; i < freeCells.Count; i++)
            {
                var cell = freeCells[i];

                // Колония (свободная или почка, но еще не орган)
                if (cell.ColonyId.HasValue && !cell.OrganId.HasValue)
                {
                    colonyIds.Add(cell.ColonyId.Value);
                }
                // Зрелый орган
                if (cell.OrganId.HasValue)
                {
                    organIds.Add(cell.OrganId.Value);
                }
                // Организм (крыша над органами)
                if (cell.OrganismId.HasValue)
                {
                    organismIds.Add(cell.OrganismId.Value);
                }
                // Полные одиночки
                if (!cell.ColonyId.HasValue && !cell.OrganId.HasValue && !cell.OrganismId.HasValue)
                {
                    loneCellsCount++;
                }
            }

            int col1X = 15;
            int col2X = 220; // Правая колонка
            int col3X = 380; // Правая колонка
            int rowY = uiY;
            int rowStep = 22;

            // Левая колонка: Базовая информация
            Raylib.DrawText($"Tick: {tickCounter}", col1X, rowY, 18, Color.White);
            Raylib.DrawText($"Cells: {freeCells.Count}", col1X, rowY + rowStep, 18, Color.White);
            Raylib.DrawText($"Lone Cells: {loneCellsCount}", col1X, rowY + rowStep * 2, 18, Color.SkyBlue);
            Raylib.DrawText($"Colonies: {colonyIds.Count}", col1X, rowY + rowStep * 3, 18, Color.Yellow);

            // Правая колонка: Высшие структуры
            Raylib.DrawText($"Organs: {organIds.Count}", col2X, rowY, 18, new Color(255, 165, 0, 255)); // Оранжевый
            Raylib.DrawText($"Organisms: {organismIds.Count}", col2X, rowY + rowStep, 18, Color.Green);
            Raylib.DrawText($"Species: {simulation.GetStableSpeciesCount()}/inf", col2X, rowY + rowStep * 2, 18, Color.Lime);
            Raylib.DrawText($"Beasts: {simulation.GetStableBeastCount()}/inf", col3X, rowY, 18, Color.Magenta);

            string speedText = speedMultiplier == 0 ? "PAUSED [Space]" : $"Speed x{speedMultiplier} [1,2,3]";
            Raylib.DrawText(speedText, col2X, rowY + rowStep * 3, 18, speedMultiplier == 0 ? Color.Red : Color.Yellow);
            Raylib.DrawText(speedText, 15, uiY + 110, 18, speedMultiplier == 0 ? Color.Red : Color.Yellow);


            int ly = uiY;
            int lx = windowWidth - 170;
            Raylib.DrawText("ENVIRONMENT", lx, ly, 12, Color.Gray);
            int ey = ly + 16;
            int row = 18;
            Raylib.DrawRectangle(lx, ey, 13, 13, new Color((byte)100, (byte)180, (byte)255, (byte)255));
            Raylib.DrawText("Minerals", lx + 20, ey - 1, 15, Color.White);
            Raylib.DrawRectangle(lx, ey + row, 13, 13, new Color((byte)50, (byte)255, (byte)50, (byte)255));
            Raylib.DrawText("Organic", lx + 20, ey + row - 1, 15, Color.White);
            Raylib.DrawRectangle(lx, ey + row * 2, 13, 13, new Color((byte)255, (byte)200, (byte)50, (byte)255));
            Raylib.DrawText("Chem trail", lx + 20, ey + row * 2 - 1, 15, Color.White);
            Raylib.DrawRectangle(lx, ey + row * 3, 13, 13, new Color((byte)70, (byte)45, (byte)30, (byte)255));
            Raylib.DrawText("Toxic plume", lx + 20, ey + row * 3 - 1, 15, Color.White);
            Raylib.DrawRectangle(lx, ey + row * 4, 13, 13, new Color((byte)255, (byte)120, (byte)60, (byte)255));
            Raylib.DrawText("Vent heat", lx + 20, ey + row * 4 - 1, 15, Color.White);
            Raylib.DrawRectangle(lx, ey + row * 5, 13, 13, new Color((byte)255, (byte)105, (byte)180, (byte)255));
            Raylib.DrawText("MeatScraps", lx + 20, ey + row * 5 - 1, 15, Color.White);

            int cx2 = windowWidth - 360;
            Raylib.DrawText("CELL TYPES", cx2, ly, 12, Color.Gray);
            int cy2 = ly + 16;
            int crow = 16;
            Raylib.DrawRectangle(cx2, cy2, 10, 10, new Color((byte)255, (byte)60, (byte)60, (byte)255));
            Raylib.DrawText("Predator", cx2 + 16, cy2 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow, 10, 10, new Color((byte)180, (byte)80, (byte)255, (byte)255));
            Raylib.DrawText("Resistant", cx2 + 16, cy2 + crow - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 2, 10, 10, new Color((byte)80, (byte)255, (byte)120, (byte)255));
            Raylib.DrawText("Phototroph", cx2 + 16, cy2 + crow * 2 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 3, 10, 10, new Color((byte)80, (byte)180, (byte)255, (byte)255));
            Raylib.DrawText("Chemotroph", cx2 + 16, cy2 + crow * 3 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 4, 10, 10, new Color((byte)255, (byte)220, (byte)80, (byte)255));
            Raylib.DrawText("Social", cx2 + 16, cy2 + crow * 4 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 5, 10, 10, new Color((byte)255, (byte)255, (byte)255, (byte)255));
            Raylib.DrawText("Sensor", cx2 + 16, cy2 + crow * 5 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 6, 10, 10, new Color((byte)255, (byte)160, (byte)50, (byte)255));
            Raylib.DrawText("Fast", cx2 + 16, cy2 + crow * 6 - 2, 12, Color.White);
            Raylib.DrawRectangle(cx2, cy2 + crow * 7, 10, 10, new Color((byte)190, (byte)190, (byte)190, (byte)255));
            Raylib.DrawText("Neutral", cx2 + 16, cy2 + crow * 7 - 2, 12, Color.White);

            Raylib.DrawText("Close window to save & exit", 15, windowHeight - 22, 14, Color.Gray);
            // === ДИАГНОСТИКА (временно): где реально 5000 клеток ===
            //int[] decile = new int[10];
            //int dPhoto = 0, dPred = 0, dRes = 0, dChemo = 0, dNeut = 0;
            //for (int di = 0; di < freeCells.Count; di++)
            //{
            //    var dc = freeCells[di];
            //    int band = Math.Clamp((int)(dc.Y / (float)mapRenderHeight * 10f), 0, 9);
            //    decile[band]++;
            //    var gg = dc.Genome;
            //    if (gg.PredatoryDrive > 0.6f) dPred++;
            //    else if (gg.EnvironmentResistance > 0.7f) dRes++;
            //    else if (gg.LightDependency > 0.7f) dPhoto++;
            //    else if (gg.ChemicalAffinity > 0.7f) dChemo++;
            //    else dNeut++;
            //}
            //Raylib.DrawRectangle(4, 4, 168, 150, new Color((byte)0, (byte)0, (byte)0, (byte)150));
            //Raylib.DrawText("DEPTH % (top->btm)", 8, 8, 11, Color.White);
            //for (int b = 0; b < 10; b++)
            //    Raylib.DrawText($"{b * 10,2}-{b * 10 + 10,3}% : {decile[b]}", 8, 22 + b * 11, 11, Color.White);
            //Raylib.DrawText($"PH{dPhoto} PR{dPred} RS{dRes} CH{dChemo} NT{dNeut}", 8, 134, 11, Color.Yellow);
            Raylib.EndDrawing();
            

        }

        simulation.ExportResults();
        Raylib.UnloadTexture(bgTexture);
        Raylib.UnloadImage(bgImage);
        SoupLogger.Shutdown();
        Raylib.CloseWindow();
    }

    static Color GetCellColor(AbiogenesisGenome genome)
    {
        // Приоритет стратегий (все аргументы явно приведены к byte)
        if (genome.PredatoryDrive > 0.6f)
            return new Color((byte)255, (byte)60, (byte)60, (byte)255);
        if (genome.EnvironmentResistance > 0.7f)
            return new Color((byte)180, (byte)80, (byte)255, (byte)255);
        if (genome.LightDependency > 0.7f)
            return new Color((byte)80, (byte)255, (byte)120, (byte)255);
        if (genome.ChemicalAffinity > 0.7f)
            return new Color((byte)80, (byte)180, (byte)255, (byte)255);
        if (genome.MergeAffinity > 0.7f)
            return new Color((byte)255, (byte)220, (byte)80, (byte)255);
        if (genome.SenseRadius > 0.7f)
            return new Color((byte)255, (byte)255, (byte)255, (byte)255);
        if (genome.Speed > 0.7f)
            return new Color((byte)255, (byte)160, (byte)50, (byte)255);

        // Нейтральный цвет
        int gray = (int)(150 + Math.Max(genome.LightDependency,
                               Math.Max(genome.ChemicalAffinity, genome.MergeAffinity)) * 80);

        return new Color((byte)gray, (byte)gray, (byte)gray, (byte)255);
    }
}