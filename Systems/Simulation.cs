using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AbiogenesisEngine.Core;

namespace AbiogenesisEngine.Systems;

public class Simulation
{
    private AbiogenesisMap _map;
    private List<Cell> _cells = new();
    private CellSpatialGrid _spatialGrid;
    private Random _rng;
    private int _tick = 0;
    private int _stableSpeciesCount = 0;
    private int _stableBeastCount = 0;
    private int _initialCellCount;
    private int _maxTicks;
    private int _targetSpecies;
    private int _realSeed;
    private DeathStatistics _deathStats = new DeathStatistics();
    private List<SnapshotSpeciesData> _savedSpecies = new();
    // НАШЕ ДОБАВЛЕНИЕ: Хранилище для тупиковых, но жестких суперхищников и мегафауны
    private List<SnapshotSpeciesData> _beastSpecies = new();
    // ТРЕКЕР ТКАНЕЙ: Запоминает, сколько тиков подряд суперорганизм удерживает форму из 6+ органов
    private Dictionary<uint, int> _macroStructureAgeTracker = new(); // Ключ теперь OrganismId
    private List<Meteoroid> _meteoroids = new();
    private Dictionary<uint, float> _colonyDriftAngle = new();
    private Dictionary<uint, float> _organismDriftAngle = new();     // ДОБАВЛЕНО
    private int _nextMeteoroidTick;
    private int _nextClimateShiftTick;

    private uint _nextCellId = 1;
    private uint _nextColonyId = 1;
    private uint _nextOrganId = 1;       // ДОБАВЛЕНО
    private uint _nextOrganismId = 1;    // ДОБАВЛЕНО

    // Словари для размеров и центров (заполняются каждый тик в UpdateCellsPhysicsAndBiology)
    private Dictionary<uint, int> _colonySizes = new();
    private Dictionary<uint, int> _organSizes = new();
    private Dictionary<uint, int> _organismSizes = new();

    private Dictionary<uint, (float X, float Y)> _colonyCenters = new();
    private Dictionary<uint, (float X, float Y)> _organCenters = new();
    private Dictionary<uint, (float X, float Y)> _organismCenters = new();

    public Simulation(int seed, int initialCellCount = 300, int maxTicks = 20000, int targetSpecies = 15,
                  int mapWidth = 2000, int mapHeight = 2000)
    {
        SoupLogger.Initialize();
        _realSeed = seed;
        _rng = new Random(seed);
        _map = new AbiogenesisMap(seed, mapWidth, mapHeight);
        _spatialGrid = new CellSpatialGrid(50);
        _initialCellCount = initialCellCount;
        _maxTicks = maxTicks;
        _targetSpecies = targetSpecies;

        // === ЗОНОВЫЙ СПАВН: Разделяем карту на 6 независимых стартовых зон ===
        // Это предотвращает мгновенное слияние всех клеток в одну бессмертную кучу в удачной точке.
        int zonesCount = 6;
        int cellsPerZone = initialCellCount / zonesCount;
        int remainder = initialCellCount % zonesCount;

        for (int z = 0; z < zonesCount; z++)
        {
            // Рассчитываем центр зоны (сетка 3x2, с небольшим случайным смещением, чтобы не было идеально по линейке)
            int col = z % 3;
            int row = z / 3;

            float zoneWidth = mapWidth / 3f;
            float zoneHeight = mapHeight / 2f;

            // Центр зоны + случайное смещение внутри зоны (чтобы не спавнились всегда в геометрическом центре)
            float centerX = (col * zoneWidth) + (zoneWidth / 2f) + (float)(_rng.NextDouble() - 0.5) * (zoneWidth * 0.5f);
            float centerY = (row * zoneHeight) + (zoneHeight / 2f) + (float)(_rng.NextDouble() - 0.5) * (zoneHeight * 0.5f);

            // Спавним клетки в радиусе 120px от центра зоны (плотная "лужа", но не точка)
            int cellsToSpawn = cellsPerZone + (z < remainder ? 1 : 0);

            for (int i = 0; i < cellsToSpawn; i++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float radius = (float)_rng.NextDouble() * 120f; // Радиус стартовой "лужи"

                float x = Math.Clamp(centerX + MathF.Cos(angle) * radius, 10f, mapWidth - 10f);
                float y = Math.Clamp(centerY + MathF.Sin(angle) * radius, 10f, mapHeight - 10f);

                   // === ЕСТЕСТВЕННЫЙ СТАРТ: Все клетки изначально всеядны (кроме солнца) ===
                // Мы даем им средние равные гены на химию, органику и охоту.
                // Никаких каст — в процессе мутаций они сами нащупают специализацию или останутся всеядными.
                float baseMerge = (float)(0.6 + _rng.NextDouble() * 0.4);
                float baseComm  = (float)(0.4 + _rng.NextDouble() * 0.4);

                // Задаем средние стартовые пищевые ферменты
                float startChem = (float)(0.25 + _rng.NextDouble() * 0.15); // Минералы
                float startOrg  = (float)(0.35 + _rng.NextDouble() * 0.15); // Трава
                float startPred = (float)(0.45 + _rng.NextDouble() * 0.15); // Мясо

                var startGenome = new AbiogenesisGenome(
                    mutationRate: 0.12f,
                    mergeAffinity: baseMerge,
                    communicationRadius: baseComm,
                    lightDependency: 0.15f, // Солнце заглушено — фотосинтез нужно заслужить мутациями!
                    chemicalAffinity: startChem,
                    organicAffinity: startOrg,
                    predatoryDrive: startPred
                );

                var cell = new Cell(x, y, startGenome);
                cell.BirthTick = 0;
                cell.Id = _nextCellId++;
                _cells.Add(cell);
            }
        }

        _nextMeteoroidTick = 180 + _rng.Next(320);
        _nextClimateShiftTick = 1500 + _rng.Next(1500);
    }
    public void Run()
    {
        Console.WriteLine("🧪 Abiogenesis Matrix — Запуск эмерджентной цепочки эволюции...");
        while (_tick < _maxTicks && _stableSpeciesCount < _targetSpecies)
        {
            Step();
            if (_tick % 500 == 0)
            {
                LogSystemHierarchy();
            }
        }
        
    }

    private void LogSystemHierarchy()
    {
        int loneCells = 0;

        var buckets = new Dictionary<uint, List<Cell>>();
        var cloudIds = new List<uint>();
        var seenCloudIds = new HashSet<uint>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];

            if (!cell.ColonyId.HasValue)
            {
                loneCells++;
                continue;
            }

            uint cid = cell.ColonyId.Value;

            if (!buckets.TryGetValue(cid, out var bucket))
            {
                bucket = new List<Cell>();
                buckets[cid] = bucket;
            }

            bucket.Add(cell);

            if (seenCloudIds.Add(cid))
            {
                cloudIds.Add(cid);
            }
        }

        cloudIds.Sort();

        int totalOrgans = 0;
        int totalOrganisms = 0;

        for (int cloudIndex = 0; cloudIndex < cloudIds.Count; cloudIndex++)
        {
            uint cid = cloudIds[cloudIndex];
            var cloud = buckets[cid];

            cloud.Sort((a, b) => a.Id.CompareTo(b.Id));

            var checkedCells = new HashSet<uint>();

            for (int i = 0; i < cloud.Count; i++)
            {
                var cell = cloud[i];

                if (checkedCells.Contains(cell.Id)) continue;

                int localCoreCount = 0;
                var localCoreCells = new List<Cell>();

                for (int j = 0; j < cloud.Count; j++)
                {
                    var other = cloud[j];

                    float dx = other.X - cell.X;
                    float dy = other.Y - cell.Y;

                    if ((dx * dx + dy * dy) < 400f)
                    {
                        localCoreCount++;
                        localCoreCells.Add(other);
                    }
                }

                if (localCoreCount >= 30)
                {
                    totalOrganisms++;
                }
                else if (localCoreCount >= 15)
                {
                    totalOrgans++;
                }

                if (localCoreCount >= 15)
                {
                    for (int j = 0; j < localCoreCells.Count; j++)
                    {
                        checkedCells.Add(localCoreCells[j].Id);
                    }
                }
            }
        }

        Console.WriteLine($"[Tick {_tick,5}] Одиночек: {loneCells,4} | Органов (15+ кл): {totalOrgans,3} | " +
                          $"Организмов (30+ кл): {totalOrganisms,3} | Цепей в воде: {cloudIds.Count,2} | Вышло видов: {_stableSpeciesCount}/{_targetSpecies}");

        SoupLogger.Log($"[Tick {_tick,5}] Одиночек: {loneCells,4} | Органов (15+ кл): {totalOrgans,3} | " +
                       $"Организмов (30+ кл): {totalOrganisms,3} | Цепей в воде: {cloudIds.Count,2} | Вышло видов: {_stableSpeciesCount}/{_targetSpecies}");
    }
    private void Step()
    {
        _tick++;
        UpdateMeteoroids();
        _map.FlushNutrientGridIfDirty();
        if (_tick % 1000 == 0)
        {
            int removed = _map.PurgeDepletedNutrients();
            if (removed > 0)
                SoupLogger.Log($"[🧹 NUTRIENT PURGE] Тик {_tick}: удалено пустых нутриентов: {removed}, осталось живых: {_map.GetNutrients().Count}");
        }
        UpdateClimateSchedule();
        _map.UpdateClimate();

        _spatialGrid.Clear();
        foreach (var cell in _cells) _spatialGrid.Insert(cell);

        UpdateCellsPhysicsAndBiology();
        ProcessEmergentClustering();

        // ИСПРАВЛЕНО: вызываем стресс и разрыв после кластеризации, но до выхода на сушу
        ApplyOrganismStressAndRupture();

        ProcessMacroOrganismLandExit();
    }

    private int _climateStage = 0; // 0 норма, 1 лава, 2 норма, 3 лёд
    private void UpdateClimateSchedule()
    {
        if (_tick < _nextClimateShiftTick) return;

        _climateStage = (_climateStage + 1) % 4;
        float target = _climateStage switch
        {
            1 => 1.0f,  // лавовый период
            3 => -1.0f,  // ледниковый период
            _ => 0.0f,  // норма между ними
        };
        _map.SetClimateTarget(target);

        // Норма длится дольше, экстремальные фазы — короче, чтобы система успевала отреагировать.
        int duration = (target == 0f) ? 1200 + _rng.Next(800) : 900 + _rng.Next(700);
        _nextClimateShiftTick = _tick + duration;

        string name = target > 0f ? "Лавовый период (поверхность раскаляется)" :
                      target < 0f ? "Ледниковый период (поверхность остывает)" :
                                    "Норма";
        SoupLogger.Log($"[🌡️ CLIMATE SHIFT] Тик {_tick}: {name}. Цель климата: {target:+0;-0;0}");
    }

    private void UpdateMeteoroids()
    {
        if (_tick >= _nextMeteoroidTick)
        {
            SpawnMeteoroid();
            _nextMeteoroidTick = _tick + 120 + _rng.Next(320);
        }

        for (int i = _meteoroids.Count - 1; i >= 0; i--)
        {
            var m = _meteoroids[i];

            if (m.Exploded)
            {
                m.ImpactFlash--;

                if (m.ImpactFlash <= 0)
                {
                    _meteoroids.RemoveAt(i);
                }

                continue;
            }

            m.X += m.VX;
            m.Y += m.VY;

            if (m.X < -80f || m.X > _map.Width + 80f || m.Y > _map.Height + 80f)
            {
                _meteoroids.RemoveAt(i);
                continue;
            }

            if (m.Y >= m.TargetY || m.Y >= _map.Height - 8f)
            {
                m.X = Math.Clamp(m.X, 0, _map.Width - 1);
                m.Y = Math.Clamp(m.Y, 0, _map.Height - 1);

                m.Exploded = true;
                m.ImpactFlash = (int)(10 + m.ImpactForce * 12f); // 13/17/22 кадра вспышки

                // 1. ЕДА: количество зависит от силы удара (1/2/3 вызова спавнера)
                int foodBursts = m.ImpactForce < 0.4f ? 1 : (m.ImpactForce < 0.7f ? 2 : 3);
                for (int k = 0; k < foodBursts; k++)
                    _map.SpawnImpactNutrients(m.X, m.Y);

                // 2. ПАНСПЕРМИЯ: метеорит привозит с собой клетки
                // Чем больше метеор, тем больше клеток. S=2-4, M=4-8, L=8-15
                int panspermiaCount = 0;
                if (m.ImpactForce < 0.3f)
                    panspermiaCount = 24 + _rng.Next(12);        // S: 6-10
                else if (m.ImpactForce < 0.7f)
                    panspermiaCount = 48 + _rng.Next(24);        // M: 12-20
                else
                    panspermiaCount = 96 + _rng.Next(48);        // L: 18-30

                // Спавним только если не превышаем лимит популяции (6000)
                int canSpawn = Math.Min(panspermiaCount, Math.Max(0, 5500 - _cells.Count));

                if (canSpawn > 0)
                { 
                    float spawnRadius = m.WaveRadius * 0.3f; // Клетки появляются в зоне взрыва

                    for (int p = 0; p < canSpawn; p++)
                    {
                        float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                        float radius = (float)_rng.NextDouble() * spawnRadius;
                        float px = Math.Clamp(m.X + MathF.Cos(angle) * radius, 10f, _map.Width - 10f);
                        float py = Math.Clamp(m.Y + MathF.Sin(angle) * radius, 10f, _map.Height - 10f);

                        // Базовый геном с небольшими мутациями (панспермия приносит разнообразие)
                        float baseMerge = (float)(0.5 + _rng.NextDouble() * 0.3);
                        float baseComm = (float)(0.4 + _rng.NextDouble() * 0.3);
                        float startChem = (float)(0.2 + _rng.NextDouble() * 0.2);
                        float startOrg = (float)(0.3 + _rng.NextDouble() * 0.2);
                        float startPred = (float)(0.3 + _rng.NextDouble() * 0.3);

                        var genome = new AbiogenesisGenome(
                            mutationRate: 0.1f + (float)_rng.NextDouble() * 0.05f,
                            mergeAffinity: baseMerge,
                            communicationRadius: baseComm,
                            lightDependency: 0.1f + (float)_rng.NextDouble() * 0.1f,
                            chemicalAffinity: startChem,
                            organicAffinity: startOrg,
                            predatoryDrive: startPred
                        );

                        var cell = new Cell(px, py, genome, 40f);
                        cell.BirthTick = _tick;
                        cell.Id = _nextCellId++;
                        _cells.Add(cell);
                    }

                    SoupLogger.Log($"[☄️ PANSPERMIA] Метеорит {m.SizeClass} (сила {m.ImpactForce:F2}) принёс {canSpawn} клеток жизни!");
                }

                // 2. УДАР ПО КЛЕТКАМ через spatial grid (быстро, O(1) по ячейкам)
                // 2. УДАР ПО КЛЕТКАМ через spatial grid (быстро, O(1) по ячейкам)
                var nearby = _spatialGrid.GetNearbyCells(m.X, m.Y, m.WaveRadius);
                int killed = 0, wounded = 0, pushed = 0;

                for (int j = 0; j < nearby.Count; j++)
                {
                    var cell = nearby[j];
                    float dx = cell.X - m.X;
                    float dy = cell.Y - m.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.1f) dist = 0.1f;

                    float resistance = cell.Genome.EnvironmentResistance;

                    if (dist <= m.LethalRadius)
                    {
                        // ЗОНА СМЕРТИ: шанс = сила * (1 - защита)^2
                        float deathChance = m.ImpactForce * (1f - resistance) * (1f - resistance);
                        if (_rng.NextDouble() < deathChance)
                        {
                            cell.Energy = 0f; // умрёт на следующем тике
                            killed++;
                            _deathStats.MeteoriteDeaths++;
                            _deathStats.TotalDeaths++;
                        }
                        else
                        {
                            // Выжил, но тяжело ранен
                            cell.Energy -= 30f * m.ImpactForce * (1f - resistance * 0.5f);
                            wounded++;
                        }
                    }
                }

                SoupLogger.Log(
                    $"[☄️ IMPACT {m.SizeClass}] Сила: {m.ImpactForce:F2} | " +
                    $"Летальная: {m.LethalRadius:F0}px |" +
                    $"В зоне: {nearby.Count} | Убито: {killed} "
                );
            }
        }
    }

    private void SpawnMeteoroid()
    {
        float x = _rng.Next(_map.Width);
        float roll = (float)_rng.NextDouble();
        float targetY;

        // Глубина падения
        if (roll < 0.35f)
        {
            float t = roll / 0.35f;
            targetY = _map.Height * (0.06f + 0.18f * t);
        }
        else if (roll < 0.70f)
        {
            float t = (roll - 0.35f) / 0.35f;
            targetY = _map.Height * (0.26f + 0.34f * t);
        }
        else
        {
            float t = (roll - 0.70f) / 0.30f;
            targetY = _map.Height * (0.62f + 0.33f * t);
        }

        // ЭМЕРДЖЕНТНАЯ ГЕНЕРАЦИЯ: случайная сила удара от 0.1f до 1.0f (например, 0.111, 0.31, 0.5)
        float impactForce = 0.1f + (float)_rng.NextDouble() * 0.9f;

        // Формула радиусов: волна ВСЕГДА в 4 раза шире летальной зоны, чтобы раскидывать клетки
        float lethalRadius = impactForce * 100f;  // При 0.5 -> 50px
        float waveRadius = impactForce * 400f;    // При 0.5 -> 200px

        float speed = 7f + (float)_rng.NextDouble() * 12f;
        float vx = (float)(_rng.NextDouble() - 0.5) * 2.5f;

        _meteoroids.Add(new Meteoroid
        {
            X = x,
            Y = -20f,
            VX = vx,
            VY = speed,
            TargetY = targetY,
            Exploded = false,
            ImpactFlash = 0,
            ImpactForce = impactForce,
            LethalRadius = lethalRadius,
            WaveRadius = waveRadius,
            SizeClass = impactForce < 0.3f ? "S" : (impactForce < 0.7f ? "M" : "L") // Для лога
        });
    }

    public List<Meteoroid> GetMeteoroids() => _meteoroids;

    private void UpdateCellsPhysicsAndBiology()
    {
        var bornCells = new List<Cell>();
        var deadCellsHashes = new HashSet<uint>();

        // 1. Собираем уникальные ID для всех трех уровней
        var uniqueColonyIds = new List<uint>();
        var seenColonies = new HashSet<uint>();
        var uniqueOrganIds = new List<uint>();
        var seenOrgans = new HashSet<uint>();
        var uniqueOrganismIds = new List<uint>();
        var seenOrganisms = new HashSet<uint>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue && seenColonies.Add(c.ColonyId.Value)) uniqueColonyIds.Add(c.ColonyId.Value);
            if (c.OrganId.HasValue && seenOrgans.Add(c.OrganId.Value)) uniqueOrganIds.Add(c.OrganId.Value);
            if (c.OrganismId.HasValue && seenOrganisms.Add(c.OrganismId.Value)) uniqueOrganismIds.Add(c.OrganismId.Value);
        }

        uniqueColonyIds.Sort();
        uniqueOrganIds.Sort();
        uniqueOrganismIds.Sort();

        // 2. Считаем размеры за один проход
        var colonySizes = new Dictionary<uint, int>();
        var organSizes = new Dictionary<uint, int>();
        var organismSizes = new Dictionary<uint, int>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue) colonySizes[c.ColonyId.Value] = colonySizes.GetValueOrDefault(c.ColonyId.Value, 0) + 1;
            if (c.OrganId.HasValue) organSizes[c.OrganId.Value] = organSizes.GetValueOrDefault(c.OrganId.Value, 0) + 1;
            if (c.OrganismId.HasValue) organismSizes[c.OrganismId.Value] = organismSizes.GetValueOrDefault(c.OrganismId.Value, 0) + 1;
        }

        // 3. Центры за ОДИН проход (правка B — было O(сущности × клетки) трижды, стало O(клетки) один раз)
        var colSumX = new Dictionary<uint, float>();
        var colSumY = new Dictionary<uint, float>();
        var orgSumX = new Dictionary<uint, float>();
        var orgSumY = new Dictionary<uint, float>();
        var omSumX = new Dictionary<uint, float>();
        var omSumY = new Dictionary<uint, float>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue)
            {
                uint k = c.ColonyId.Value;
                colSumX[k] = colSumX.GetValueOrDefault(k, 0f) + c.X;
                colSumY[k] = colSumY.GetValueOrDefault(k, 0f) + c.Y;
            }
            if (c.OrganId.HasValue)
            {
                uint k = c.OrganId.Value;
                orgSumX[k] = orgSumX.GetValueOrDefault(k, 0f) + c.X;
                orgSumY[k] = orgSumY.GetValueOrDefault(k, 0f) + c.Y;
            }
            if (c.OrganismId.HasValue)
            {
                uint k = c.OrganismId.Value;
                omSumX[k] = omSumX.GetValueOrDefault(k, 0f) + c.X;
                omSumY[k] = omSumY.GetValueOrDefault(k, 0f) + c.Y;
            }
        }

        var colonyCenters = new Dictionary<uint, (float X, float Y)>();
        var organCenters = new Dictionary<uint, (float X, float Y)>();
        var organismCenters = new Dictionary<uint, (float X, float Y)>();

        foreach (uint colId in uniqueColonyIds)
        {
            int cnt = colonySizes.GetValueOrDefault(colId, 0);
            if (cnt > 0 && colSumX.TryGetValue(colId, out float sx))
                colonyCenters[colId] = (sx / cnt, colSumY[colId] / cnt);
        }
        foreach (uint organId in uniqueOrganIds)
        {
            int cnt = organSizes.GetValueOrDefault(organId, 0);
            if (cnt > 0 && orgSumX.TryGetValue(organId, out float sx))
                organCenters[organId] = (sx / cnt, orgSumY[organId] / cnt);
        }
        foreach (uint orgId in uniqueOrganismIds)
        {
            int cnt = organismSizes.GetValueOrDefault(orgId, 0);
            if (cnt > 0 && omSumX.TryGetValue(orgId, out float sx))
                organismCenters[orgId] = (sx / cnt, omSumY[orgId] / cnt);
        }

        // 4. Расчет дрейфа (только для Организмов и свободных Колоний)
        const float FOOD_SENSE_RADIUS = 350f;
        const float TURN_RATE = 0.25f;
        const float FREE_WALK_STEP = 0.30f;
        const float JITTER = 0.04f;
        bool recalc = (_tick % 6 == 0);

        foreach (uint orgId in uniqueOrganismIds)
        {
            if (!organismCenters.TryGetValue(orgId, out var center)) continue;
            if (!_organismDriftAngle.TryGetValue(orgId, out float ang)) ang = (float)(_rng.NextDouble() * Math.PI * 2);

            if (recalc)
            {
                var food = _map.FindNearestNutrient(center.X, center.Y, FOOD_SENSE_RADIUS);
                if (food != null)
                {
                    float desired = MathF.Atan2(food.Y - center.Y, food.X - center.X);
                    float diff = desired - ang;
                    while (diff > MathF.PI) diff -= 2f * MathF.PI;
                    while (diff < -MathF.PI) diff += 2f * MathF.PI;
                    if (diff > TURN_RATE) diff = TURN_RATE;
                    if (diff < -TURN_RATE) diff = -TURN_RATE;
                    ang += diff;
                }
                else
                {
                    ang += (float)(_rng.NextDouble() - 0.5) * 2f * FREE_WALK_STEP;
                }
            }
            else
            {
                ang += (float)(_rng.NextDouble() - 0.5) * 2f * JITTER;
            }
            if (ang > MathF.PI) ang -= 2f * MathF.PI;
            else if (ang < -MathF.PI) ang += 2f * MathF.PI;
            _organismDriftAngle[orgId] = ang;
        }

        foreach (uint colId in uniqueColonyIds)
        {
            bool hasOrganism = false;
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].ColonyId == colId && _cells[i].OrganismId.HasValue)
                {
                    hasOrganism = true;
                    break;
                }
            }
            if (hasOrganism) continue;

            if (!colonyCenters.TryGetValue(colId, out var center)) continue;
            if (!_colonyDriftAngle.TryGetValue(colId, out float ang)) ang = (float)(_rng.NextDouble() * Math.PI * 2);

            if (recalc)
            {
                var food = _map.FindNearestNutrient(center.X, center.Y, FOOD_SENSE_RADIUS);
                if (food != null)
                {
                    float desired = MathF.Atan2(food.Y - center.Y, food.X - center.X);
                    float diff = desired - ang;
                    while (diff > MathF.PI) diff -= 2f * MathF.PI;
                    while (diff < -MathF.PI) diff += 2f * MathF.PI;
                    if (diff > TURN_RATE) diff = TURN_RATE;
                    if (diff < -TURN_RATE) diff = -TURN_RATE;
                    ang += diff;
                }
                else
                {
                    ang += (float)(_rng.NextDouble() - 0.5) * 2f * FREE_WALK_STEP;
                }
            }
            else
            {
                ang += (float)(_rng.NextDouble() - 0.5) * 2f * JITTER;
            }
            if (ang > MathF.PI) ang -= 2f * MathF.PI;
            else if (ang < -MathF.PI) ang += 2f * MathF.PI;
            _colonyDriftAngle[colId] = ang;
        }

        // 5. Применение центров и дрейфа к клеткам + обновление
        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];

            // Сброс флагов
            cell.HasColonyCenter = false;
            cell.HasOrganCenter = false;
            cell.HasOrganismCenter = false;
            cell.DriftX = 0f;
            cell.DriftY = 0f;

            // Приоритет 1: Организм (задаёт глобальный дрейф)
            if (cell.OrganismId.HasValue && organismCenters.TryGetValue(cell.OrganismId.Value, out var orgCenter))
            {
                cell.OrganismCenterX = orgCenter.X;
                cell.OrganismCenterY = orgCenter.Y;
                cell.HasOrganismCenter = true;
                if (_organismDriftAngle.TryGetValue(cell.OrganismId.Value, out float orgAng))
                {
                    float dmag = cell.Genome.Speed * 1.1f;
                    cell.DriftX = MathF.Cos(orgAng) * dmag;
                    cell.DriftY = MathF.Sin(orgAng) * dmag;
                }
            }
            // Приоритет 2: Орган (держит форму)
            if (cell.OrganId.HasValue && organCenters.TryGetValue(cell.OrganId.Value, out var organCenter))
            {
                cell.OrganCenterX = organCenter.X;
                cell.OrganCenterY = organCenter.Y;
                cell.HasOrganCenter = true;
            }
            // Приоритет 3: Колония (держит форму, если нет органа)
            if (!cell.HasOrganCenter && cell.ColonyId.HasValue && colonyCenters.TryGetValue(cell.ColonyId.Value, out var colCenter))
            {
                cell.ColonyCenterX = colCenter.X;
                cell.ColonyCenterY = colCenter.Y;
                cell.HasColonyCenter = true;
                if (_colonyDriftAngle.TryGetValue(cell.ColonyId.Value, out float colAng))
                {
                    float dmag = cell.Genome.Speed * 1.1f;
                    cell.DriftX = MathF.Cos(colAng) * dmag;
                    cell.DriftY = MathF.Sin(colAng) * dmag;
                }
            }

            cell.Update(_map, _rng, _spatialGrid);

            if (_rng.NextDouble() < cell.Genome.MutationRate * 0.02f)
            {
                cell.Genome = cell.Genome.Mutate(_rng);
            }
        } // <-- ЗАКРЫВАЕМ ЦИКЛ ОБНОВЛЕНИЯ КЛЕТОК

        // 6. Глобальный осмос (вызываем после того, как все клетки поели и потратили энергию)
        ApplyGlobalOsmosis();

        // 7. Удаление мёртвых (теперь голодные клетки могли быть спасены осмосом!)
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            var cell = _cells[i];
            if (cell.Energy <= 0f)
            {
                _deathStats.StarvationDeaths++;
                _deathStats.TotalDeaths++;
                deadCellsHashes.Add(cell.Id);
            }
            else if (cell.Integrity <= 0f)
            {
                _deathStats.TraumaDeaths++;
                _deathStats.TotalDeaths++;
                deadCellsHashes.Add(cell.Id);
            }
        }

        // 8. Деление (после осмоса, чтобы клетки с восстановленной энергией могли делиться)
        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (deadCellsHashes.Contains(cell.Id)) continue;

            if (cell.Energy >= cell.Genome.DivisionThreshold * 100f && !cell.IsDying)
            {
                // Проверка лимита деления для органов (60 клеток)
                if (cell.OrganId.HasValue && organSizes.GetValueOrDefault(cell.OrganId.Value, 0) >= 60)
                {
                    continue;
                }

                cell.Energy *= 0.5f;

                // Правка A: ребёнок рождается НЕ в точке родителя (4..9px разброс),
                // чтобы не слипаться в одну ячейку spatialGrid
                float spawnAngle = (float)(_rng.NextDouble() * Math.PI * 2);
                float spawnDist = 4f + (float)_rng.NextDouble() * 5f;
                float spawnOffsetX = MathF.Cos(spawnAngle) * spawnDist;
                float spawnOffsetY = MathF.Sin(spawnAngle) * spawnDist;

                var child = new Cell(cell.X + spawnOffsetX, cell.Y + spawnOffsetY, cell.Genome.Mutate(_rng), cell.Energy);

                child.ColonyId = cell.ColonyId;
                child.OrganId = cell.OrganId;
                child.OrganismId = cell.OrganismId;
                child.HeadingX = cell.HeadingX;
                child.HeadingY = cell.HeadingY;
                child.DriftX = cell.DriftX;
                child.DriftY = cell.DriftY;
                child.ColonyCenterX = cell.ColonyCenterX;
                child.ColonyCenterY = cell.ColonyCenterY;
                child.HasColonyCenter = cell.HasColonyCenter;
                child.OrganCenterX = cell.OrganCenterX;
                child.OrganCenterY = cell.OrganCenterY;
                child.HasOrganCenter = cell.HasOrganCenter;
                child.OrganismCenterX = cell.OrganismCenterX;
                child.OrganismCenterY = cell.OrganismCenterY;
                child.HasOrganismCenter = cell.HasOrganismCenter;
                child.Age = 0;
                child.BirthTick = _tick;
                child.Id = _nextCellId++;
                bornCells.Add(child);
            }
        }

        // 9. Фактическое удаление мёртвых и спавн мяса
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            if (deadCellsHashes.Contains(_cells[i].Id))
            {
                var dead = _cells[i];
                _cells.RemoveAt(i);
                SpawnNutrientAt(dead.X, dead.Y, NutrientType.MeatScraps, 8f);
            }
        }

        _cells.AddRange(bornCells);

        // 10. Лимит популяции
        if (_cells.Count > 6000)
        {
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (_cells.Count <= 5000) break;
                if (!_cells[i].ColonyId.HasValue && !_cells[i].OrganId.HasValue)
                {
                    _cells.RemoveAt(i);
                    _deathStats.CullingDeaths++;
                    _deathStats.TotalDeaths++;
                }
            }
            if (_cells.Count > 5000)
            {
                int removed = _cells.Count - 5000;
                _cells.RemoveRange(5000, removed);
                _deathStats.CullingDeaths += removed;
                _deathStats.TotalDeaths += removed;
            }
        }
    }

    private void ApplyGlobalOsmosis()
    {
        // Группируем клетки по их высшей доступной сущности: OrganismId > OrganId > ColonyId
        var groups = new Dictionary<uint, List<Cell>>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            uint groupId = cell.OrganismId.HasValue ? cell.OrganismId.Value :
                           (cell.OrganId.HasValue ? cell.OrganId.Value :
                           (cell.ColonyId.HasValue ? cell.ColonyId.Value : 0));

            if (groupId == 0) continue; // Одиночки не участвуют в глобальном осмосе

            if (!groups.TryGetValue(groupId, out var list))
            {
                list = new List<Cell>();
                groups[groupId] = list;
            }
            list.Add(cell);
        }

        foreach (var kvp in groups)
        {
            var groupCells = kvp.Value;
            if (groupCells.Count < 2) continue;
            float totalExcess = 0f;
            float totalDeficit = 0f;

            // Шаг 1: считаем, сколько каждая клетка ГОТОВА отдать (по своему MergeAffinity)
            // и сколько ей не хватает до порога сытости (30f)
            for (int i = 0; i < groupCells.Count; i++)
            {
                var cell = groupCells[i];
                if (cell.Energy > 60f)
                {
                    // Клетка отдаёт только долю излишка, пропорциональную своей "социальности"
                    float rawExcess = cell.Energy - 60f;
                    float willingToGive = rawExcess * cell.Genome.MergeAffinity;
                    totalExcess += willingToGive;
                    cell.Energy -= willingToGive; // забираем у неё только то, что она готова отдать
                }
                else if (cell.Energy < 30f)
                {
                    float deficit = 30f - cell.Energy;
                    totalDeficit += deficit;
                }
            }

            // Шаг 2: распределяем собранный пул голодающим (с потерей 10% на транспорт)
            if (totalExcess > 0f && totalDeficit > 0f)
            {
                float transferEfficiency = 0.9f;
                float availablePool = totalExcess * transferEfficiency;
                for (int i = 0; i < groupCells.Count; i++)
                {
                    var cell = groupCells[i];
                    if (cell.Energy < 30f)
                    {
                        float deficit = 30f - cell.Energy;
                        float share = (deficit / totalDeficit) * availablePool;
                        cell.Energy += share;
                    }
                }
            }
        }
    }


    private void ProcessEmergentClustering()
    {
        if (_tick % 5 != 0 || _cells.Count == 0) return;

        // 1. Собираем уникальные ID и сортируем для детерминированности
        var uniqueColonyIds = new List<uint>();
        var seenColonies = new HashSet<uint>();
        var uniqueOrganIds = new List<uint>();
        var seenOrgans = new HashSet<uint>();
        var uniqueOrganismIds = new List<uint>();
        var seenOrganisms = new HashSet<uint>();

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue && seenColonies.Add(c.ColonyId.Value)) uniqueColonyIds.Add(c.ColonyId.Value);
            if (c.OrganId.HasValue && seenOrgans.Add(c.OrganId.Value)) uniqueOrganIds.Add(c.OrganId.Value);
            if (c.OrganismId.HasValue && seenOrganisms.Add(c.OrganismId.Value)) uniqueOrganismIds.Add(c.OrganismId.Value);
        }

        uniqueColonyIds.Sort();
        uniqueOrganIds.Sort();
        uniqueOrganismIds.Sort();

        // 2. Считаем размеры
        _colonySizes.Clear();
        _organSizes.Clear();
        _organismSizes.Clear();

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue) _colonySizes[c.ColonyId.Value] = _colonySizes.GetValueOrDefault(c.ColonyId.Value, 0) + 1;
            if (c.OrganId.HasValue) _organSizes[c.OrganId.Value] = _organSizes.GetValueOrDefault(c.OrganId.Value, 0) + 1;
            if (c.OrganismId.HasValue) _organismSizes[c.OrganismId.Value] = _organismSizes.GetValueOrDefault(c.OrganismId.Value, 0) + 1;
        }

        var aliveIds = new HashSet<uint>(_cells.Count);
        for (int i = 0; i < _cells.Count; i++) aliveIds.Add(_cells[i].Id);

        // C0. КОРЗИНЫ ССЫЛОК: ID -> список клеток этой сущности. Строим ОДИН проход.
        // Дальше все перекраски и подсчёты ходят по коротким спискам, а не по всему _cells.
        var colonyCells = new Dictionary<uint, List<Cell>>();
        var organCells = new Dictionary<uint, List<Cell>>();
        var organismCells = new Dictionary<uint, List<Cell>>();
        var organismOrganSet = new Dictionary<uint, HashSet<uint>>(); // organismId -> множество OrganId

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            if (c.ColonyId.HasValue)
            {
                if (!colonyCells.TryGetValue(c.ColonyId.Value, out var l)) { l = new List<Cell>(); colonyCells[c.ColonyId.Value] = l; }
                l.Add(c);
            }
            if (c.OrganId.HasValue)
            {
                if (!organCells.TryGetValue(c.OrganId.Value, out var l)) { l = new List<Cell>(); organCells[c.OrganId.Value] = l; }
                l.Add(c);
            }
            if (c.OrganismId.HasValue)
            {
                uint omk = c.OrganismId.Value;
                if (!organismCells.TryGetValue(omk, out var l)) { l = new List<Cell>(); organismCells[omk] = l; }
                l.Add(c);
                if (!organismOrganSet.TryGetValue(omk, out var os)) { os = new HashSet<uint>(); organismOrganSet[omk] = os; }
                if (c.OrganId.HasValue) os.Add(c.OrganId.Value);
            }
        }

        var processedThisTick = new HashSet<uint>();

        // 3. СЛИЯНИЕ ОДИНОЧЕК И КОЛОНИЙ
        int cellCount = _cells.Count;
        for (int i = 0; i < cellCount; i++)
        {
            if (i >= _cells.Count) break;
            var a = _cells[i];

            if (processedThisTick.Contains(a.Id)) continue;
            if (a.Energy < 20f) continue;

            var nearby = _spatialGrid.GetNearbyCells(a.X, a.Y, 50f);
            nearby.Sort((x, y) => x.Id.CompareTo(y.Id));

            for (int k = 0; k < nearby.Count; k++)
            {
                var b = nearby[k];

                if (b.Id == a.Id || b.Energy < 20f || !aliveIds.Contains(b.Id)) continue;
                if (processedThisTick.Contains(b.Id)) continue;

                float avgMerge = (a.Genome.MergeAffinity + b.Genome.MergeAffinity) / 2f;

                if (avgMerge < 0.42f) continue;

                // Случай 1: Две одиночки сливаются в новую колонию
                if (!a.ColonyId.HasValue && !b.ColonyId.HasValue && !a.OrganId.HasValue && !b.OrganId.HasValue)
                {
                    if (_rng.NextDouble() < avgMerge * 0.5f)
                    {
                        uint newColonyId = _nextColonyId++;
                        a.ColonyId = newColonyId;
                        b.ColonyId = newColonyId;
                        _colonySizes[newColonyId] = 2;

                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }

                // Случай 2: Одиночка присоединяется к колонии (<30 клеток)
                if (!a.ColonyId.HasValue && !a.OrganId.HasValue && b.ColonyId.HasValue && !b.OrganId.HasValue)
                {
                    uint colId = b.ColonyId.Value;
                    int colSize = _colonySizes.GetValueOrDefault(colId, 0);

                    if (colSize < 30 && _rng.NextDouble() < avgMerge * 0.4f)
                    {
                        a.ColonyId = colId;
                        _colonySizes[colId] = colSize + 1;
                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }

                // Случай 3: Колония присоединяется к одиночке (симметрично случаю 2)
                if (a.ColonyId.HasValue && !a.OrganId.HasValue && !b.ColonyId.HasValue && !b.OrganId.HasValue)
                {
                    uint colId = a.ColonyId.Value;
                    int colSize = _colonySizes.GetValueOrDefault(colId, 0);

                    if (colSize < 30 && _rng.NextDouble() < avgMerge * 0.4f)
                    {
                        b.ColonyId = colId;
                        _colonySizes[colId] = colSize + 1;
                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }

                // Случай 4: Две колонии сливаются (сумма <30)
                if (a.ColonyId.HasValue && !a.OrganId.HasValue && b.ColonyId.HasValue && !b.OrganId.HasValue)
                {
                    if (a.ColonyId.Value == b.ColonyId.Value) continue;

                    uint colA = a.ColonyId.Value;
                    uint colB = b.ColonyId.Value;
                    int sizeA = _colonySizes.GetValueOrDefault(colA, 0);
                    int sizeB = _colonySizes.GetValueOrDefault(colB, 0);

                    if (sizeA + sizeB < 30 && _rng.NextDouble() < avgMerge * 0.35f)
                    {
                        uint winner = Math.Min(colA, colB);
                        uint loser = Math.Max(colA, colB);

                        // C1. Перекраска только клеток loser (по корзине), затем сливаем корзины
                        var loserList = colonyCells.GetValueOrDefault(loser);
                        if (loserList != null)
                        {
                            for (int m = 0; m < loserList.Count; m++) loserList[m].ColonyId = winner;
                            if (!colonyCells.TryGetValue(winner, out var winList)) { winList = new List<Cell>(); colonyCells[winner] = winList; }
                            winList.AddRange(loserList);
                            colonyCells.Remove(loser);
                        }

                        _colonySizes[winner] = sizeA + sizeB;
                        _colonySizes.Remove(loser);
                        _colonyDriftAngle.Remove(loser);

                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }

                // Случай 5: Одиночка присоединяется к органу (<60 клеток, организм открыт)
                if (!a.ColonyId.HasValue && !a.OrganId.HasValue && b.OrganId.HasValue && b.OrganismId.HasValue)
                {
                    uint organId = b.OrganId.Value;
                    int organSize = _organSizes.GetValueOrDefault(organId, 0);
                    uint organismId = b.OrganismId.Value;

                    // C2. Подсчёт органов из корзины (без вложенного обхода _cells)
                    int organCount = organismOrganSet.TryGetValue(organismId, out var os5) ? os5.Count : 0;

                    if (organSize < 60 && organCount <= 3 && _rng.NextDouble() < avgMerge * 0.35f)
                    {
                        a.OrganId = organId;
                        a.OrganismId = organismId;
                        _organSizes[organId] = organSize + 1;
                        if (organCells.TryGetValue(organId, out var ol5)) ol5.Add(a);
                        if (organismCells.TryGetValue(organismId, out var ml5)) ml5.Add(a);
                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }

                // Случай 6: Орган присоединяется к одиночке (симметрично случаю 5)
                if (a.OrganId.HasValue && a.OrganismId.HasValue && !b.ColonyId.HasValue && !b.OrganId.HasValue)
                {
                    uint organId = a.OrganId.Value;
                    int organSize = _organSizes.GetValueOrDefault(organId, 0);
                    uint organismId = a.OrganismId.Value;

                    int organCount = organismOrganSet.TryGetValue(organismId, out var os6) ? os6.Count : 0;

                    if (organSize < 60 && organCount <= 3 && _rng.NextDouble() < avgMerge * 0.35f)
                    {
                        b.OrganId = organId;
                        b.OrganismId = organismId;
                        _organSizes[organId] = organSize + 1;
                        if (organCells.TryGetValue(organId, out var ol6)) ol6.Add(b);
                        if (organismCells.TryGetValue(organismId, out var ml6)) ml6.Add(b);
                        processedThisTick.Add(a.Id);
                        processedThisTick.Add(b.Id);
                        break;
                    }
                    continue;
                }
            }
        }

        // 4. СОЗРЕВАНИЕ: Колония >= 30 клеток становится Органом
        for (int i = 0; i < uniqueColonyIds.Count; i++)
        {
            uint colId = uniqueColonyIds[i];
            if (_colonySizes.GetValueOrDefault(colId, 0) >= 30)
            {
                uint newOrganId = _nextOrganId++;

                // C3. Перекраска по корзине
                var maturing = colonyCells.GetValueOrDefault(colId);
                if (maturing != null && maturing.Count > 0)
                {
                    uint targetOrg = 0;
                    for (int j = 0; j < maturing.Count; j++)
                    {
                        var mc = maturing[j];
                        mc.ColonyId = null;
                        mc.OrganId = newOrganId;
                        if (!mc.OrganismId.HasValue) mc.OrganismId = _nextOrganismId++;
                        targetOrg = mc.OrganismId.Value;
                    }
                    organCells[newOrganId] = maturing;
                    if (!organismCells.TryGetValue(targetOrg, out var tl)) { tl = new List<Cell>(); organismCells[targetOrg] = tl; }
                    tl.AddRange(maturing);
                    if (!organismOrganSet.TryGetValue(targetOrg, out var ts)) { ts = new HashSet<uint>(); organismOrganSet[targetOrg] = ts; }
                    ts.Add(newOrganId);
                    colonyCells.Remove(colId);
                }
                _colonySizes.Remove(colId);
                _colonyDriftAngle.Remove(colId);
            }
        }

        // 5. ПОЧКОВАНИЕ: Орган > 70 клеток вычленяет 10-15 клеток в новую колонию (почку)
        var organsToProcess = new List<uint>(uniqueOrganIds);
        organsToProcess.Sort();

        for (int i = 0; i < organsToProcess.Count; i++)
        {
            uint organId = organsToProcess[i];
            int size = _organSizes.GetValueOrDefault(organId, 0);

            if (size > 70)
            {
                // Собираем клетки этого конкретного органа в список
                var organCellList = new List<Cell>();
                for (int j = 0; j < _cells.Count; j++)
                {
                    if (_cells[j].OrganId == organId)
                    {
                        organCellList.Add(_cells[j]);
                    }
                }

                // Сортируем по ID для детерминированности
                organCellList.Sort((a, b) => a.Id.CompareTo(b.Id));

                int budSize = Math.Min(15, size - 60);
                if (budSize < 10) budSize = 10;

                // ЗАЩИТА: если по какой-то причине клеток в списке меньше, чем размер почки
                if (organCellList.Count < budSize)
                {
                    budSize = organCellList.Count;
                }

                uint newColonyId = _nextColonyId++;

                // Забираем последние 'budSize' клеток и делаем их почкой
                for (int k = organCellList.Count - 1; k >= organCellList.Count - budSize; k--)
                {
                    var cell = organCellList[k];
                    cell.OrganId = null;
                    cell.ColonyId = newColonyId;
                }

                // Обновляем счетчики размеров
                _organSizes[organId] -= budSize;
                _colonySizes[newColonyId] = budSize;
            }
        }

        // 6. ПОГЛОЩЕНИЕ ПОЧКИ: Колония (почка) касается своего Органа (< 60) -> сливается
        for (int i = 0; i < _cells.Count; i++)
        {
            var a = _cells[i];
            if (!a.ColonyId.HasValue || !a.OrganismId.HasValue) continue;

            var nearby = _spatialGrid.GetNearbyCells(a.X, a.Y, 15f);
            nearby.Sort((x, y) => x.Id.CompareTo(y.Id));

            for (int k = 0; k < nearby.Count; k++)
            {
                var b = nearby[k];
                if (b.Id == a.Id || !b.OrganId.HasValue || !b.OrganismId.HasValue) continue;

                if (b.OrganismId.Value == a.OrganismId.Value && _organSizes.GetValueOrDefault(b.OrganId.Value, 0) < 60)
                {
                    uint targetOrganId = b.OrganId.Value;
                    uint oldColonyId = a.ColonyId.Value;

                    // C4. Перекраска по корзине
                    var budList = colonyCells.GetValueOrDefault(oldColonyId);
                    if (budList != null)
                    {
                        for (int m = 0; m < budList.Count; m++)
                        {
                            budList[m].ColonyId = null;
                            budList[m].OrganId = targetOrganId;
                        }
                        if (organCells.TryGetValue(targetOrganId, out var tgtList)) tgtList.AddRange(budList);
                        else organCells[targetOrganId] = budList;
                        colonyCells.Remove(oldColonyId);
                    }

                    int absorbed = _colonySizes.GetValueOrDefault(oldColonyId, 0);
                    _organSizes[targetOrganId] = _organSizes.GetValueOrDefault(targetOrganId, 0) + absorbed;
                    _colonySizes.Remove(oldColonyId);
                    _colonyDriftAngle.Remove(oldColonyId);
                    break;
                }
            }
        }

        // 7. ФЕДЕРАЦИЯ: Слияние двух ОТКРЫТЫХ организмов (<= 3 органов каждый)
        // organismOrganCounts больше не нужен — количество органов организма = organismOrganSet[org].Count
        for (int i = 0; i < _cells.Count; i++)
        {
            var a = _cells[i];
            if (a.Energy < 20f || !a.OrganismId.HasValue) continue;

            var nearby = _spatialGrid.GetNearbyCells(a.X, a.Y, 20f);
            nearby.Sort((x, y) => x.Id.CompareTo(y.Id));

            for (int k = 0; k < nearby.Count; k++)
            {
                var b = nearby[k];
                if (b.Id == a.Id || b.Energy < 20f || !b.OrganismId.HasValue) continue;
                if (a.OrganismId.Value == b.OrganismId.Value) continue;

                uint orgA = a.OrganismId.Value;
                uint orgB = b.OrganismId.Value;

                // C5. Счётчик органов из корзины + проверка массы
                int countA = organismOrganSet.TryGetValue(orgA, out var sa) ? sa.Count : 0;
                int countB = organismOrganSet.TryGetValue(orgB, out var sb) ? sb.Count : 0;
                int sizeA = _organismSizes.GetValueOrDefault(orgA, 0);
                int sizeB = _organismSizes.GetValueOrDefault(orgB, 0);

                if (countA <= 3 && countB <= 3 && (sizeA + sizeB) <= 240)
                {
                    uint winner = Math.Min(orgA, orgB);
                    uint loser = Math.Max(orgA, orgB);

                    // Перекраска организма по корзине
                    var loserList = organismCells.GetValueOrDefault(loser);
                    if (loserList != null)
                    {
                        for (int m = 0; m < loserList.Count; m++) loserList[m].OrganismId = winner;
                        if (!organismCells.TryGetValue(winner, out var winList)) { winList = new List<Cell>(); organismCells[winner] = winList; }
                        winList.AddRange(loserList);
                        organismCells.Remove(loser);
                    }

                    // Объединяем множества органов
                    if (organismOrganSet.TryGetValue(loser, out var loseOrgs))
                    {
                        if (!organismOrganSet.TryGetValue(winner, out var winOrgs)) { winOrgs = new HashSet<uint>(); organismOrganSet[winner] = winOrgs; }
                        foreach (var oid in loseOrgs) winOrgs.Add(oid);
                        organismOrganSet.Remove(loser);
                    }

                    _organismSizes[winner] = _organismSizes.GetValueOrDefault(winner, 0) + _organismSizes.GetValueOrDefault(loser, 0);
                    _organismSizes.Remove(loser);
                    _macroStructureAgeTracker[winner] = Math.Max(
                        _macroStructureAgeTracker.GetValueOrDefault(winner, 0),
                        _macroStructureAgeTracker.GetValueOrDefault(loser, 0)
                    );
                    _macroStructureAgeTracker.Remove(loser);

                    if (_organismDriftAngle.ContainsKey(loser)) _organismDriftAngle.Remove(loser);

                    break;
                }
            }
        }

        // 8. Переиндексация spatial grid после всех изменений
        _spatialGrid.Clear();
        for (int i = 0; i < _cells.Count; i++)
        {
            _spatialGrid.Insert(_cells[i]);
        }
    }

    private void ApplyOrganismStressAndRupture()
    {
        if (_tick % 10 != 0 || _cells.Count == 0) return;

        // Собираем уникальные организмы
        var uniqueOrganismIds = new List<uint>();
        var seenOrganisms = new HashSet<uint>();
        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].OrganismId.HasValue && seenOrganisms.Add(_cells[i].OrganismId.Value))
            {
                uniqueOrganismIds.Add(_cells[i].OrganismId.Value);
            }
        }
        uniqueOrganismIds.Sort();

        foreach (uint orgId in uniqueOrganismIds)
        {
            // Считаем массу и находим органы этого организма
            int totalMass = 0;
            var organSizesInOrg = new Dictionary<uint, int>();

            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].OrganismId == orgId)
                {
                    totalMass++;
                    if (_cells[i].OrganId.HasValue)
                    {
                        uint oId = _cells[i].OrganId.Value;
                        organSizesInOrg[oId] = organSizesInOrg.GetValueOrDefault(oId, 0) + 1;
                    }
                }
            }

            // 1. Стресс перемасса (> 240)
            if (totalMass > 320)
            {
                float overweight = totalMass - 320f;
                // Квадратичный рост урона: при 250 ~ 0.1, при 300 ~ 1.0, при 350 ~ 3.0
                float stressDamage = overweight * 0.05f;  // Линейный рост

                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i].OrganismId == orgId)
                    {
                        float resistance = _cells[i].Genome.EnvironmentResistance;

                        // ИСПРАВЛЕНО: Внутреннее давление обходит 80% защиты. 
                        // Броня работает против токсинов/температуры, но не спасает от разрыва тканей изнутри.
                        float effectiveResistance = resistance * 0.2f;

                        // Усиливаем базовый множитель с 10f до 5f, но теперь он реально пробивает броню
                        float actualDamage = stressDamage * (1f - effectiveResistance) * 5f;

                        _cells[i].Integrity -= actualDamage;
                    }
                }
            }

            // 2. Разрыв самого толстого органа (>= 320)
            if (totalMass >= 320)
            {
                float ruptureChance = (totalMass - 320f) * 0.002f; // Растет с перевесом
                if (_rng.NextDouble() < ruptureChance)
                {
                    // Находим самый большой орган (тай-брейк: меньший ID)
                    uint targetOrganId = 0;
                    int maxSize = 0;

                    var sortedOrgans = new List<uint>(organSizesInOrg.Keys);
                    sortedOrgans.Sort(); // Детерминированный обход

                    foreach (uint oId in sortedOrgans)
                    {
                        if (organSizesInOrg[oId] > maxSize)
                        {
                            maxSize = organSizesInOrg[oId];
                            targetOrganId = oId;
                        }
                    }

                    if (targetOrganId != 0 && maxSize >= 30)
                    {
                        // Убиваем весь орган
                        int rupturedCells = 0;
                        for (int i = _cells.Count - 1; i >= 0; i--)
                        {
                            if (_cells[i].OrganId == targetOrganId)
                            {
                                SpawnNutrientAt(_cells[i].X, _cells[i].Y, NutrientType.MeatScraps, 12f);
                                _cells.RemoveAt(i);
                                rupturedCells++;
                                _deathStats.RuptureDeaths++;
                                _deathStats.TotalDeaths++;
                            }
                        }

                        SoupLogger.Log($"[💥 ORGAN RUPTURE] Организм {orgId} лопнул от жадности! Масса: {totalMass}. Погиб орган {targetOrganId} ({rupturedCells} кл.)");
                    }
                }
            }
        }
    }

    private void ProcessMacroOrganismLandExit()
    {
        if (_tick % 20 != 0 || _cells.Count == 0) return;

        var uniqueOrganismIds = new List<uint>();
        var seenOrganisms = new HashSet<uint>();
        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].OrganismId.HasValue && seenOrganisms.Add(_cells[i].OrganismId.Value))
            {
                uniqueOrganismIds.Add(_cells[i].OrganismId.Value);
            }
        }
        uniqueOrganismIds.Sort();

        foreach (uint organismId in uniqueOrganismIds)
        {
            var organismCells = new List<Cell>();
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].OrganismId == organismId)
                {
                    organismCells.Add(_cells[i]);
                }
            }

            if (organismCells.Count == 0) continue;

            // Группируем по OrganId для подсчета зрелых органов
            var organGroups = new Dictionary<uint, List<Cell>>();
            for (int i = 0; i < organismCells.Count; i++)
            {
                if (organismCells[i].OrganId.HasValue)
                {
                    uint oId = organismCells[i].OrganId.Value;
                    if (!organGroups.ContainsKey(oId)) organGroups[oId] = new List<Cell>();
                    organGroups[oId].Add(organismCells[i]);
                }
            }

            int trueOrganismModulesCount = 0;
            var sortedOrganIds = new List<uint>(organGroups.Keys);
            sortedOrganIds.Sort();

            foreach (uint oId in sortedOrganIds)
            {
                if (organGroups[oId].Count >= 30)
                {
                    trueOrganismModulesCount++;
                }
            }

            int totalMass = organismCells.Count;

            if (totalMass >= 30 && _tick % 500 == 0 && trueOrganismModulesCount > 0)
            {
                SoupLogger.Log($"Организм ({organismId}) имеет {totalMass} клеток. Зрелых органов (30+ кл): {trueOrganismModulesCount}");
            }

            // Условия выхода: 6-9 органов, >= 160 массы
            if (trueOrganismModulesCount >= 6 && trueOrganismModulesCount <= 9 && totalMass >= 160)
            {
                int structureAge = _macroStructureAgeTracker.GetValueOrDefault(organismId, 0) + 20;
                _macroStructureAgeTracker[organismId] = structureAge;

                if (structureAge % 20 == 0 && structureAge <= 100)
                {
                    SoupLogger.Log($"[🌱 КЛЕТОЧНАЯ ИНКУБАЦИЯ] Организм {organismId} обрастает тканями: {structureAge}/100 тиков. Вес: {totalMass} кл. Органов: {trueOrganismModulesCount}");
                }

                if (structureAge >= 100)
                {
                    float totalWeight = 0;
                    for (int i = 0; i < organismCells.Count; i++) totalWeight += organismCells[i].ContributionScore + 0.1f;

                    float speedSum = 0, senseSum = 0, metabSum = 0, divSum = 0, mutSum = 0, mergeSum = 0, commSum = 0;
                    float lightSum = 0, chemSum = 0, orgSum = 0, predSum = 0, regenSum = 0, toxSum = 0, diffSum = 0;
                    float thermSum = 0, optTempSum = 0;

                    for (int i = 0; i < organismCells.Count; i++)
                    {
                        float w = organismCells[i].ContributionScore + 0.1f;
                        var g = organismCells[i].Genome;
                        speedSum += g.Speed * w; senseSum += g.SenseRadius * w; metabSum += g.Metabolism * w;
                        divSum += g.DivisionThreshold * w; mutSum += g.MutationRate * w; mergeSum += g.MergeAffinity * w;
                        commSum += g.CommunicationRadius * w; lightSum += g.LightDependency * w; chemSum += g.ChemicalAffinity * w;
                        orgSum += g.OrganicAffinity * w; predSum += g.PredatoryDrive * w; regenSum += g.RegenerationRate * w;
                        toxSum += g.EnvironmentResistance * w; diffSum += g.CellDifferentiation * w; thermSum += g.Thermotolerance * w;
                        optTempSum += g.OptimalTemperature * w;
                    }

                    var controlGenome = new AbiogenesisGenome
                    {
                        Speed = speedSum / totalWeight,
                        SenseRadius = senseSum / totalWeight,
                        Metabolism = metabSum / totalWeight,
                        DivisionThreshold = divSum / totalWeight,
                        MutationRate = mutSum / totalWeight,
                        MergeAffinity = mergeSum / totalWeight,
                        CommunicationRadius = commSum / totalWeight,
                        LightDependency = lightSum / totalWeight,
                        ChemicalAffinity = chemSum / totalWeight,
                        OrganicAffinity = orgSum / totalWeight,
                        PredatoryDrive = predSum / totalWeight,
                        RegenerationRate = regenSum / totalWeight,
                        EnvironmentResistance = toxSum / totalWeight,
                        CellDifferentiation = diffSum / totalWeight,
                        Thermotolerance = thermSum / totalWeight,
                        OptimalTemperature = optTempSum / totalWeight
                    };

                    bool isNewSpecies = true;
                    for (int si = 0; si < _savedSpecies.Count; si++)
                    {
                        if (controlGenome.DistanceTo(_savedSpecies[si].OrganismGenome) < 0.45f)
                        {
                            isNewSpecies = false;
                            break;
                        }
                    }

                    if (isNewSpecies)
                    {
                        float total = organismCells.Count;
                        int photoCount = 0, chemoCount = 0, predCount = 0, defCount = 0, sensCount = 0, speedCount = 0;
                        for (int i = 0; i < organismCells.Count; i++)
                        {
                            var g = organismCells[i].Genome;
                            if (g.LightDependency > 0.65f) photoCount++;
                            if (g.ChemicalAffinity > 0.65f) chemoCount++;
                            if (g.PredatoryDrive > 0.55f) predCount++;
                            if (g.EnvironmentResistance > 0.65f) defCount++;
                            if (g.SenseRadius > 0.65f) sensCount++;
                            if (g.Speed > 0.6f) speedCount++;
                        }

                        double nervousIntellect = Math.Round(sensCount / total, 2);
                        SnapshotSpeciesData speciesData = new SnapshotSpeciesData
                        {
                            SpeciesId = "PENDING_ID",
                            TotalCellMass = totalMass,
                            OrganisedModulesCount = trueOrganismModulesCount,
                            ExitTick = _tick,
                            AnatomyProfile = new Dictionary<string, double>
                            {
                                { "DigestiveSystem", Math.Round(chemoCount / total + controlGenome.OrganicAffinity, 2) },
                                { "EpithelialArmor", Math.Round(defCount / total, 2) },
                                { "NervousSystem", nervousIntellect },
                                { "LocomotorSystem", Math.Round(speedCount / total, 2) }
                            },
                            OrganismGenome = controlGenome,
                            EvolutionHistory = new EvolutionaryHistory
                            {
                                OrganismId = organismId,
                                FirstAppearanceTick = _tick,
                                TotalGenerations = 1
                            }
                        };

                        if (nervousIntellect >= 0.10)
                        {
                            _stableSpeciesCount++;
                            _savedSpecies.Add(speciesData);
                            Console.WriteLine($"\n[👑 RAZUM SPECIES] НА СУШУ ВЫШЕЛ РАЗУМНЫЙ ВИД: RAZUM_SPECIES_{_stableSpeciesCount:D2} на тике {_tick}! (Масса: {speciesData.TotalCellMass} кл.)");
                            ExportResults();
                        }
                        else
                        {
                            _stableBeastCount++;
                            _beastSpecies.Add(speciesData);
                            Console.WriteLine($"\n[🦖 BEAST DETECTED] В БЕСТИАРИЙ СУШИ ВЫБРОШЕНО ЖИВОТНОЕ на тике {_tick}! (Интеллект: {nervousIntellect})");
                            ExportResults();
                        }
                    }

                    // Удаляем клетки организма из воды
                    var exitingIds = new HashSet<uint>(organismCells.Count);
                    for (int i = 0; i < organismCells.Count; i++) exitingIds.Add(organismCells[i].Id);

                    for (int i = _cells.Count - 1; i >= 0; i--)
                    {
                        if (exitingIds.Contains(_cells[i].Id)) _cells.RemoveAt(i);
                    }

                    _macroStructureAgeTracker.Remove(organismId);
                    _organismDriftAngle.Remove(organismId);
                }
            }
            else
            {
                // Деградация, если условия не выполняются
                if (_macroStructureAgeTracker.ContainsKey(organismId))
                {
                    int currentAge = _macroStructureAgeTracker[organismId];
                    currentAge -= 15;
                    if (currentAge <= 0)
                    {
                        _macroStructureAgeTracker.Remove(organismId);
                    }
                    else
                    {
                        _macroStructureAgeTracker[organismId] = currentAge;
                        SoupLogger.Log($"[⚠️ TISSUE INSTABILITY] Организм {organismId} потерял стабильность. Таймер деградирует: {currentAge}/150 тиков.");
                    }
                }
            }
        }
    }


    private void SpawnNutrientAt(float x, float y, NutrientType type, float amount)
    {
        _map.AddNutrient(x, y, type, amount);
    }

    public void PrintDeathStatistics()
    {
        _deathStats.PrintReport();
    }

    public void ExportResults()
    {
        string targetFolder = @"D:\repos\EvolveGenEngineAgentBM\AbiogenesisEngine\JSON_Data";
        Directory.CreateDirectory(targetFolder);
        var opts = new JsonSerializerOptions { WriteIndented = true };

        // =====================================================================
        // 1. ACCUMULATIVE EXPORT OF HIGHER INTELLIGENT SPECIES (NO DUPLICATES)
        // =====================================================================
        if (_savedSpecies.Count > 0)
        {
            string razumPath = Path.Combine(targetFolder, "abiogenesis_results.json");
            var globalRazumList = new List<SnapshotSpeciesData>();
            var existingTicks = new HashSet<int>();

            if (File.Exists(razumPath))
            {
                try
                {
                    string oldJson = File.ReadAllText(razumPath);
                    using var doc = JsonDocument.Parse(oldJson);
                    if (doc.RootElement.TryGetProperty("EvolvedLandSpecies", out var oldList))
                    {
                        foreach (var item in oldList.EnumerateArray())
                        {
                            var spec = JsonSerializer.Deserialize<SnapshotSpeciesData>(item.GetRawText());
                            if (spec != null && !existingTicks.Contains(spec.ExitTick))
                            {
                                globalRazumList.Add(spec);
                                existingTicks.Add(spec.ExitTick);
                            }
                        }
                    }
                }
                catch { /* Quietly ignore if file was empty or corrupted */ }
            }

            foreach (var currentSpec in _savedSpecies)
            {
                if (!existingTicks.Contains(currentSpec.ExitTick))
                {
                    globalRazumList.Add(currentSpec);
                    existingTicks.Add(currentSpec.ExitTick);
                }
            }

            for (int i = 0; i < globalRazumList.Count; i++)
            {
                globalRazumList[i].SpeciesId = $"RAZUM_SPECIES_{i + 1:D2}";
            }

            var finalHubResult = new
            {
                LastActiveSeed = _realSeed,
                TotalTicksAccumulated = _tick,
                TotalRazumCount = globalRazumList.Count,
                EvolvedLandSpecies = globalRazumList
            };

            string tempRazumPath = razumPath + ".tmp";
            File.WriteAllText(tempRazumPath, JsonSerializer.Serialize(finalHubResult, opts));
            // Атомарная замена: если краш случится во время записи, старый файл останется целым
            File.Move(tempRazumPath, razumPath, overwrite: true);
            Console.WriteLine($"[JSON_DATA] Global RAZUM species database updated! Total clean species: {globalRazumList.Count}");
        }

        // =====================================================================
        // 2. ACCUMULATIVE EXPORT OF WILD BEASTS MATRIX (FIXED TYPES)
        // =====================================================================
        if (_beastSpecies.Count > 0)
        {
            string beastsPath = Path.Combine(targetFolder, "soup_beasts_results.json");
            var globalBeastsList = new List<SnapshotSpeciesData>();
            var existingBeastTicks = new HashSet<int>();

            if (File.Exists(beastsPath))
            {
                try
                {
                    string oldJson = File.ReadAllText(beastsPath);
                    using var doc = JsonDocument.Parse(oldJson);
                    if (doc.RootElement.TryGetProperty("WildBeasts", out var oldList))
                    {
                        foreach (var item in oldList.EnumerateArray())
                        {
                            var beast = JsonSerializer.Deserialize<SnapshotSpeciesData>(item.GetRawText());
                            if (beast != null && !existingBeastTicks.Contains(beast.ExitTick))
                            {
                                globalBeastsList.Add(beast);
                                existingBeastTicks.Add(beast.ExitTick);
                            }
                        }
                    }
                }
                catch { /* Quietly ignore reading errors */ }
            }

            foreach (var currentBeast in _beastSpecies)
            {
                if (!existingBeastTicks.Contains(currentBeast.ExitTick))
                {
                    globalBeastsList.Add(currentBeast);
                    existingBeastTicks.Add(currentBeast.ExitTick);
                }
            }

            for (int i = 0; i < globalBeastsList.Count; i++)
            {
                globalBeastsList[i].SpeciesId = $"WILD_BEAST_{i + 1:D2}";
            }

            var finalBeastsResult = new
            {
                LastActiveSeed = _realSeed,
                TotalBeastsCount = globalBeastsList.Count,
                WildBeasts = globalBeastsList
            };

            string tempBeastsPath = beastsPath + ".tmp";
            File.WriteAllText(tempBeastsPath, JsonSerializer.Serialize(finalBeastsResult, opts));
            File.Move(tempBeastsPath, beastsPath, overwrite: true);
            Console.WriteLine($"[JSON_DATA] Global WILD BEASTS database updated! Total clean beasts: {globalBeastsList.Count}");
        }
    } // Конец метода ExportResults

    public void RunSingleTick() => Step();

    public List<Cell> GetFreeCells() => _cells;
    public List<object> GetColonies() => new List<object>();

    public AbiogenesisMap GetMap() => _map;
    public int GetTick() => _tick;
    public int GetStableSpeciesCount() => _stableSpeciesCount;
    public int GetStableBeastCount() => _stableBeastCount;
}



public class SnapshotSpeciesData
{
    public string SpeciesId { get; set; }
    public int TotalCellMass { get; set; }
    public int OrganisedModulesCount { get; set; }
    public int ExitTick { get; set; }
    public Dictionary<string, double> AnatomyProfile { get; set; }
    public AbiogenesisGenome OrganismGenome { get; set; }
    public EvolutionaryHistory EvolutionHistory { get; set; }
}
public class DeathStatistics
{
    public int TotalDeaths { get; set; }
    public int StarvationDeaths { get; set; }      // Энергия <= 0
    public int TraumaDeaths { get; set; }          // Целостность <= 0 (урон, температура)
    public int MeteoriteDeaths { get; set; }       // Прямое попадание метеорита
    public int RuptureDeaths { get; set; }         // Разрыв органа от перенаселения
    public int CollapseDeaths { get; set; }        // Глобальный коллапс цепи
    public int CullingDeaths { get; set; }         // Удаление из-за лимита популяции (5000+)

    public void PrintReport()
    {
        Console.WriteLine("\n========== 📊 СТАТИСТИКА СМЕРТНОСТИ ==========");
        Console.WriteLine($"📉 Всего смертей: {TotalDeaths}");
        Console.WriteLine($"🍎 От голода (нет энергии): {StarvationDeaths}");
        Console.WriteLine($"🩸 От травм (потеря целостности): {TraumaDeaths}");
        Console.WriteLine($"☄️ От метеоритов: {MeteoriteDeaths}");
        Console.WriteLine($"💥 От разрыва органов: {RuptureDeaths}");
        Console.WriteLine($"💀 От глобального коллапса: {CollapseDeaths}");
        Console.WriteLine($"✂️ От лимита популяции: {CullingDeaths}");
        Console.WriteLine("==============================================\n");
    }
}

