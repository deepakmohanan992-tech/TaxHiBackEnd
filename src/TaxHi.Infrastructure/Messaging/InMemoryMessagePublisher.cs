using Microsoft.Extensions.Logging;
using TaxHi.Application.Interfaces;

namespace TaxHi.Infrastructure.Messaging;

public sealed class InMemoryMessagePublisher : IMessagePublisher
{
    private readonly ILogger<InMemoryMessagePublisher> _logger;

    public InMemoryMessagePublisher(ILogger<InMemoryMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("In-memory message published to topic {Topic}: {Message}", topic, message);
        // Non-durable: messages are not persisted and may be lost on restart.
        return Task.CompletedTask;
    }
}