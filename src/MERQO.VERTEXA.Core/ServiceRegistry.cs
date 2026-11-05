namespace Merqo.Vertexa.Core;

/// <summary>
/// Deliberately tiny composition root (≈60 lines) so the product has dependency
/// injection without pulling in a container package. Supports singletons by instance
/// or factory; resolution is explicit and constructor-free (no reflection).
/// </summary>
public sealed class ServiceRegistry
{
    private readonly Dictionary<Type, object> _instances = new();
    private readonly Dictionary<Type, Func<ServiceRegistry, object>> _factories = new();
    private readonly HashSet<Type> _constructing = new();

    public ServiceRegistry AddSingleton<T>(T instance) where T : class
    {
        _instances[typeof(T)] = instance;
        return this;
    }

    public ServiceRegistry AddSingleton<T>(Func<ServiceRegistry, T> factory) where T : class
    {
        _factories[typeof(T)] = registry => factory(registry);
        return this;
    }

    public T Get<T>() where T : class
    {
        if (_instances.TryGetValue(typeof(T), out var existing)) return (T)existing;
        if (_factories.TryGetValue(typeof(T), out var factory))
        {
            if (!_constructing.Add(typeof(T)))
                throw new InvalidOperationException($"Cyclic dependency while constructing {typeof(T).Name}.");
            try
            {
                var created = factory(this);
                _instances[typeof(T)] = created;
                return (T)created;
            }
            finally
            {
                _constructing.Remove(typeof(T));
            }
        }
        throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
    }

    public bool TryGet<T>(out T value) where T : class
    {
        try
        {
            value = Get<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default!;
            return false;
        }
    }

    public bool IsRegistered<T>() => _instances.ContainsKey(typeof(T)) || _factories.ContainsKey(typeof(T));
}
