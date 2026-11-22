using System;
using System.Collections.Generic;

namespace AbiogenesisEngine.Core;

public class Cell
{
    public uint Id;
    public uint? ColonyId;   // блуждающая колония или почка-колония
    public uint? OrganId;    // зрелый орган (30-60 клеток)
    public uint? OrganismId; // тело-крыша над органами
    public float X, Y;
    public float Energy;
    public int Age;
    public int BirthTick;
    public AbiogenesisGenome Genome;
    public float ColonyCenterX, ColonyCenterY;
    public bool HasColonyCenter;
    public float OrganCenterX, OrganCenterY;
    public bool HasOrganCenter;
    public float OrganismCenterX, OrganismCenterY;
    public bool HasOrganismCenter;
    public float ContributionScore;
    public float Integrity = 100f;
    public float HeadingX = 1f, HeadingY = 0f;
    public float DriftX, DriftY;
    public List<(float X, float Y)> Trail = new();

    public Cell(float x, float y, AbiogenesisGenome genome, float initialEnergy = 50f)
    {
        X = x; Y = y;
        Genome = genome;
        Energy = initialEnergy;
        BirthTick = 0;
        // Стартовый курс детерминирован из позиции: разброс по карте сразу, без общего направления.
        float startAngle = (x * 0.013f + y * 0.027f);
        HeadingX = MathF.Cos(startAngle);
        HeadingY = MathF.Sin(startAngle);
    }
    public bool IsKin(Cell other)
    {
        if (OrganismId.HasValue && other.OrganismId.HasValue)
            return OrganismId.Value == other.OrganismId.Value;

        if (!OrganismId.HasValue && !other.OrganismId.HasValue &&
            ColonyId.HasValue && other.ColonyId.HasValue)
            return ColonyId.Value == other.ColonyId.Value;

        return false;
    }

    public bool SameGroup(Cell other)
    {
        if (OrganId.HasValue && other.OrganId.HasValue)
            return OrganId.Value == other.OrganId.Value;

        if (!OrganId.HasValue && !other.OrganId.HasValue &&
            ColonyId.HasValue && other.ColonyId.HasValue)
            return ColonyId.Value == other.ColonyId.Value;

        return false;
    }

    public void Update(AbiogenesisMap map, Random rng, CellSpatialGrid grid)
    {
        Age++;
        Trail.Add((X, Y));
        if (Trail.Count > 8) Trail.RemoveAt(0);

        // 1. Урон или бонус от токсинов
        float toxinLevel = map.GetToxinAt(X, Y);
        if (Genome.EnvironmentResistance < 0.4f)
        {
            float toxinDamage = toxinLevel * 2f * (1f - Genome.EnvironmentResistance / 0.4f);
            Integrity -= toxinDamage;
        }
        else
        {
            float toxinFood = toxinLevel * (Genome.EnvironmentResistance - 0.4f) * 5f;
            Energy += Math.Min(3.5f, toxinFood);
        }

        // 2. Сбор энергии из среды (КОНКУРЕНЦИЯ ФЕРМЕНТОВ И ВСЕЯДНОСТЬ)
        float energyGain = 0f;

        // Считаем общую сумму гетеротрофных ферментов, чтобы узнать их доли
        float totalDietSum = Genome.ChemicalAffinity + Genome.OrganicAffinity + Genome.PredatoryDrive;
        if (totalDietSum <= 0.05f) totalDietSum = 0.05f; // защита от деления на ноль

        // Вычисляем честную биологическую долю (процент) каждого фермента в желудке
        float chemicalShare = Genome.ChemicalAffinity / totalDietSum;
        float organicShare = Genome.OrganicAffinity / totalDietSum;
        float predatoryShare = Genome.PredatoryDrive / totalDietSum;

        // А. СОЛНЦЕ (Уникальная стратегия, исключена из общего пула желудка)
        float light = map.GetLightAt(X, Y);
        if (light > 0.3f && Genome.LightDependency > 0.6f)
        {
            energyGain += Genome.LightDependency * light * 3f;
        }

        // Б. МИНЕРАЛЫ (Зависят от доли chemicalShare)
        if (Genome.ChemicalAffinity > 0.15f)
        {
            var chem = map.FindNearestNutrient(X, Y, Genome.SenseRadius * 200f, NutrientType.MineralSalt);
            if (chem != null)
            {
                float dx = chem.X - X, dy = chem.Y - Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 3f)
                {
                    X += (dx / dist) * Genome.Speed * 1.5f;
                    Y += (dy / dist) * Genome.Speed * 1.5f;
                }
                else
                {
                    float eaten = Math.Min(15f, chem.Amount);
                    chem.Amount -= eaten;

                    // Энергия зависит от ЧЕСТНОЙ доли фермента в организме
                    energyGain += chemicalShare * eaten * 3.0f;
                }
            }
        }

        // В. ТРАВА / ОРГАНИКА (Зависит от доли organicShare)
        if (Genome.OrganicAffinity > 0.15f)
        {
            var org = map.FindNearestNutrient(X, Y, Genome.SenseRadius * 200f, NutrientType.OrganicSludge);
            if (org != null)
            {
                float dx = org.X - X, dy = org.Y - Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 3f)
                {
                    X += (dx / dist) * Genome.Speed * 1.5f;
                    Y += (dy / dist) * Genome.Speed * 1.5f;
                }
                else
                {
                    float eaten = Math.Min(20f, org.Amount);
                    org.Amount -= eaten;

                    // Если укусил хищник с organicShare = 0.33, он получит крохи
                    energyGain += organicShare * eaten * 3.0f;
                }
            }
        }


        if (Genome.PredatoryDrive > 0.15f)
        {
            var prey = grid.FindNearestPrey(X, Y, Genome.SenseRadius * 180f, this);
            bool kin = ColonyId.HasValue && prey != null && prey.ColonyId == ColonyId;

            // Охотимся только на более слабых (жертва < 70% энергии охотника)
            if (prey != null && !kin && prey.Energy < Energy * 0.7f)
            {
                float dx = prey.X - X, dy = prey.Y - Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 3f)
                {
                    X += (dx / dist) * Genome.Speed * 2f;
                    Y += (dy / dist) * Genome.Speed * 2f;
                }
                else
                {
                    // Базовое "высасывание" зависит от хищности
                    float baseDrain = Genome.PredatoryDrive * 30f;

                    // Броня жертвы снижает количество украденной энергии
                    float armorReduction = prey.Genome.EnvironmentResistance * 0.8f;
                    float effectiveDrain = baseDrain * (1f - armorReduction);

                    // ЗАЩИТА: нельзя украсть больше энергии, чем у жертвы есть в данный момент
                    float stolen = Math.Min(effectiveDrain, prey.Energy);

                    // Жертва теряет ровно столько, сколько у неё украли
                    prey.Energy -= stolen;

                    // Хищник получает ровно свою долю от украденного. 
                    // Никаких множителей! Если stolen = 30, а predatoryShare = 0.5, он получит ровно 15.
                    energyGain += predatoryShare * stolen * 3.0f;
                }
            }
            else
            {
                // Поедание падали (без изменений)
                var meat = map.FindNearestNutrient(X, Y, Genome.SenseRadius * 180f, NutrientType.MeatScraps);
                if (meat != null)
                {
                    float dx = meat.X - X, dy = meat.Y - Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > 3f)
                    {
                        X += (dx / dist) * Genome.Speed * 1.5f;
                        Y += (dy / dist) * Genome.Speed * 1.5f;
                    }
                    else
                    {
                        float eaten = Math.Min(15f, meat.Amount);
                        meat.Amount -= eaten;
                        energyGain += predatoryShare * eaten * 3.0f;
                    }
                }
            }
        }



        // 3. Температурное воздействие (ИСПРАВЛЕНО: бьёт по Integrity, а не по Energy)
        // === ТЕМПЕРАТУРНОЕ ВОЗДЕЙСТВИЕ (АСИММЕТРИЧНОЕ) ===
        float temperature = map.GetTemperatureAt(X, Y);
        float tempDifference = temperature - Genome.OptimalTemperature; // > 0 если жарко, < 0 если холодно
        float absDiff = MathF.Abs(tempDifference);

        // 1. Влияние на эффективность процессов (и жара, и холод немного мешают)
        float tempEfficiency = 1f - (absDiff * (1.0f - Genome.Thermotolerance));
        tempEfficiency = Math.Clamp(tempEfficiency, 0.1f, 1f);
        energyGain *= tempEfficiency;

        // 2. Метаболические затраты
        float baseMetabolism = Genome.Metabolism * 0.3f;

        if (ColonyId.HasValue)
        {
            baseMetabolism *= 0.7f; // бонус колонии: меньше трат на поддержание
        }

        if (tempDifference > 0f)
        {
            // ЖАРА: клетке НЕ нужно греться, метаболизм замедляется.
            // Чем жарче — тем меньше базовых трат (экономия до 50%).
            float heatSaving = tempDifference * 0.5f;
            float effectiveMetabolism = baseMetabolism * MathF.Max(0.5f, 1f - heatSaving);
            Energy -= effectiveMetabolism;
        }
        else
        {
            // ХОЛОД: клетка тратит ДОПОЛНИТЕЛЬНУЮ энергию на поддержание гомеостаза ("согреться")
            float coldStress = MathF.Abs(tempDifference);
            Energy -= baseMetabolism * (1f + coldStress);
        }

        // 3. Прямой физический урон (ТОЛЬКО ОТ ЖАРЫ)
        // Перегрев выше оптимальной зоны начинает плавить мембрану
        if (tempDifference > 0.25f)
        {
            float shockDamage = (tempDifference - 0.25f) * 5f * (1f - Genome.Thermotolerance);
            Integrity -= shockDamage;
        }

        // Экстремальная жара (кипяток)
        if (temperature > 0.75f)
        {
            float burnIntensity = (temperature - 0.75f) * 30f * (1f - Genome.Thermotolerance);
            Integrity -= burnIntensity;
        }

        // 4. Движение: иерархия притяжения (Организм > Орган > Колония)
        bool isGrouped = OrganismId.HasValue || OrganId.HasValue || ColonyId.HasValue;

        if (isGrouped)
        {
            float targetX = X, targetY = Y;
            float magnetStrength = Genome.Speed;

            // Приоритет 1: Организм (самая сильная связь)
            if (HasOrganismCenter)
            {
                targetX = OrganismCenterX;
                targetY = OrganismCenterY;
                magnetStrength *= 0.8f; // Слабее, чтобы органы не слипались в точку
            }
            // Приоритет 2: Орган (плотное ядро)
            else if (HasOrganCenter)
            {
                targetX = OrganCenterX;
                targetY = OrganCenterY;
                magnetStrength *= 1.2f; // Сильнее, держим форму органа
            }
            // Приоритет 3: Колония (почка или свободная кучка)
            else if (HasColonyCenter)
            {
                targetX = ColonyCenterX;
                targetY = ColonyCenterY;
                magnetStrength *= 1.5f;
            }

            float dx = targetX - X;
            float dy = targetY - Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 15f)
            {
                float hunger = 1f - Math.Clamp(Energy / 60f, 0f, 1f);
                float drive = magnetStrength * (1.2f + hunger * 1.5f);
                X += (dx / dist) * drive;
                Y += (dy / dist) * drive;
            }
            else if (dist < 6f)
            {
                // ОТТАЛКИВАНИЕ: чем ближе, тем сильнее расталкиваем
                float repulsionForce = (6f - dist) / 6f; // 0..1, где 1 = очень близко
                float repulsionStrength = Genome.Speed * (0.8f + repulsionForce * 1.2f);

                X -= (dx / dist) * repulsionStrength;
                Y -= (dy / dist) * repulsionStrength;
            }
            else
            {
                X += (float)(rng.NextDouble() - 0.5) * Genome.Speed * 0.4f;
                Y += (float)(rng.NextDouble() - 0.5) * Genome.Speed * 0.4f;
            }

            // ДОПОЛНИТЕЛЬНОЕ ОТТАЛКИВАНИЕ ОТ БЛИЖАЙШИХ СОСЕДЕЙ (глобальное)
            // Проверяем всех соседей в радиусе 8px и расталкиваем если слишком близко
            var nearbyCells = grid.GetNearbyCells(X, Y, 8f);
            for (int n = 0; n < nearbyCells.Count; n++)
            {
                var neighbor = nearbyCells[n];
                if (neighbor.Id == Id) continue;

                float ndx = X - neighbor.X;
                float ndy = Y - neighbor.Y;
                float ndist = MathF.Sqrt(ndx * ndx + ndy * ndy);

                if (ndist < 3f && ndist > 0.1f)
                {
                    // Сильное отталкивание на очень близких дистанциях
                    float pushStrength = (3f - ndist) * 0.5f;
                    X += (ndx / ndist) * pushStrength;
                    Y += (ndy / ndist) * pushStrength;
                }
            }

            if (energyGain <= 0f)
            {
                X += DriftX;
                Y += DriftY;
            }
        }
        else
        {
            // ОДИНОЧКА
            float rot = (float)(rng.NextDouble() - 0.5) * 0.36f;
            float cs = MathF.Cos(rot), sn = MathF.Sin(rot);
            float nhx = HeadingX * cs - HeadingY * sn;
            float nhy = HeadingX * sn + HeadingY * cs;
            HeadingX = nhx; HeadingY = nhy;
            float step = Genome.Speed * 2.8f;
            X += HeadingX * step;
            Y += HeadingY * step;
            if (X <= 0f || X >= map.Width - 1f) HeadingX = -HeadingX;
            if (Y <= 0f || Y >= map.Height - 1f) HeadingY = -HeadingY;
        }
        Energy += energyGain;

        // 6. Регенерация (чинит Integrity за счет Energy)
        // 6. Регенерация (чинимся только при серьезных повреждениях, чтобы не тратить энергию на микроцарапины каждый тик)
        if (Integrity < 90f && Energy > 25f)
        {
            float repair = Genome.RegenerationRate; // Чуть эффективнее, когда действительно нужно
            Integrity = Math.Min(100f, Integrity + repair);
            Energy -= repair * 0.8f; // Плата за регенерацию
        }

        // 7. Границы и вклад
        X = Math.Clamp(X, 0, map.Width - 1);
        Y = Math.Clamp(Y, 0, map.Height - 1);
        // Считаем базовый вклад
        float rawContribution = energyGain / Math.Max(0.1f, Genome.Metabolism);

        // Если при делении получилась бесконечность или не-число (NaN) — принудительно гасим в 0!
        if (float.IsInfinity(rawContribution) || float.IsNaN(rawContribution))
        {
            ContributionScore = 0f;
        }
        else
        {
            ContributionScore = rawContribution;
        }
    }

    // Порог 25f дает клетке шанс мигнуть белым и попытаться отрегенерировать перед смертью
    public bool IsDying => Energy < 25f || Integrity < 25f;
}