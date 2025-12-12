using AppCopyDirecToDrive.Services;
using System;
using System.IO;
using System.Text.Json;

public static class TokenManager
{
    private static readonly string ConfigFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "AppCopyDirecToDrive",
        "tokens.json");

    public static void SaveTokens(TokenResult tokens)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
            var json = JsonSerializer.Serialize(tokens);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            // Логування помилки
            Console.WriteLine($"Помилка збереження токенів: {ex.Message}");
        }
    }

    public static TokenResult LoadTokens()
    {
        if (!File.Exists(ConfigFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<TokenResult>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка завантаження токенів: {ex.Message}");
            return null;
        }
    }

    public static void ClearTokens()
    {
        if (File.Exists(ConfigFilePath))
        {
            File.Delete(ConfigFilePath);
        }
    }
}