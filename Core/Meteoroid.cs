namespace AbiogenesisEngine.Core;

public class Meteoroid
{
    public float X;
    public float Y;
    public float VX;
    public float VY;
    public float TargetY;
    public bool Exploded;
    public int ImpactFlash;

    // Параметры удара — задаются при спавне в зависимости от класса
    public float ImpactForce;    // 0.3 / 0.6 / 1.0 — сила удара
    public float LethalRadius;   // радиус зоны смерти (px)
    public float WaveRadius;     // радиус ударной волны (px)
    public string SizeClass;     // "S" / "M" / "L" для логов
}