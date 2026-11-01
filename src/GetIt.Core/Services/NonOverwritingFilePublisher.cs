namespace GetIt_App.Services;

public static class NonOverwritingFilePublisher
{
    public static string Move(string sourcePath, string destinationPath, bool createAlternative = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("The destination must include a directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileNameWithoutExtension(fullDestinationPath);
        var extension = Path.GetExtension(fullDestinationPath);
        for (var suffix = 0; suffix <= (createAlternative ? 9999 : 0); suffix++)
        {
            var candidate = suffix == 0 || !createAlternative
                ? fullDestinationPath
                : Path.Combine(directory, $"{fileName} ({suffix}){extension}");
            try
            {
                File.Move(fullSourcePath, candidate);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                if (!createAlternative)
                    throw new IOException($"Arquivo já existe e não será sobrescrito: {candidate}");
            }
        }

        throw new IOException("Não foi possível encontrar um nome disponível para salvar outra cópia.");
    }
}
