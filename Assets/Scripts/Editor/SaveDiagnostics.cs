using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bezi;
using UnityEngine;

/// <summary>
/// Read-only editor diagnostics for the save system. Dumps save file listing
/// and contents from Application.persistentDataPath/saves so save-loss bugs
/// can be diagnosed without leaving the editor.
/// </summary>
public static class SaveDiagnostics
{
    private static string SavesDir => Path.Combine(Application.persistentDataPath, "saves");

    [BeziAction(
        "List all files in the saves folder with size and last write time.",
        IsReadOnly = true
    )]
    public static string ListSaveFiles()
    {
        var sb = new StringBuilder();
        sb.AppendLine("persistentDataPath: " + Application.persistentDataPath);
        if (!Directory.Exists(SavesDir))
            return sb.AppendLine("saves directory does not exist").ToString();

        foreach (var f in Directory.GetFiles(SavesDir).OrderBy(f => f))
        {
            var fi = new FileInfo(f);
            sb.AppendLine($"{fi.Name}  size={fi.Length}  modified={fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        return sb.ToString();
    }

    [BeziAction(
        "Return the full JSON content of a save file by file name (e.g. slot_0.json).",
        IsReadOnly = true
    )]
    public static string ReadSaveFile(string fileName)
    {
        if (fileName.Contains("..") || Path.IsPathRooted(fileName))
            throw new ArgumentException("File name must be a plain file name inside the saves folder.");
        string path = Path.Combine(SavesDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Save file not found: {path}");
        return File.ReadAllText(path);
    }

    [BeziAction(
        "Return the tail of the Unity Editor log file, optionally filtered by a substring.",
        IsReadOnly = true
    )]
    public static string ReadEditorLog(string filter, int maxLines)
    {
        string logPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData)
                         + "/Unity/Editor/Editor.log";
        if (!File.Exists(logPath))
            throw new FileNotFoundException($"Editor log not found: {logPath}");

        var lines = new List<string>();
        using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (string.IsNullOrEmpty(filter) || line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    lines.Add(line);
            }
        }

        if (lines.Count <= maxLines)
            return string.Join("\n", lines);
        return "…(" + (lines.Count - maxLines) + " earlier lines omitted)\n" + string.Join("\n", lines.Skip(lines.Count - maxLines));
    }
}
