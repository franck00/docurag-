using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Messaging;
using DocuRag.Infrastructure.Messaging;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class DeadLetterInspectorTests
{
    private static ServiceBusReceivedMessage DeadLettered(BinaryData body, string reason, string description)
        => ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: body,
            messageId: "msg-1",
            properties: new Dictionary<string, object>
            {
                ["DeadLetterReason"] = reason,
                ["DeadLetterErrorDescription"] = description,
            },
            deliveryCount: 5,
            sequenceNumber: 42,
            enqueuedTime: new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ToEntry_ReadableBody_ExposesDocumentAndReason()
    {
        var payload = new DocumentMessage
        {
            DocumentId = "doc-123",
            BlobUrl = "https://example.blob.core.windows.net/docs/acte.pdf",
            FileName = "acte.pdf",
            DocumentType = "ohada",
        };
        var message = DeadLettered(
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(payload)),
            "MaxDeliveryCountExceeded",
            "Message could not be consumed after 5 delivery attempts.");

        var entry = ServiceBusDeadLetterInspector.ToEntry(message);

        Assert.Equal("msg-1", entry.MessageId);
        Assert.Equal(42, entry.SequenceNumber);
        Assert.Equal("doc-123", entry.DocumentId);
        Assert.Equal("acte.pdf", entry.FileName);
        Assert.Equal("MaxDeliveryCountExceeded", entry.Reason);
        Assert.Equal(5, entry.DeliveryCount);
    }

    [Fact]
    public void ToEntry_UnreadableBody_StillReturnsReason()
    {
        // Cas typique : message dead-letteré par le worker pour DeserializationError.
        var message = DeadLettered(
            BinaryData.FromString("pas du json"), "DeserializationError", "'p' is an invalid start of a value.");

        var entry = ServiceBusDeadLetterInspector.ToEntry(message);

        Assert.Null(entry.DocumentId);
        Assert.Null(entry.FileName);
        Assert.Equal("DeserializationError", entry.Reason);
        Assert.Contains("invalid", entry.ErrorDescription);
    }
}
