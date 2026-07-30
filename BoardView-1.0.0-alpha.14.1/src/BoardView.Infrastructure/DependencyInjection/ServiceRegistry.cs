namespace BoardView.Infrastructure.DependencyInjection;

/// <summary>Registro mínimo de servicios para mantener la composición explícita y sin paquetes externos.</summary>
public sealed class ServiceRegistry
{
    private readonly Dictionary<Type, Func<ServiceProvider, object>> factories = new();

    public ServiceRegistry AddSingleton<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        factories[typeof(TService)] = _ => instance;
        return this;
    }

    public ServiceRegistry AddSingleton<TService>(Func<ServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        TService? instance = null;
        factories[typeof(TService)] = provider => instance ??= factory(provider);
        return this;
    }

    public ServiceRegistry AddTransient<TService>(Func<ServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        factories[typeof(TService)] = provider => factory(provider);
        return this;
    }

    public ServiceProvider Build() => new(factories);
}
