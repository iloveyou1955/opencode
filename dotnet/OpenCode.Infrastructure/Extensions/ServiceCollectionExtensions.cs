using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 自动扫描并注册带有 ServiceRegistration 特性的服务
        /// </summary>
        public static IServiceCollection AddAutoRegisteredServices(this IServiceCollection services, params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<ServiceRegistrationAttribute>() != null);

                foreach (var type in types)
                {
                    var attrs = type.GetCustomAttributes<ServiceRegistrationAttribute>();

                    foreach (var attr in attrs)
                    {
                        var serviceType = attr.ServiceType ?? type;

                        switch (attr.Lifetime)
                        {
                            case ServiceLifetime.Singleton:
                                services.AddSingleton(serviceType, type);
                                break;
                            case ServiceLifetime.Scoped:
                                services.AddScoped(serviceType, type);
                                break;
                            case ServiceLifetime.Transient:
                                services.AddTransient(serviceType, type);
                                break;
                        }
                    }
                }
            }

            return services;
        }
    }
}
