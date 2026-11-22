namespace AbiogenesisEngine.Core;

public class Nutrient
{
    public float X, Y;
    public float Amount;
    public NutrientType Type;
    public bool IsDepleted => Amount <= 0;
}

public enum NutrientType
{
    OrganicSludge,    // Органика (для OrganicAffinity)
    MineralSalt,      // Минералы (для ChemicalAffinity)
    MeatScraps
}