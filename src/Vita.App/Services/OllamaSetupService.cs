using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Vita.App.Services;

public sealed record OllamaProbe(
    bool Installed,
    string? ExecutablePath,
    bool Running,
    string? Version,
    IReadOnlyList<string> Models,
    string? Error)
{
    public bool HasModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        static string Normalize(string value)
        {
            var name = value.Trim().ToLowerInvariant();
            return name.EndsWith(":latest", StringComparison.Ordinal) ? name[..^7] : name;
        }

        var wanted = Normalize(model);
        return Models.Any(m =>
        {
            var installed = Normalize(m);
            return installed == wanted ||
                   installed.StartsWith(wanted + ":", StringComparison.Ordinal) ||
                   wanted.StartsWith(installed + ":", StringComparison.Ordinal);
        });
    }
}

public sealed record AiRequirements(
    double RamGb,
    double FreeDiskGb,
    string RecommendedModel,
    string RecommendedModelLabel,
    double RecommendedModelGb,
    string Summary);

// Everything needed to make Ollama usable on a Windows machine: find the
// executable, start the service, install with winget, pull a model and report
// what hardware can handle. The UI decides when (and whether) to call these.
public sealed class OllamaSetupService(IHttpClientFactory httpFactory, IConfiguration configuration)
{
    public string DefaultBaseUrl =>
        AiSettingsService.NormalizeBaseUrl(AiProviderCatalog.OllamaLocal, configuration["Ai:LocalBaseUrl"]);

    public string? FindExecutable()
    {
        var overridePath = configuration["Ai:OllamaPath"];
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("where.exe", "ollama")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is not null)
            {
                var path = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .FirstOrDefault(File.Exists);
                process.WaitForExit(3000);
                if (path is not null) return path;
            }
        }
        catch
        {
            // Not on PATH; the fixed locations above cover the normal installer.
        }

        return null;
    }

    public async Task<OllamaProbe> ProbeAsync(string? baseUrl, CancellationToken ct = default)
    {
        var url = NormalizeLocalUrl(baseUrl);
        var executable = FindExecutable();

        try
        {
            using var http = httpFactory.CreateClient("ai");
            http.Timeout = TimeSpan.FromSeconds(4);
            using var versionResponse = await http.GetAsync($"{url}/api/version", ct);
            if (!versionResponse.IsSuccessStatusCode)
            {
                return new OllamaProbe(executable is not null, executable, false, null, Array.Empty<string>(),
                    $"O serviço respondeu com HTTP {(int)versionResponse.StatusCode}. Confirme se a URL é de um Ollama.");
            }

            string? version = null;
            try
            {
                using var versionDoc = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync(ct));
                if (versionDoc.RootElement.TryGetProperty("version", out var v)) version = v.GetString();
            }
            catch (JsonException)
            {
                // Version is optional.
            }

            var models = new List<string>();
            try
            {
                var tags = await http.GetFromJsonAsync<JsonElement>($"{url}/api/tags", ct);
                if (tags.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                            models.Add(n);
                    }
                }
            }
            catch
            {
                // Running is what matters; an empty model list just prompts a pull.
            }

            return new OllamaProbe(executable is not null, executable, true, version, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var error = executable is null
                ? "O Ollama não está instalado nesta máquina."
                : "O Ollama está instalado, mas o serviço não está rodando.";
            if (ct.IsCancellationRequested) error = "Verificação cancelada.";
            return new OllamaProbe(executable is not null, executable, false, null, Array.Empty<string>(), error);
        }
    }

    public async Task<(bool Ok, string Message)> StartAsync(string? baseUrl, CancellationToken ct = default)
    {
        var current = await ProbeAsync(baseUrl, ct);
        if (current.Running) return (true, "O Ollama já está rodando.");

        var executable = current.ExecutablePath ?? FindExecutable();
        if (executable is null)
            return (false, "O Ollama não está instalado. Use “Instalar Ollama” ou baixe em ollama.com/download.");

        try
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };
            info.ArgumentList.Add("serve");

            var uri = new Uri(NormalizeLocalUrl(baseUrl));
            info.Environment["OLLAMA_HOST"] = $"{uri.Host}:{uri.Port}";

            using var process = Process.Start(info);
            if (process is null) return (false, "Não foi possível iniciar o processo do Ollama.");

            var log = new List<string>();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) { log.Add(e.Data); if (log.Count > 20) log.RemoveAt(0); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) { log.Add(e.Data); if (log.Count > 20) log.RemoveAt(0); } };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(500, ct);
                if (process.HasExited) break;
                var probe = await ProbeAsync(baseUrl, ct);
                if (probe.Running) return (true, "Ollama iniciado. O serviço está respondendo.");
            }

            var detail = string.Empty;
            lock (log) detail = string.Join(" ", log.TakeLast(3));
            return (false, process.HasExited
                ? $"O Ollama encerrou ao iniciar. {TextAnalysis.Truncate(detail, 220)}"
                : "O Ollama foi iniciado, mas ainda não respondeu. Tente verificar novamente em alguns segundos.");
        }
        catch (OperationCanceledException)
        {
            return (false, "A inicialização foi cancelada.");
        }
        catch (Exception ex)
        {
            return (false, $"Não foi possível iniciar o Ollama: {ex.Message}");
        }
    }

    public async Task<(bool Ok, string Message)> PullModelAsync(
        string? baseUrl, string model, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model)) return (false, "Informe o nome do modelo antes de baixar.");

        var executable = FindExecutable();
        if (executable is null)
            return (false, "O Ollama não está instalado. Instale primeiro e depois baixe o modelo.");

        try
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };
            info.ArgumentList.Add("pull");
            info.ArgumentList.Add(model.Trim());

            var uri = new Uri(NormalizeLocalUrl(baseUrl));
            info.Environment["OLLAMA_HOST"] = $"{uri.Host}:{uri.Port}";

            using var process = Process.Start(info);
            if (process is null) return (false, "Não foi possível iniciar o download do modelo.");

            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0
                ? (true, $"Modelo {model} disponível.")
                : (false, $"O download do modelo terminou com código {process.ExitCode}. Veja o log acima.");
        }
        catch (OperationCanceledException)
        {
            return (false, "O download do modelo foi cancelado.");
        }
        catch (Exception ex)
        {
            return (false, $"Não foi possível baixar o modelo: {ex.Message}");
        }
    }

    public async Task<(bool Ok, string Message)> InstallAsync(Action<string>? onProgress = null, CancellationToken ct = default)
    {
        try
        {
            var info = new ProcessStartInfo("winget.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("install");
            info.ArgumentList.Add("--id");
            info.ArgumentList.Add("Ollama.Ollama");
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add("--silent");
            info.ArgumentList.Add("--accept-package-agreements");
            info.ArgumentList.Add("--accept-source-agreements");

            using var process = Process.Start(info);
            if (process is null)
                return (false, "Não foi possível abrir o winget. Baixe o instalador em ollama.com/download.");

            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0
                ? (true, "Instalação concluída pelo winget.")
                : (false, $"O winget terminou com código {process.ExitCode}. Se a instalação não aparecer, use o site oficial.");
        }
        catch (OperationCanceledException)
        {
            return (false, "A instalação foi cancelada.");
        }
        catch (Exception ex)
        {
            return (false, $"Não foi possível instalar automaticamente ({ex.Message}). Baixe o instalador em ollama.com/download.");
        }
    }

    public bool OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ollama.com/download/windows") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AiRequirements GetRequirements()
    {
        var ramGb = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d, 1);
        var freeGb = 0d;
        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady) freeGb = Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 1);
            }
        }
        catch
        {
            // Disk info is only a recommendation hint.
        }

        var (model, label, size) = freeGb is > 0 and < 6
            ? ("llama3.2:1b", "Llama 3.2 1B", 1.3)
            : ramGb >= 14
                ? ("llama3.1", "Llama 3.1 8B", 4.9)
                : ramGb >= 8
                    ? ("llama3.2:3b", "Llama 3.2 3B", 2.0)
                    : ("llama3.2:1b", "Llama 3.2 1B", 1.3);

        var summary = $"Seu computador tem {ramGb:0.#} GB de RAM e {freeGb:0.#} GB livres em disco. " +
                      $"Recomendado: {label} (~{size:0.#} GB). Você pode escolher outro modelo quando quiser.";

        return new AiRequirements(ramGb, freeGb, model, label, size, summary);
    }

    private static string NormalizeLocalUrl(string? baseUrl) =>
        AiSettingsService.NormalizeBaseUrl(AiProviderCatalog.OllamaLocal, baseUrl);
}
