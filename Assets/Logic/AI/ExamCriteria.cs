using System.Collections.Generic;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Qué tiene que lograr un cerebro en el examen para considerarse funcional. Umbrales fijados el 2026-10-20 con
    /// la línea base de 16 cerebros: el mejor especialista de cada escenario apenas los alcanza en el suyo
    /// (anillo 0.65, puntos 0.41, exploración 0.47) y ningún cerebro los pasaba todos.
    /// </summary>
    public static class ExamCriteria
    {
        public const float RING = 0.5f;
        public const float RANDOM_POINTS = 0.5f;
        public const float EXPLORATION = 0.4f;
        public const int MAX_CRASHED_STAGES = 2;

        /// <summary>Los criterios que el cerebro no cumple; vacío si aprueba.</summary>
        public static List<string> Failures(float ring, float randomPoints, float exploration, int crashedStages)
        {
            var failures = new List<string>();
            if (ring < RING)
                failures.Add($"ring {ring:F2} < {RING:F2}");
            if (randomPoints < RANDOM_POINTS)
                failures.Add($"random points {randomPoints:F2} < {RANDOM_POINTS:F2}");
            if (exploration < EXPLORATION)
                failures.Add($"exploration {exploration:F2} < {EXPLORATION:F2}");
            if (crashedStages > MAX_CRASHED_STAGES)
                failures.Add($"crashed in {crashedStages} stages > {MAX_CRASHED_STAGES}");
            return failures;
        }
    }
}
