using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using Serilog;
using VmManager.Agent.Auth;
using VmManager.Agent.Components;
using VmManager.Agent.Components.Auth;
using VmManager.Agent.Endpoints;
using VmManager.Agent.Hubs;
using VmManager.Agent.Services;
using VmManager.Agent.Services.Rdp;

namespace VmManager.Agent;

public static class AgentHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        Log.Information("AgentHost.RunAsync starting");

        RdpCredSspConnectionHandler? rdpHandler = null;

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();
        builder.WebHost.UseStaticWebAssets();

        int httpPort = builder.Configuration.GetValue("VmManager:HttpPort", 18275);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(
                httpPort,
                listenOptions =>
                {
                    listenOptions.Use(next =>
                        async context =>
                        {
                            System.IO.Pipelines.PipeReader input = context.Transport.Input;
                            System.IO.Pipelines.ReadResult result = await input.ReadAsync(
                                context.ConnectionClosed
                            );
                            System.Buffers.ReadOnlySequence<byte> buffer = result.Buffer;

                            if (buffer.Length > 0 && buffer.First.Span[0] == 0x03)
                            {
                                input.AdvanceTo(buffer.Start);
                                DuplexPipeStream stream = new DuplexPipeStream(
                                    input,
                                    context.Transport.Output
                                );
                                await rdpHandler!.HandleConnectionAsync(
                                    stream,
                                    context.ConnectionClosed
                                );
                                return;
                            }

                            input.AdvanceTo(buffer.Start);
                            await next(context);
                        }
                    );
                }
            );
        });

        Log.Information("AgentHost: configuring services");

        if (OperatingSystem.IsWindows())
            builder.Services.AddWindowsService();
        else if (OperatingSystem.IsLinux())
            builder.Host.UseSystemd();
        string? vmBackend = ReadVmBackendFromSettings();
        builder.Services.AddBackendServices(vmBackend);
        builder.Services.AddCatalogServices();
        builder.Services.AddAgentServices(vmBackend);

        builder
            .Services.AddAuthentication("Basic")
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);

        builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder("Basic")
                .RequireAuthenticatedUser()
                .Build();

            foreach (string permission in Permission.All)
            {
                options.AddPolicy(
                    permission,
                    policy => policy.AddRequirements(new PermissionRequirement(permission))
                );
            }
        });

        builder.Services.AddControllers().AddApplicationPart(typeof(AgentHost).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddHealthChecks();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddScoped<BasicAuthStateProvider>();
        builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<BasicAuthStateProvider>()
        );
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            string xmlFile = Path.ChangeExtension(typeof(AgentHost).Assembly.Location, ".xml");
            if (File.Exists(xmlFile))
                options.IncludeXmlComments(xmlFile);
        });
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        Log.Information("AgentHost: building app");
        WebApplication app = builder.Build();

        rdpHandler = app.Services.GetRequiredService<RdpCredSspConnectionHandler>();

        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<Auth.MustChangePasswordMiddleware>();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapControllers();
        app.MapHub<ProgressHub>("/hubs/progress");
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapRdpEndpoints();
        app.MapPrometheusEndpoints();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AllowAnonymous();

        int rdpProxyPort = builder.Configuration.GetValue("VmManager:RdpProxyPort", 13389);
        if (rdpProxyPort > 0)
        {
            RdpProxyListener rdpProxyListener = app.Services.GetRequiredService<RdpProxyListener>();
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await rdpProxyListener.StartAsync(rdpProxyPort, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "RDP proxy listener crashed");
                    }
                },
                cancellationToken
            );
        }

        Log.Information(
            "AgentHost: starting (HTTP+RDP :{HttpPort}{StandaloneRdp})",
            httpPort,
            rdpProxyPort > 0 ? ", standalone RDP :" + rdpProxyPort : ""
        );
        Task runTask = app.RunAsync();
        cancellationToken.Register(() => app.StopAsync().GetAwaiter().GetResult());
        await runTask;
    }

    private static string? ReadVmBackendFromSettings()
    {
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VmManager",
            "settings.json"
        );
        if (!File.Exists(settingsPath))
            return null;

        try
        {
            string json = File.ReadAllText(settingsPath);
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("VmBackend", out JsonElement val))
                return val.GetString();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read VmBackend from settings");
        }
        return null;
    }
}
