using System.Diagnostics;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;

namespace DocuRag.Core.Tests;

public class TraceContextPropagationTests
{
    [Fact]
    public void Traceparent_round_trips_through_message_properties()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "docurag=1";

        var message = IngestionMessageSerializer.ToServiceBusMessage(new IngestionMessage
        {
            DocumentId = "doc",
            BlobUrl = "https://st.blob.core.windows.net/documents/a.pdf",
            BlobName = "a.pdf",
            DocType = "general",
            EventId = "evt-1",
        }, activity);

        var context = TraceContextPropagation.Extract(message.ApplicationProperties.AsReadOnly());

        Assert.True(context.HasValue);
        var restored = context.GetValueOrDefault();
        Assert.Equal(activity.TraceId, restored.TraceId);
        Assert.Equal(activity.SpanId, restored.SpanId);
        Assert.Equal("docurag=1", restored.TraceState);
        Assert.True(restored.IsRemote);
        Assert.Equal("evt-1", message.MessageId);
        Assert.Equal("doc", message.CorrelationId);
    }

    [Fact]
    public void Missing_or_invalid_traceparent_yields_null()
    {
        Assert.Null(TraceContextPropagation.Extract(new Dictionary<string, object>()));
        Assert.Null(TraceContextPropagation.Extract(new Dictionary<string, object> { ["traceparent"] = "n'importe quoi" }));
    }

    [Fact]
    public void Message_body_round_trips()
    {
        var original = new IngestionMessage
        {
            DocumentId = "d1",
            BlobUrl = "https://st.blob.core.windows.net/documents/x/a.pdf",
            BlobName = "x/a.pdf",
            DocType = "x",
        };

        var sbMessage = IngestionMessageSerializer.ToServiceBusMessage(original, activity: null);
        var roundTripped = IngestionMessageSerializer.Deserialize(sbMessage.Body);

        Assert.Equal(original, roundTripped);
        Assert.False(sbMessage.ApplicationProperties.ContainsKey("traceparent"));
    }

    [Fact]
    public void Invalid_body_throws_json_exception() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            IngestionMessageSerializer.Deserialize(BinaryData.FromString("{\"documentId\":\"x\"}")));
}
