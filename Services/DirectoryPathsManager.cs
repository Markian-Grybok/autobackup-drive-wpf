using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class DirectoryPathsManager
{
    private readonly string _filePath;

    public DirectoryPathsManager(string fileName = "directory_paths.json")
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _filePath = Path.Combine(appDataPath, "AppCopyDirecToDrive", fileName);
    }

    public List<string> LoadPaths()
    {
        if (!File.Exists(_filePath)) 
            return new List<string>();

        string json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    public void SavePaths(List<string> paths)
    {
        string json = JsonSerializer.Serialize(paths);
        File.WriteAllText(_filePath, json);
    }
}