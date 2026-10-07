using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.Game
{
    public static class GameGoal
    {
        private static List<(string id, Collisions collisions)> collidableObjects = new();

        public static string Winner = null;
        // Name of the winning car; null when the match ended in a tie (or has not ended).
        public static string WinnerName = null;

        public static void ResetResult()
        {
            Winner = null;
            WinnerName = null;
        }

        public static void RegisterCollidable(string id, Collisions collisions)
        {
            collidableObjects.RemoveAll(p => p.id == id);
            collidableObjects.Add((id, collisions));
        }

        internal static void EvaluateCollisions(Collisions collided, string name)
        {
            if (collided.OntoMoving && !collided.By)
            {
                WinnerName = name;
                Winner = $"The {name} car won!";
            }
            else if (collided.OntoMoving && collided.By)
            {
                WinnerName = null;
                Winner = "The game tied!";
            }
            else if (collided.OntoStatic)
            {
                WinnerName = collidableObjects.Where(p => name != p.id).FirstOrDefault().id;
                Winner = $"The {WinnerName} car won!";
            }
        }
    }
}
