using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace manager;

public sealed class DockerRuntime : IRoomRuntime
{
    private const string NetworkName = "areschat-net";
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly DockerClient client;
    private readonly string image;

    public DockerRuntime(string image, string? endpoint)
    {
        var uri = new Uri(!string.IsNullOrEmpty(endpoint)
            ? endpoint
            : OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");

        client = new DockerClientConfiguration(uri).CreateClient();
        this.image = image;
    }

    private static string CName(Room r) => "areschat-room-" + r.Id;
    private static string VName(Room r) => "areschat-room-data-" + r.Id;
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    public async Task<string> StatusAsync(Room room)
    {
        try
        {
            var info = await client.Containers.InspectContainerAsync(CName(room));
            return info.State.Status;
        }
        catch (DockerContainerNotFoundException)
        {
            return "sin crear";
        }
    }

    public async Task StartAsync(Room room)
    {
        if (await StatusAsync(room) == "sin crear")
        {
            await EnsureImageAsync();
            await EnsureNetworkAsync();
            await CreateAsync(room);
        }

        await client.Containers.StartContainerAsync(CName(room), new ContainerStartParameters());
    }

    private async Task EnsureNetworkAsync()
    {
        var networks = await client.Networks.ListNetworksAsync(new NetworksListParameters());

        if (networks.Any(n => n.Name == NetworkName))
            return;

        await client.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = NetworkName, Driver = "bridge" });
    }

    private async Task EnsureImageAsync()
    {
        try
        {
            await client.Images.InspectImageAsync(image);
            return;
        }
        catch (DockerImageNotFoundException) { }

        int colon = image.LastIndexOf(':');
        int slash = image.LastIndexOf('/');
        string repo = colon > slash ? image[..colon] : image;
        string tag = colon > slash ? image[(colon + 1)..] : "latest";

        string? user = Environment.GetEnvironmentVariable("REGISTRY_USER");
        string? token = Environment.GetEnvironmentVariable("REGISTRY_TOKEN");
        var auth = new AuthConfig();

        if (!string.IsNullOrEmpty(token))
        {
            auth.Username = user;
            auth.Password = token;
        }

        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repo, Tag = tag },
            auth,
            new Progress<JSONMessage>());
    }

    private async Task CreateAsync(Room room)
    {
        string tcp = room.Port + "/tcp";
        string udp = room.Port + "/udp";

        var env = new List<string>
        {
            "ROOM_DATA=/data",
            "ROOM_PORT=" + room.Port,
            "ROOM_NAME=" + room.Name,
            "ROOM_OWNER=" + room.Owner
        };

        if (!string.IsNullOrEmpty(room.AdminToken))
        {
            env.Add("ROOM_ADMIN_TOKEN=" + room.AdminToken);
            env.Add("ROOM_ADMIN_URL=http://0.0.0.0:8081");
        }

        await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = image,
            Name = CName(room),
            Env = env,
            Labels = new Dictionary<string, string> { ["areschat.room"] = room.Id },
            ExposedPorts = new Dictionary<string, EmptyStruct> { [tcp] = default, [udp] = default },
            HostConfig = new HostConfig
            {
                Binds = new List<string> { VName(room) + ":/data" },
                NetworkMode = NetworkName,
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [tcp] = new List<PortBinding> { new PortBinding { HostPort = room.Port.ToString() } },
                    [udp] = new List<PortBinding> { new PortBinding { HostPort = room.Port.ToString() } }
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            }
        });
    }

    public async Task StopAsync(Room room)
    {
        try
        {
            await client.Containers.StopContainerAsync(CName(room), new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
        }
        catch (DockerContainerNotFoundException) { }
    }

    public async Task RemoveAsync(Room room, bool purgeData)
    {
        await StopAsync(room);

        try
        {
            await client.Containers.RemoveContainerAsync(CName(room), new ContainerRemoveParameters { Force = true });
        }
        catch (DockerContainerNotFoundException) { }

        if (purgeData)
        {
            try { await client.Volumes.RemoveAsync(VName(room), true); }
            catch (DockerApiException) { }
        }
    }

    public async Task<string> LogsAsync(Room room, int tail)
    {
        var parameters = new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = tail.ToString() };
        var info = await client.Containers.InspectContainerAsync(CName(room));

        // Sin TTY, Docker mezcla stdout y stderr en un flujo con una cabecera de 8 bytes por bloque.
        using var stream = await client.Containers.GetContainerLogsAsync(CName(room), info.Config.Tty, parameters, CancellationToken.None);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(CancellationToken.None);
        return stdout + stderr;
    }

    public async Task<(int Status, string Body)> AdminAsync(Room room, string method, string path, string? body)
    {
        if (string.IsNullOrEmpty(room.AdminToken))
            return (409, Err("Esta sala se creo sin API de administracion. Eliminala y crea una nueva."));

        string ip;

        try
        {
            var info = await client.Containers.InspectContainerAsync(CName(room));

            if (!info.State.Running)
                return (409, Err("La sala esta detenida."));

            if (!info.NetworkSettings.Networks.TryGetValue(NetworkName, out var net) || string.IsNullOrEmpty(net.IPAddress))
                return (409, Err("La sala no esta en la red de administracion. Eliminala y crea una nueva."));

            ip = net.IPAddress;
        }
        catch (DockerContainerNotFoundException)
        {
            return (404, Err("La sala no tiene contenedor. Inicia la sala primero."));
        }

        using var req = new HttpRequestMessage(new HttpMethod(method), "http://" + ip + ":8081/admin/" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", room.AdminToken);

        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await http.SendAsync(req);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException e)
        {
            return (502, Err("No se pudo hablar con la sala: " + e.Message));
        }
        catch (TaskCanceledException)
        {
            return (504, Err("La sala no respondio a tiempo."));
        }
    }
}