using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Attributes
{
    /// <summary>
    /// 用于自动注册服务的特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ServiceRegistrationAttribute : Attribute
    {
        /// <summary>
        /// 服务的生命周期
        /// </summary>
        public ServiceLifetime Lifetime { get; }

        /// <summary>
        /// 注册为的接口类型（可选）
        /// </summary>
        public Type? ServiceType { get; }

        public ServiceRegistrationAttribute(ServiceLifetime lifetime = ServiceLifetime.Singleton, Type? serviceType = null)
        {
            Lifetime = lifetime;
            ServiceType = serviceType;
        }
    }
}
