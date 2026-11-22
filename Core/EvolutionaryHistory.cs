using System;
using System.Collections.Generic;

namespace AbiogenesisEngine.Core;

public class EvolutionaryHistory
{
    public uint OrganismId;
    public List<AncestorRecord> Ancestors = new();
    public List<EvolutionaryEvent> KeyEvents = new();
    public int TotalGenerations;
    public int FirstAppearanceTick;
    public int LastUpdateTick;
}

public class AncestorRecord
{
    public int Generation;
    public AbiogenesisGenome Genome;
    public float EnvironmentDepth; // 0 = поверхность, 1 = глубина
    public int Tick;
    public string Note;
}

public class EvolutionaryEvent
{
    public int Tick;
    public string EventType;
    public string Description;
}