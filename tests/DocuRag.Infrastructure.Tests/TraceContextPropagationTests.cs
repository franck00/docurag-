using System.Diagnostics;
using DocuRag.Infrastructure.Messaging;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class TraceContextPropagationTests
{
    private static Activity StartW3CActivity(string? traceState = null)
    {
        var activity = new Activity("Enqueue");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.TraceStateString = traceState;
        return activity.Start();
    }

    [Fact]
    public void InjectThenExtract_RestoresSameTraceAndParentSpan()
    {
        using var activity = StartW3CActivity("congo=t61rcWkgMzE");
        var properties = new Dictionary<string, object>();

        TraceContextPropagation.Inject(activity, properties);
        var parent = TraceContextPropagation.Extract(properties);

        Assert.Equal(activity.TraceId, parent.TraceId);
        Assert.Equal(activity.SpanId, parent.SpanId);
        Assert.Equal("congo=t61rcWkgMzE", parent.TraceState);
        Assert.True(parent.IsRemote);
    }

    [Fact]
    public void Inject_WithoutTraceState_WritesOnlyTraceParent()
    {
        using var activity = StartW3CActivity();
        var properties = new Dictionary<string, object>();

        TraceContextPropagation.Inject(activity, properties);

        Assert.Equal(activity.Id, properties[TraceContextPropagation.TraceParentKey]);
        Assert.False(properties.ContainsKey(TraceContextPropagation.TraceStateKey));
    }

    [Fact]
    public void Inject_NoActivity_LeavesPropertiesUntouched()
    {
        var properties = new Dictionary<string, object>();

        TraceContextPropagation.Inject(null, properties);

        Assert.Empty(properties);
    }

    [Fact]
    public void Inject_HierarchicalId_IsIgnored()
    {
        var activity = new Activity("Legacy");
        activity.SetIdFormat(ActivityIdFormat.Hierarchical);
        using var started = activity.Start();
        var properties = new Dictionary<string, object>();

        TraceContextPropagation.Inject(started, properties);

        Assert.Empty(properties);
    }

    [Theory]
    [InlineData("pas-un-traceparent")]
    [InlineData("00-0af7651916cd43dd-b7ad6b7169203331-01")]
    public void Extract_InvalidTraceParent_ReturnsDefault(string value)
    {
        var properties = new Dictionary<string, object>
        {
            [TraceContextPropagation.TraceParentKey] = value,
        };

        Assert.Equal(default(ActivityContext), TraceContextPropagation.Extract(properties));
    }

    [Fact]
    public void Extract_MissingOrNonStringProperty_ReturnsDefault()
    {
        Assert.Equal(default(ActivityContext), TraceContextPropagation.Extract(new Dictionary<string, object>()));
        Assert.Equal(default(ActivityContext), TraceContextPropagation.Extract(new Dictionary<string, object>
        {
            [TraceContextPropagation.TraceParentKey] = 42,
        }));
    }
}
