using System.Diagnostics;

namespace OpenTelemetryTester
{
    public class Context
    {
        public string TraceId { get; }
        public string SpanId { get; }
        public string TraceFlags { get; }
        public string TraceState { get; }

        public Context(ActivityContext context)
        {
            TraceId = context.TraceId.ToHexString();
            SpanId = context.SpanId.ToHexString();
            TraceFlags = context.TraceFlags.ToString();
            TraceState = context.TraceState;
        }
    }
}