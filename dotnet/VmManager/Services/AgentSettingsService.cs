using System.Text.Json;

namespace VmManager.Services;

public class AgentSettingsService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AgentSettingsService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _filePath = Path.Combine(appPaths.AppDataDir, "agents.json");
    }

    public List<AgentConfiguration> Load()
    {
        if (!File.Exists(_filePath))
            return CreateDefault();

        try
        {
            string json = File.ReadAllText(_filePath);
            List<AgentConfiguration>? agents = JsonSerializer.Deserialize<List<AgentConfiguration>>(
                json,
                JsonOptions
            );
            if (agents == null)
                return CreateDefault();

            bool needsResave = false;
            foreach (AgentConfiguration agent in agents)
            {
                if (!string.IsNullOrEmpty(agent.Password))
                {
                    if (SecureStorage.IsProtected(agent.Password))
                    {
                        agent.Password = SecureStorage.Unprotect(agent.Password);
                    }
                    else
                    {
                        needsResave = true;
                    }
                }
            }

            if (needsResave)
                Save(agents);

            return agents;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(List<AgentConfiguration> agents)
    {
        foreach (AgentConfiguration agent in agents)
        {
            if (
                agent.IsLocal
                && !agent
                    .Url.TrimEnd('/')
                    .Equals("http://localhost:18275", StringComparison.OrdinalIgnoreCase)
            )
                agent.IsLocal = false;
        }

        List<AgentConfiguration> toSerialize = agents
            .Select(a => new AgentConfiguration
            {
                Id = a.Id,
                Name = a.Name,
                Url = a.Url,
                RdpProxyHost = a.RdpProxyHost,
                Username = a.Username,
                Password = SecureStorage.Protect(a.Password),
                IsLocal = a.IsLocal,
            })
            .ToList();

        string directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(toSerialize, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public string? LoadSelectedAgentId()
    {
        string selectedPath = _filePath + ".selected";
        if (File.Exists(selectedPath))
            return File.ReadAllText(selectedPath).Trim();
        return null;
    }

    public void SaveSelectedAgentId(string agentId)
    {
        File.WriteAllText(_filePath + ".selected", agentId);
    }

    private List<AgentConfiguration> CreateDefault()
    {
        List<AgentConfiguration> defaults = new List<AgentConfiguration>();
#if WINDOWS && !CLIENT_ONLY
        defaults.Add(
            new AgentConfiguration
            {
                Id = "local",
                Name = "Local",
                Url = "http://localhost:18275",
                IsLocal = true,
            }
        );
#endif
        Save(defaults);
        return defaults;
    }
}
