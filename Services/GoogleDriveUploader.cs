using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using File = System.IO.File;

public class GoogleDriveUploader
{
    private readonly DriveService _driveService;
    private readonly string _logFilePath;

    public GoogleDriveUploader(DriveService driveService)
    {
        _driveService = driveService;
        // Лог-файл у MyDocuments
        string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _logFilePath = Path.Combine(myDocuments, "AppCopyDirecToDrive", "upload_log.txt");
    }

    public async Task UploadAllFromJsonAsync(string jsonFileName, string parentDriveFolderId = "root")
    {
        string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string jsonPath = Path.Combine(myDocuments, "AppCopyDirecToDrive", jsonFileName);

        if (!File.Exists(jsonPath))
        {
            await LogUpload($"Помилка: JSON файл не знайдено у MyDocuments: {jsonPath}", false);
            throw new FileNotFoundException("JSON файл не знайдено у MyDocuments: " + jsonPath);
        }

        string jsonContent = await File.ReadAllTextAsync(jsonPath);
        var localDirs = JsonSerializer.Deserialize<List<string>>(jsonContent);

        if (localDirs == null || localDirs.Count == 0)
        {
            await LogUpload("Помилка: Список директорій порожній або JSON некоректний.", false);
            throw new InvalidOperationException("Список директорій порожній або JSON некоректний.");
        }

        // Get or create a parent folder with the current day name
        string dayFolderName = DateTime.Now.ToString("dddd-MMM-yyyy", new System.Globalization.CultureInfo("en-US"));
        string dayFolderId = await GetOrCreateDriveFolderAsync(dayFolderName, parentDriveFolderId);

        // Create a subfolder with the current date inside the day folder
        string dateFolderName = DateTime.Now.ToString("H:mm:ss");
        string dateFolderId = await CreateDriveFolderAsync(dateFolderName, dayFolderId);

        await LogUpload($"Розпочато завантаження директорій до папки Google Drive (ID: {dateFolderId}, Ім'я: {dateFolderName})", true);

        foreach (var dir in localDirs)
        {
            if (Directory.Exists(dir))
            {
                await UploadDirectoryRecursive(dir, dateFolderId, Path.GetFileName(dir));
            }
            else
            {
                await LogUpload($"Пропущено: директорія не знайдена -> {dir}", false);
                Console.WriteLine($"Пропущено: директорія не знайдена -> {dir}");
            }
        }

        await LogUpload($"Завантаження до папки {dateFolderName} завершено.", true);
    }

    private async Task<string> GetOrCreateDriveFolderAsync(string name, string parentId)
    {
        // Check if a folder with the given name already exists in the parent folder
        var listRequest = _driveService.Files.List();
        listRequest.Q = $"name = '{name}' and mimeType = 'application/vnd.google-apps.folder' and '{parentId}' in parents and trashed = false";
        listRequest.Fields = "files(id, name)";
        var files = await listRequest.ExecuteAsync();

        if (files.Files != null && files.Files.Count > 0)
        {
            await LogUpload($"Використано існуючу папку: {name} (ID: {files.Files[0].Id})", true);
            return files.Files[0].Id; // Return the ID of the existing folder
        }

        // If no folder exists, create a new one
        return await CreateDriveFolderAsync(name, parentId);
    }

    private async Task<string> CreateDriveFolderAsync(string name, string parentId)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = new List<string> { parentId }
        };

        var request = _driveService.Files.Create(fileMetadata);
        request.Fields = "id";
        var folder = await request.ExecuteAsync();

        await LogUpload($"Створено нову папку: {name} (ID: {folder.Id})", true);
        return folder.Id;
    }

    private async Task UploadDirectoryRecursive(string currentLocalPath, string parentDriveFolderId, string folderName)
    {
        // Create folder in Google Drive with the directory name
        string driveFolderId = await CreateDriveFolderAsync(folderName, parentDriveFolderId);

        // Upload all files in this folder
        foreach (var file in Directory.GetFiles(currentLocalPath))
        {
            await UploadFileAsync(file, driveFolderId);
        }

        // Recursively process each subdirectory
        foreach (var subDir in Directory.GetDirectories(currentLocalPath))
        {
            await UploadDirectoryRecursive(subDir, driveFolderId, Path.GetFileName(subDir));
        }
    }

    private async Task UploadFileAsync(string filePath, string parentFolderId)
    {
        try
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { parentFolderId }
            };

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var request = _driveService.Files.Create(fileMetadata, stream, GetMimeType(filePath));
            request.Fields = "id";

            await request.UploadAsync();

            await LogUpload($"Успішно завантажено файл: {filePath} до папки (ID: {parentFolderId})", true);
        }
        catch (Exception ex)
        {
            await LogUpload($"Помилка при завантаженні файлу: {filePath}. Помилка: {ex.Message}", false);
            throw;
        }
    }

    private async Task LogUpload(string message, bool isSuccess)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string dayHeader = DateTime.Now.ToString("dddd, MMMM dd, yyyy", new System.Globalization.CultureInfo("en-US"));
            string status = isSuccess ? "Успіх" : "Помилка";

            // Формат запису
            string logEntry = $"[{timestamp}] [{status}] {message}";

            // Перевірка, чи існує файл і чи потрібен заголовок дня
            bool writeDayHeader = false;
            if (!File.Exists(_logFilePath))
            {
                writeDayHeader = true;
            }
            else
            {
                string lastLine = await File.ReadAllTextAsync(_logFilePath);
                if (!lastLine.Contains($"=== {dayHeader} ==="))
                {
                    writeDayHeader = true;
                }
            }

            // Дозапис у файл
            using (var writer = new StreamWriter(_logFilePath, true))
            {
                if (writeDayHeader)
                {
                    await writer.WriteLineAsync($"\n=== {dayHeader} ===");
                }
                await writer.WriteLineAsync(logEntry);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка при записі логу: {ex.Message}");
        }
    }

    private string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }
}