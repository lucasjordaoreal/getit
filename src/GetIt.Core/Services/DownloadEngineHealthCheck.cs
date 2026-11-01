using System.Diagnostics;
using System.ComponentModel;
using System.Security;

namespace GetIt_App.Services;

public sealed record DownloadEngineIssue(string Component, string Path, string Reason);

public static class DownloadEngineHealthCheck
{
    public static async Task<DownloadEngineIssue?> CheckExecutableAsync(
        string component,
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(component, path, $"o caminho informado é inválido: {exception.Message}");
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (FileNotFoundException)
        {
            return new(component, fullPath, "o arquivo não foi encontrado");
        }
        catch (DirectoryNotFoundException)
        {
            return new(component, fullPath, "a pasta do executável não foi encontrada");
        }
        catch (UnauthorizedAccessException)
        {
            return new(component, fullPath, "o acesso ao arquivo foi negado");
        }
        catch (SecurityException)
        {
            return new(component, fullPath, "o acesso ao arquivo foi negado");
        }
        catch (IOException exception)
        {
            return new(component, fullPath, $"o arquivo não está acessível: {exception.Message}");
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return new(component, fullPath, "o caminho aponta para uma pasta, não para um executável");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fullPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)!
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                return new(component, fullPath, "o Windows não conseguiu iniciar o executável");
            }

            using var registration = timeout.Token.Register(() => TryTerminate(process));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0)
            {
                var detail = errorTask.Result.Trim();
                if (detail.Length > 240)
                {
                    detail = detail[..240];
                }

                return new(component, fullPath, string.IsNullOrWhiteSpace(detail)
                    ? $"o executável encerrou com código {process.ExitCode}"
                    : $"o executável encerrou com código {process.ExitCode}: {detail}");
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return new(component, fullPath, "o executável não respondeu ao teste de inicialização em 5 segundos");
        }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException or BadImageFormatException)
        {
            TryTerminate(process);
            return new(component, fullPath, $"não foi possível iniciar o executável: {exception.Message}");
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
