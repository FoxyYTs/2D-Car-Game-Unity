using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// The brains saved by the trainers in Application.persistentDataPath, newest first.
public static class BrainLibrary
{
    public class Entry
    {
        public string File;
        public TrainingManager.SavedBrain Brain;
        public DateTime Saved;

        public bool IsVersus => File.StartsWith(VersusTrainingManager.BRAIN_FILE_PREFIX);

        private string Kind => IsVersus ? "Versus"
                             : Brain.Course == nameof(TrainingManager.CourseKind.RandomPoints) ? "Puntos al azar"
                             : Brain.Course == nameof(TrainingManager.CourseKind.Exploration) ? "Exploración"
                             : Brain.Course == "Curriculum" ? "Currículo"
                             : "Circuito";

        public string Label =>
            $"{Kind} · semilla {Brain.Seed} · gen {Brain.Generation} · " +
            $"fitness {Brain.Fitness:F2} · {Saved:dd MMM HH:mm}" + ScoresText;

        // Validation score per environment, for brains trained by the curriculum.
        private string ScoresText => Brain.Scores is { Length: > 0 }
                                         ? " · " + string.Join(" ", Brain.Scores.Select(score => $"{Short(score.Name)} {score.Score:F2}"))
                                         : "";

        private static string Short(string environment) => environment switch
        {
            nameof(TrainingManager.CourseKind.Ring) => "anillo",
            nameof(TrainingManager.CourseKind.RandomPoints) => "puntos",
            _ => "explor."
        };
    }

    public static List<Entry> List()
    {
        var entries = new List<Entry>();
        var folder = new DirectoryInfo(Application.persistentDataPath);
        if (!folder.Exists)
            return entries;

        foreach (var file in folder.GetFiles("*.json"))
        {
            if (!file.Name.StartsWith(TrainingManager.BRAIN_FILE_PREFIX) &&
                !file.Name.StartsWith(VersusTrainingManager.BRAIN_FILE_PREFIX))
                continue;

            try
            {
                var brain = JsonUtility.FromJson<TrainingManager.SavedBrain>(File.ReadAllText(file.FullName));
                if (brain?.Layers is { Length: >= 2 } && brain.Genome != null)
                    entries.Add(new Entry { File = file.Name, Brain = brain, Saved = file.LastWriteTime });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Skipping {file.Name}: {exception.Message}");
            }
        }

        return entries.OrderByDescending(entry => entry.Saved).ToList();
    }
}
