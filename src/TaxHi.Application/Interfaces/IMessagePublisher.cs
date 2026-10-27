namespace TaxHi.Application.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default);
}