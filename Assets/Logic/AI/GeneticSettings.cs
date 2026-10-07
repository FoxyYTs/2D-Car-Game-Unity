namespace Assets.Logic.AI
{
    public class GeneticSettings
    {
        public int PopulationSize { get; set; } = 50;

        /// <summary>Los mejores que pasan sin cambios a la siguiente generación.</summary>
        public int EliteCount { get; set; } = 2;

        /// <summary>Probabilidad de que cada gen mute.</summary>
        public float MutationRate { get; set; } = 0.1f;

        /// <summary>Desviación estándar del ruido gaussiano que se suma al gen que muta.</summary>
        public float MutationSigma { get; set; } = 0.3f;

        /// <summary>Los genes de la población inicial se sortean uniformemente en [-InitialRange, InitialRange].</summary>
        public float InitialRange { get; set; } = 1f;
    }
}
