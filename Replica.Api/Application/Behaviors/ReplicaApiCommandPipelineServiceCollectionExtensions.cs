using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Replica.Api.Application.Abstractions;
using Replica.Api.Infrastructure;

namespace Replica.Api.Application.Behaviors;

public static class ReplicaApiCommandPipelineServiceCollectionExtensions
{
    public static IServiceCollection AddReplicaApiCommandPipeline(this IServiceCollection services)
    {
        services.AddOptions<ReplicaApiCommandPipelineOptions>();
        services.TryAddSingleton<IReplicaOrderPushPublisher, NoOpReplicaOrderPushPublisher>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ReplicaApiCommandValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ReplicaApiCommandIdempotencyBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ReplicaApiCommandTransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ReplicaApiPushNotificationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ReplicaApiCommandTelemetryBehavior<,>));

        RegisterValidatorsFromAssembly(services, typeof(ReplicaApiCommandPipelineServiceCollectionExtensions).Assembly);
        return services;
    }

    private static void RegisterValidatorsFromAssembly(IServiceCollection services, Assembly assembly)
    {
        var validatorOpenGenericType = typeof(IReplicaApiCommandValidator<>);
        var registrations = assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType
                                    && candidate.GetGenericTypeDefinition() == validatorOpenGenericType)
                .Select(serviceType => new { serviceType, implementationType = type }))
            .ToList();

        foreach (var registration in registrations)
            services.AddTransient(registration.serviceType, registration.implementationType);
    }
}
