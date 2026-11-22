using System;

namespace AbiogenesisEngine.Core;

public struct AbiogenesisGenome
{
    // ИСПРАВЛЕНО: Превратили в свойства { get; set; }, чтобы System.Text.Json их видел!
    public float Speed { get; set; }
    public float SenseRadius { get; set; }
    public float Metabolism { get; set; }
    public float DivisionThreshold { get; set; }
    public float MutationRate { get; set; }
    public float MergeAffinity { get; set; }
    public float CommunicationRadius { get; set; }
    public float LightDependency { get; set; }
    public float ChemicalAffinity { get; set; }
    public float OrganicAffinity { get; set; }
    public float PredatoryDrive { get; set; }
    public float RegenerationRate { get; set; }
    public float EnvironmentResistance { get; set; } // Environment Resistance: устойчивость к токсинам И физическим воздействиям (ударные волны, температурные шоки). Клетки с высокой EnvironmentResistance имеют плотные мембраны, защищающие от химического и механического стресса.
    public float CellDifferentiation { get; set; }
    public float Thermotolerance { get; set; }      // Устойчивость к экстремальным температурам (0-1)
    public float OptimalTemperature { get; set; }   // Предпочтительная температура (0-1, где 0=холод, 1=жара)

    public AbiogenesisGenome(float speed = 0.5f, float senseRadius = 0.5f,
        float metabolism = 0.5f, float divisionThreshold = 0.7f,
        float mutationRate = 0.1f, float mergeAffinity = 0.5f,
        float communicationRadius = 0.5f, float lightDependency = 0.5f,
        float chemicalAffinity = 0.5f, float organicAffinity = 0.5f,
        float predatoryDrive = 0.1f, float regenerationRate = 0.5f,
        float environmentResistance = 0.3f, float cellDifferentiation = 0.3f, 
        float thermotolerance = 0.3f, float optimalTemperature = 0.5f)
    {
        Speed = speed; SenseRadius = senseRadius; Metabolism = metabolism;
        DivisionThreshold = divisionThreshold; MutationRate = mutationRate;
        MergeAffinity = mergeAffinity; CommunicationRadius = communicationRadius;
        LightDependency = lightDependency; ChemicalAffinity = chemicalAffinity;
        OrganicAffinity = organicAffinity; PredatoryDrive = predatoryDrive;
        RegenerationRate = regenerationRate; EnvironmentResistance = environmentResistance;
        CellDifferentiation = cellDifferentiation; Thermotolerance = thermotolerance;
        OptimalTemperature = optimalTemperature;
    }

    public AbiogenesisGenome Mutate(Random rng)
    {
        float m = Math.Max(0.01f, MutationRate);
        float MutateVal(float val, float min = 0f, float max = 1f) =>
            Math.Clamp(val + (float)(rng.NextDouble() - 0.5) * m, min, max);

        return new AbiogenesisGenome(
            MutateVal(Speed), MutateVal(SenseRadius), MutateVal(Metabolism),
            MutateVal(DivisionThreshold, 0.3f, 0.95f), MutateVal(MutationRate, 0.01f, 0.3f),
            MutateVal(MergeAffinity), MutateVal(CommunicationRadius),
            MutateVal(LightDependency), MutateVal(ChemicalAffinity),
            MutateVal(OrganicAffinity), MutateVal(PredatoryDrive),
            MutateVal(RegenerationRate), MutateVal(EnvironmentResistance),
            MutateVal(CellDifferentiation), MutateVal(Thermotolerance),
            MutateVal(OptimalTemperature)
        );
    }

    public static AbiogenesisGenome Crossover(AbiogenesisGenome a, AbiogenesisGenome b, Random rng)
    {
        float Pick(float x, float y) => rng.NextDouble() < 0.5f ? x : y;
        return new AbiogenesisGenome
        {
            Speed = Pick(a.Speed, b.Speed),
            SenseRadius = Pick(a.SenseRadius, b.SenseRadius),
            Metabolism = Pick(a.Metabolism, b.Metabolism),
            DivisionThreshold = Pick(a.DivisionThreshold, b.DivisionThreshold),
            MutationRate = Pick(a.MutationRate, b.MutationRate),
            MergeAffinity = Pick(a.MergeAffinity, b.MergeAffinity),
            CommunicationRadius = Pick(a.CommunicationRadius, b.CommunicationRadius),
            LightDependency = Pick(a.LightDependency, b.LightDependency),
            ChemicalAffinity = Pick(a.ChemicalAffinity, b.ChemicalAffinity),
            OrganicAffinity = Pick(a.OrganicAffinity, b.OrganicAffinity),
            PredatoryDrive = Pick(a.PredatoryDrive, b.PredatoryDrive),
            RegenerationRate = Pick(a.RegenerationRate, b.RegenerationRate),
            EnvironmentResistance = Pick(a.EnvironmentResistance, b.EnvironmentResistance), // Фикс затирания при кроссовере
            CellDifferentiation = Pick(a.CellDifferentiation, b.CellDifferentiation),
            Thermotolerance = Pick(a.Thermotolerance, b.Thermotolerance),
            OptimalTemperature = Pick(a.OptimalTemperature, b.OptimalTemperature)
        };
    }

    public float DistanceTo(AbiogenesisGenome other)
    {
        return MathF.Abs(Speed - other.Speed) +
               MathF.Abs(SenseRadius - other.SenseRadius) +
               MathF.Abs(Metabolism - other.Metabolism) +
               MathF.Abs(MergeAffinity - other.MergeAffinity) +
               MathF.Abs(LightDependency - other.LightDependency) +
               MathF.Abs(ChemicalAffinity - other.ChemicalAffinity) +
               MathF.Abs(PredatoryDrive - other.PredatoryDrive) +
               MathF.Abs(EnvironmentResistance - other.EnvironmentResistance);
    }
}
