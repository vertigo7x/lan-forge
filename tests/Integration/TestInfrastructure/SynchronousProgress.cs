namespace LanForge.IntegrationTests;

/// <summary>
/// IProgress que entrega el valor en el hilo que reporta, sin encolarlo.
/// Progress&lt;T&gt; despacha al hilo de fondo, asi que una asercion posterior puede
/// ejecutarse antes de que llegue el ultimo evento.
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
