using System;
using System.Collections.Generic;
using System.Linq;

namespace AbiogenesisEngine.Core;

public class AbiogenesisMap
{
    public int Width { get; set; } = 2000;
    public int Height { get; set; } = 2000;
    private List<Nutrient> _nutrients = new();
    private bool _nutrientGridDirty = false;
    private Dictionary<(int, int), List<Nutrient>> _nutrientGrid = new();
    private const int CellSize = 50;

    private Random _rng;
    private float[,] _lightMap;
    private float[,] _toxinMap;
    private float[,] _temperatureMap;


    // Жерло + геометрия шлейфа: высота струи, полуширины яда/дыма и тепла у жерла, раскрытие конуса.
    private List<(float X, float Y, float PlumeHeight, float CoreHalfBase, float HeatHalfBase, float Slope)> _ventSites = new();

    public List<(float X, float Y, float PlumeHeight, float CoreHalfBase, float HeatHalfBase, float Slope)> GetVentSites() => _ventSites;

    private List<(int X, int Y)> _chemicalPixels = new();

    public AbiogenesisMap(int seed, int width = 2000, int height = 2000)
    {
        Width = width;
        Height = height;
        _rng = new Random(seed);

        // Порядок важен: сначала создаем и заполняем _temperatureMap, потом свет/токсин
        
        GenerateEnvironmentGradients();
        GenerateTemperatureMap();
        SpawnInitialNutrients();
        RebuildNutrientGrid();
    }

    private void GenerateEnvironmentGradients()
    {
        _lightMap = new float[Width, Height];
        _toxinMap = new float[Width, Height];

        // 1. Свет: ЭКСПОНЕНЦИАЛЬНОЕ затухание. Линейный профиль давал солнце на 70% глубины
        // (порог фотосинтеза 0.3 на прямой = глубина 0.7) — отсюда "рай фототрофов" и стоячие
        // наросты на 35-45%. Теперь фотическая зона — узкая крышка сверху, дальше мрак.
        // 0.18 от высоты = характерная глубина: свет=0.3 достигается на ~22% глубины (~195px при H=900).
        for (int y = 0; y < Height; y++)
        {
            float lightAtDepth = MathF.Exp(-(float)y / (Height * 0.18f));
            for (int x = 0; x < Width; x++)
            {
                // Мультипликативный шум: на глубине не создаёт "пятен света", куда бы полезли фототрофы.
                float noise = (float)(_rng.NextDouble() - 0.5) * 0.08f;
                _lightMap[x, y] = Math.Clamp(lightAtDepth * (1f + noise), 0f, 1f);
            }
        }

        // 2. Венты расставляются ПО СЛОТАМ (равномерно по горизонтали)
        int ventCount = 12;
        float slotW = (float)Width / ventCount;
        for (int i = 0; i < ventCount; i++)
        {
            float vx = Math.Clamp(slotW * (i + 0.5f) + (float)(_rng.NextDouble() - 0.5) * slotW * 0.6f, 0, Width - 1);
            float vy = Height - 6 - _rng.Next(10);
            float plumeH = 60f + _rng.Next(50); // Высота станет от 60 до 110 пикселей
            float coreBase = 5f + _rng.Next(6);
            float heatBase = 16f + _rng.Next(12);
            float slope = 0.16f + (float)_rng.NextDouble() * 0.14f;
            _ventSites.Add((vx, vy, plumeH, coreBase, heatBase, slope));

            // ТОКСИН: узкий конус ядра вверх (чёрная субстанция)
            int yTop = Math.Max(0, (int)(vy - plumeH));
            int yBot = Math.Min(Height - 1, (int)vy);
            float maxHalf = coreBase + slope * plumeH;
            int xL = Math.Max(0, (int)(vx - maxHalf));
            int xR = Math.Min(Width - 1, (int)(vx + maxHalf));

            for (int y = yTop; y <= yBot; y++)
            {
                float h = vy - y;
                float fade = 1f - h / plumeH;
                float half = coreBase + slope * h;
                for (int x = xL; x <= xR; x++)
                {
                    float dx = MathF.Abs(x - vx);
                    if (dx > half) continue;
                    float across = 1f - (dx / half) * 0.7f;
                    float val = fade * across;
                    if (val > _toxinMap[x, y]) _toxinMap[x, y] = val;
                }
            }
        }
    }
    public float BaseOceanTemperature { get; set; } = 0.35f;

    private void GenerateTemperatureMap()
    {
        _temperatureMap = new float[Width, Height];

        // 1. Инициализация базового фона океана (Поверхность 0.35 -> Глубина 0.04)
        for (int y = 0; y < Height; y++)
        {
            float depthFactor = (float)y / Height;
            float baseTemp = BaseOceanTemperature - (depthFactor * (BaseOceanTemperature * 0.8f));

            for (int x = 0; x < Width; x++)
            {
                float noise = (float)(_rng.NextDouble() - 0.5) * 0.03f;
                _temperatureMap[x, y] = Math.Clamp(baseTemp + noise, 0.01f, 1.0f);
            }
        }

        // 2. ВЕНТЫ: СБАЛАНСИРОВАННЫЙ ЖАР С ИЗГИБОМ СТРУИ СТРОГО ЗА ТРУБОЙ
        for (int i = 0; i < _ventSites.Count; i++)
        {
            var (vx, vy, plumeH, coreBase, heatBase, slope) = _ventSites[i];

            // Задаем параметры трубы из Program.cs для точного совпадения изгиба
            int chimneyH = 22 + (int)(coreBase * 1.8f);

            // Узкая геометрия опасного стержня
            float narrowCoreBase = coreBase * 0.35f;
            float narrowHeatBase = heatBase * 0.42f;
            float narrowSlope = slope * 0.25f;

            // Вытянутый по вертикали полукруг тепла (Капля / Зонтик)
            float comfortRadiusX = 115f;
            float comfortRadiusY = plumeH * 1.15f;

            int xL = Math.Max(0, (int)(vx - comfortRadiusX - 40));
            int xR = Math.Min(Width - 1, (int)(vx + comfortRadiusX + 40));
            int yT = Math.Max(0, (int)(vy - comfortRadiusY - 20));
            int yB = Math.Min(Height - 1, (int)(vy + 20));

            for (int y = yT; y <= yB; y++)
            {
                float h = vy - y; // высота вверх от жерла

                // Вычисляем изгиб струи. До высоты chimneyH он в точности повторяет изгиб трубы,
                // а выше плавно продолжает уходить в сторону по инерции потока воды
                float tForBend = h <= chimneyH ? h : chimneyH + (h - chimneyH) * 0.2f;
                float bend = MathF.Sin(tForBend * 0.15f + i) * 3.5f;

                // Смещаем центральную ось струи для текущей высоты y
                float currentVx = vx + bend;

                for (int x = xL; x <= xR; x++)
                {
                    float dx = x - currentVx; // считаем расстояние от уже смещенной оси струи

                    // --- А. ВЫТЯНУТЫЙ ПО ВЕРТИКАЛИ ПОЛУКРУГ ТЕПЛА (Цвет Vert heat / Желто-оранжевый) ---
                    float normX = (x - vx) / comfortRadiusX; // купол расширяется от основания, его сильно гнуть не нужно
                    float normY = h / comfortRadiusY;
                    float ellipseDist = MathF.Sqrt(normX * normX + normY * normY);

                    if (ellipseDist <= 1.0f && y <= vy + 10)
                    {
                        float t_comfort = 0.32f + 0.18f * (1f - ellipseDist * ellipseDist);

                        if (t_comfort > _temperatureMap[x, y])
                            _temperatureMap[x, y] = t_comfort;
                    }

                    // --- Б. ГОРЯЧИЙ СТРУЙНЫЙ СТОЛБ С ИЗГИБОМ (Внутри дымохода и над ним) ---
                    if (y <= vy && h <= plumeH)
                    {
                        float r_mortal_core = narrowCoreBase + narrowSlope * h;
                        float r_acceptable_cone = narrowHeatBase + (narrowSlope * 1.1f) * h;
                        float adx = MathF.Abs(dx);

                        if (adx <= r_mortal_core)
                        {
                            // ЯРКО-КРАСНЫЙ узкий центр (0.91 - 1.00) — теперь он плавно изгибается
                            float verticalFade = 1.00f - (h / plumeH) * 0.05f;
                            if (verticalFade > _temperatureMap[x, y])
                                _temperatureMap[x, y] = verticalFade;
                        }
                    }
                }
            }
        }
    }




    private void SpawnInitialNutrients()
    {
        int initialImpacts = 48 + _rng.Next(48);

        for (int i = 0; i < initialImpacts; i++)
        {
            float slot = (i + 0.5f) / initialImpacts;
            float jitter = (float)(_rng.NextDouble() - 0.5) * Width * 0.08f;
            float x = Math.Clamp(Width * slot + jitter, 0, Width - 1);
            float depth = 0.12f + (float)_rng.NextDouble() * 0.64f;
            float y = Math.Clamp(Height * depth, 0, Height - 1);

            SpawnImpactNutrients(x, y);
        }
    }

    public void RebuildNutrientGrid()
    {
        _nutrientGrid.Clear();
        foreach (var n in _nutrients)
        {
            if (n.IsDepleted) continue;
            var key = ((int)(n.X / CellSize), (int)(n.Y / CellSize));
            if (!_nutrientGrid.ContainsKey(key)) _nutrientGrid[key] = new List<Nutrient>();
            _nutrientGrid[key].Add(n);
        }
    }
    // Периодическая чистка балласта: исчерпанные нутриенты раньше помечались, но никогда не
    // удалялись из списка. Список рос навсегда (каждая смерть и каждый взрыв доливали записи),
    // и RebuildNutrientGrid + FindNearestNutrient с каждым прогонoм лопатили всё больше пустоты.
    public int PurgeDepletedNutrients()
    {
        int before = _nutrients.Count;
        _nutrients.RemoveAll(n => n.IsDepleted);
        int removed = before - _nutrients.Count;
        if (removed > 0)
        {
            RebuildNutrientGrid();
            _nutrientGridDirty = false;
        }
        return removed;
    }
    public float GetLightAt(float x, float y)
    {
        int ix = Math.Clamp((int)x, 0, Width - 1);
        int iy = Math.Clamp((int)y, 0, Height - 1);
        return _lightMap[ix, iy];
    }

    public float GetToxinAt(float x, float y)
    {
        int ix = Math.Clamp((int)x, 0, Width - 1);
        int iy = Math.Clamp((int)y, 0, Height - 1);
        return _toxinMap[ix, iy];
    }

    public float GetTemperatureAt(float x, float y)
    {
        int ix = Math.Clamp((int)x, 0, Width - 1);
        int iy = Math.Clamp((int)y, 0, Height - 1);

        float depth = iy / (float)Height;
        float shift;
        if (_climateCurrent >= 0f)
            shift = _climateCurrent * LavaAmplitude * MathF.Max(0f, 1f - depth * LavaFalloff);
        else
            shift = _climateCurrent * IceAmplitude * MathF.Max(0f, 1f - depth * IceFalloff);

        return Math.Clamp(_temperatureMap[ix, iy] + shift, 0f, 1f);
    }

    public Nutrient? FindNearestNutrient(float x, float y, float maxRadius, NutrientType? preferredType = null)
    {
        Nutrient? best = null;
        float minDist = maxRadius * maxRadius;
        int searchRadius = (int)(maxRadius / CellSize) + 1;

        int cx = (int)(x / CellSize);
        int cy = (int)(y / CellSize);

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                var key = (cx + dx, cy + dy);
                if (_nutrientGrid.TryGetValue(key, out var nutrientList))
                {
                    foreach (var n in nutrientList)
                    {
                        if (n.IsDepleted) continue;
                        if (preferredType.HasValue && n.Type != preferredType.Value) continue;

                        float distSq = (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y);
                        if (distSq < minDist)
                        {
                            minDist = distSq;
                            best = n;
                        }
                    }
                }
            }
        }
        return best;
    }

    public void AddNutrient(float x, float y, NutrientType type, float amount)
    {
        _nutrients.Add(new Nutrient
        {
            X = Math.Clamp(x, 0, Width - 1),
            Y = Math.Clamp(y, 0, Height - 1),
            Amount = amount,
            Type = type
        });
        _nutrientGridDirty = true;
    }

    public void FlushNutrientGridIfDirty()
    {
        if (!_nutrientGridDirty) return;
        RebuildNutrientGrid();
        _nutrientGridDirty = false;
    }

    public void SpawnImpactNutrients(float x, float y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        float depth = y / Height;
        float organicChance = depth < 0.35f ? 0.72f : (depth < 0.65f ? 0.50f : 0.28f);

        int count = 55 + _rng.Next(100);
        float radius = 75f + (float)_rng.NextDouble() * 40f;

        for (int i = 0; i < count; i++)
        {
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float r = (float)_rng.NextDouble() * radius;
            float nx = Math.Clamp(x + MathF.Cos(angle) * r, 0, Width - 1);
            float ny = Math.Clamp(y + MathF.Sin(angle) * r, 0, Height - 1);

            NutrientType type = _rng.NextDouble() < organicChance ? NutrientType.OrganicSludge : NutrientType.MineralSalt;
            float amount = 45f + (float)_rng.NextDouble() * 65f;

            _nutrients.Add(new Nutrient { X = nx, Y = ny, Amount = amount, Type = type });
        }
        _nutrientGridDirty = true;
    }

    // Климатические рычаги
    private float _climateCurrent = 0f;
    private float _climateTarget = 0f;
    // Усилили амплитуду: базовая темп. верха 0.35 + 0.65 = 1.0 (максимальный накал)
    private const float LavaAmplitude = 0.65f;
    private const float LavaFalloff = 1.6f;
    private const float IceAmplitude = 0.55f;  // Усилили холод
    private const float IceFalloff = 2.2f;

    public void UpdateClimate()
    {
        // Ускорили визуальный переход в ~4 раза. Теперь экран раскаляется или остывает за ~200-300 тиков (3-5 секунд)
        _climateCurrent += (_climateTarget - _climateCurrent) * 0.015f;
    }
    // Фон надо перерисовывать, только пока климат реально движется к цели.
    // Как только дельта схлопнулась — экран статичен, обновления прекращаем.
    public bool IsClimateMoving() => MathF.Abs(_climateTarget - _climateCurrent) > 0.001f;
    public void SetClimateTarget(float t) => _climateTarget = Math.Clamp(t, -1f, 1f);


    public List<Nutrient> GetNutrients() => _nutrients;

    public void PaintChemicalSpot(float x, float y)
    {
        int cx = (int)x;
        int cy = (int)y;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = cx + dx;
                int py = cy + dy;
                if (px >= 0 && px < Width && py >= 0 && py < Height)
                    _chemicalPixels.Add((px, py));
            }
        }
    }

    public List<(int X, int Y)> GetChemicalPixels() => _chemicalPixels;
    public void ClearChemicalPixels() => _chemicalPixels.Clear();
}