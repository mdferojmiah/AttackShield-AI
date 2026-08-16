using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Persistence;
using AttackShield.Infrastructure.Persistence.Repositories;
using AttackShield.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AttackShield.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Mongo persistence, repositories and infrastructure services
    /// (AI client, streaming, auth helpers). Options are bound from config.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Options
        services.Configure<MongoOptions>(config.GetSection(MongoOptions.SectionName));
        services.Configure<AiOptions>(config.GetSection(AiOptions.SectionName));
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<StreamOptions>(config.GetSection(StreamOptions.SectionName));

        // Persistence
        services.AddSingleton<MongoContext>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthorityRepository, AuthorityRepository>();
        services.AddScoped<IDetectionRepository, DetectionRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IStatsRepository, StatsRepository>();

        // Security helpers
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IRtspUrlBuilder, RtspUrlBuilder>();

        // Streaming (singleton — owns long-lived FFmpeg processes)
        services.AddSingleton<IStreamManager, FfmpegStreamManager>();

        // AI service client
        services.AddHttpClient<IAiServiceClient, AiServiceClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
