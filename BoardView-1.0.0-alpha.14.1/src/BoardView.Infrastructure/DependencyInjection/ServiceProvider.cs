namespace BoardView.Infrastructure.DependencyInjection;

/// <summary>Resuelve servicios registrados y libera los objetos desechables creados.</summary>
public sealed class ServiceProvider : IDisposable
{
    private readonly IReadOnlyDictionary<Type, Func<ServiceProvider, object>> factories;
    private readonly HashSet<IDisposable> disposables = [];
    private bool isDisposed;

    internal ServiceProvider(IReadOnlyDictionary<Type, Func<ServiceProvider, object>> factories)
    {
        this.factories = new Dictionary<Type, Func<ServiceProvider, object>>(factories);
    }

    public TService GetRequiredService<TService>() where TService : class
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!factories.TryGetValue(typeof(TService), out Func<ServiceProvider, object>? factory))
        {
            throw new InvalidOperationException($"El servicio '{typeof(TService).FullName}' no está registrado.");
        }

        object service = factory(this);
        if (service is IDisposable disposable)
        {
            disposables.Add(disposable);
        }

        return (TService)service;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        foreach (IDisposable disposable in disposables.Reverse())
        {
            disposable.Dispose();
        }

        disposables.Clear();
        isDisposed = true;
    }
}
