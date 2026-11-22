using System;
using System.Collections.Generic;
using System.Linq;

namespace AbiogenesisEngine.Core;

public class OrganismPhenotype
{
    public uint ColonyId;
    public AbiogenesisGenome OrganismGenome; // Итоговый геном вида
    public int CellCount;
    public int Age;

    public float PhotosyntheticRatio;
    public float ChemosyntheticRatio;
    public float PredatoryRatio;
    public float DefensiveRatio;
    public float SensoryRatio;
    public float ReproductiveRatio;

    public string DominantStrategy = "Universal-Conglomerate";

    // ИСПРАВЛЕНО: Сделали поле nullable (добавили ?), чтобы компилятор не выдавал варнингов
    public EvolutionaryHistory? History { get; set; }

    // ИСПРАВЛЕНО: Теперь метод принимает плоский список клеток макро-кластера и его контрольный геном!
    public static OrganismPhenotype Calculate(List<Cell> clusterCells, uint clusterId, AbiogenesisGenome controlGenome, EvolutionaryHistory history)
    {
        int avgAge = 0;

        if (clusterCells.Count > 0)
        {
            long sumAge = 0;

            for (int i = 0; i < clusterCells.Count; i++)
            {
                sumAge += clusterCells[i].Age;
            }

            avgAge = (int)(sumAge / clusterCells.Count);
        }

        var phenotype = new OrganismPhenotype
        {
            ColonyId = clusterId,
            OrganismGenome = controlGenome,
            CellCount = clusterCells.Count,
            Age = avgAge,
            History = history
        };

        if (clusterCells.Count == 0) return phenotype;

        int total = clusterCells.Count;

        int photo = 0;
        int chemo = 0;
        int pred = 0;
        int def = 0;
        int sens = 0;
        int repro = 0;

        for (int i = 0; i < clusterCells.Count; i++)
        {
            var g = clusterCells[i].Genome;

            if (g.LightDependency > 0.7f) photo++;
            if (g.ChemicalAffinity > 0.7f) chemo++;
            if (g.PredatoryDrive > 0.6f) pred++;
            if (g.EnvironmentResistance > 0.7f) def++;
            if (g.SenseRadius > 0.7f) sens++;
            if (g.DivisionThreshold < 0.5f) repro++;
        }

        phenotype.PhotosyntheticRatio = photo / (float)total;
        phenotype.ChemosyntheticRatio = chemo / (float)total;
        phenotype.PredatoryRatio = pred / (float)total;
        phenotype.DefensiveRatio = def / (float)total;
        phenotype.SensoryRatio = sens / (float)total;
        phenotype.ReproductiveRatio = repro / (float)total;

        string best = "Universal-Conglomerate";
        float bestVal = -1f;

        if (phenotype.PhotosyntheticRatio > bestVal)
        {
            bestVal = phenotype.PhotosyntheticRatio;
            best = "Photosynthetic";
        }

        if (phenotype.ChemosyntheticRatio > bestVal)
        {
            bestVal = phenotype.ChemosyntheticRatio;
            best = "Chemosynthetic";
        }

        if (phenotype.PredatoryRatio > bestVal)
        {
            bestVal = phenotype.PredatoryRatio;
            best = "Predatory";
        }

        if (phenotype.DefensiveRatio > bestVal)
        {
            bestVal = phenotype.DefensiveRatio;
            best = "Defensive";
        }

        if (phenotype.SensoryRatio > bestVal)
        {
            bestVal = phenotype.SensoryRatio;
            best = "Sensory";
        }

        if (phenotype.ReproductiveRatio > bestVal)
        {
            bestVal = phenotype.ReproductiveRatio;
            best = "Reproductive";
        }

        phenotype.DominantStrategy = best;

        return phenotype;
    }
}
