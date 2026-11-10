using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace manager;

public sealed class DockerRuntime : IRoomRuntime
{
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
            await EnsureImageAsync();
            await CreateAsync(room);

        await client.Containers.StartContainerAsync(CName(room), new ContainerStartParameters());
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

        await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = image,
            Name = CName(room),
            Env = new List<string>
            {
                "ROOM_DATA=/data",
                "ROOM_PORT=" + room.Port,
                "ROOM_NAME=" + room.Name,
                "ROOM_OWNER=" + room.Owner
            },
            Labels = new Dictionary<string, string> { ["areschat.room"] = room.Id },
            ExposedPorts = new Dictionary<string, EmptyStruct> { [tcp] = default, [udp] = default },
            HostConfig = new HostConfig
            {
                Binds = new List<string> { VName(room) + ":/data" },
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

    private sealed class Collector : IProgress<string>
    {
        public readonly StringBuilder Text = new();
        public void Report(string value) => Text.Append(value);
    }

    public async Task<string> LogsAsync(Room room, int tail)
    {
        var collector = new Collector();

        await client.Containers.GetContainerLogsAsync(
            CName(room),
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = tail.ToString() },
            CancellationToken.None,
            collector);

        return collector.Text.ToString();
    }
}