namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The type of growing structure. Hydroponic structures grow plants in a nutrient
    /// water solution, and Fodder structures grow sprouted feed for livestock. The type
    /// decides which dashboard and sensor cards are shown for the structure.
    /// </summary>
    public enum StructureType
    {
        Hydroponic,
        Fodder
    }
}