using System;
using System.Threading.RateLimiting;
using HonzaBotner.Database;
using HonzaBotner.Discord;
using HonzaBotner.Discord.EventHandler;
using HonzaBotner.Discord.Managers;
using HonzaBotner.Discord.Services;
using HonzaBotner.Discord.Services.Commands;
using HonzaBotner.Discord.Services.EventHandlers;
using HonzaBotner.Discord.Services.Jobs;
using HonzaBotner.Discord.Services.Managers;
using HonzaBotner.Discord.Services.Utils;
using HonzaBotner.Discord.Utils;
using HonzaBotner.Scheduler;
using HonzaBotner.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;
namespace HonzaBotner;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        if (Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                // Safe only when an edge proxy is the application's sole ingress and overwrites these headers.
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        services.AddDatabaseDeveloperPageExceptionFilter();
        services.AddControllers();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        string connectionString = PsqlConnectionStringParser.GetEFConnectionString(Configuration["DATABASE_URL"]);
        ulong? guildId = Configuration.GetSection("Discord").GetValue<ulong>("GuildId");

        services
            .AddDbContext<HonzaBotnerDbContext>(options =>
                options.UseNpgsql(connectionString, b => b.MigrationsAssembly("HonzaBotner"))
            )

            // Swagger
            .AddSwaggerGen(c => { c.SwaggerDoc("v1", new OpenApiInfo { Title = "HonzaBotner", Version = "v1" }); })

            // Botner
            .AddBotnerServicesOptions(Configuration)
            .AddHttpClient()
            .AddBotnerServices()

            // Discord
            .AddDiscordOptions(Configuration)
            .AddCommandOptions(Configuration)
            .AddDiscordBot(reactions =>
                {
                    reactions
                        .AddEventHandler<BoosterHandler>()
                        .AddEventHandler<EmojiCounterHandler>()
                        .AddEventHandler<HornyJailHandler>()
                        .AddEventHandler<PinHandler>()
                        .AddEventHandler<ReminderReactionsHandler>()
                        .AddEventHandler<RoleBindingsHandler>(EventHandlerPriority.High)
                        .AddEventHandler<StaffVerificationEventHandler>(EventHandlerPriority.Urgent)
                        .AddEventHandler<VerificationEventHandler>(EventHandlerPriority.Urgent)
                        .AddEventHandler<VoiceHandler>()
                        // .AddEventHandler<BadgeRoleHandler>()
                        .AddEventHandler<ThreadHandler>()
                        .AddEventHandler<StandupButtonHandler>()
                        .AddEventHandler<StickerCounterService>()
                        ;
                }, commands =>
                {
                    commands.RegisterCommands<BotCommands>();
                    commands.RegisterCommands<EmoteCommands>(guildId);
                    commands.RegisterCommands<FunCommands>();
                    commands.RegisterCommands<MemberCommands>(guildId);
                    commands.RegisterCommands<MessageCommands>(guildId);
                    commands.RegisterCommands<ModerationCommands>(guildId);
                    commands.RegisterCommands<PinCommands>(guildId);
                    commands.RegisterCommands<PollCommands>(guildId);
                    commands.RegisterCommands<ReminderCommands>(guildId);
                    commands.RegisterCommands<VoiceCommands>(guildId);
                    commands.RegisterCommands<VerificationCommands>(guildId);
                    commands.RegisterCommands<NewsManagementCommands>(guildId);
                }
            )

            // Utils
            .AddTransient<ITranslation, Translation>()

            // Managers
            .AddTransient<IVoiceManager, VoiceManager>()
            .AddTransient<IReminderManager, ReminderManager>()
            .AddTransient<IButtonManager, ButtonManager>()
            ;

        services.AddScheduler(5000)
            .AddScopedCronJob<TriggerRemindersJobProvider>()
            .AddScopedCronJob<StandUpJobProvider>()
            .AddScopedCronJob<NewsJobProvider>()
            .AddScopedCronJob<ExpireStaffRolesJobProvider>()
            ;
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
        {
            app.UseForwardedHeaders();
        }

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(delegate (SwaggerUIOptions c)
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "HonzaBotner v1");
                c.RoutePrefix = string.Empty;
            });
        }
        else
        {
            if (Configuration.GetValue<bool>("Database:RunMigrationsOnStartup")) UpdateDatabase(app);
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/Auth") ||
                context.Request.Path.StartsWithSegments("/error"))
            {
                context.Response.Headers.ContentSecurityPolicy =
                    "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }

    private static void UpdateDatabase(IApplicationBuilder app)
    {
        using IServiceScope serviceScope = app.ApplicationServices
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();

        using HonzaBotnerDbContext? context = serviceScope.ServiceProvider.GetService<HonzaBotnerDbContext>();
        context?.Database.Migrate();
    }
}
