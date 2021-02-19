using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTelemetryTester
{
    public class InstrumentedWorker : IDisposable
    {
        private readonly string activityName;
        private readonly ActivityKind activityKind;
        private readonly Context context;
        private readonly List<KeyValuePair<string, object>> attributes;
        private readonly Action job;
        private readonly ActivitySource activitySource;

        private Activity traceActivity;

        public InstrumentedWorker(string name, ActivityKind kind, Context context, List<KeyValuePair<string, object>> attributes, Action work, ActivitySource source)
        {
            this.activitySource = source;
            this.job = work;
            this.attributes = attributes;
            this.context = context;
            this.activityKind = kind;
            this.activityName = name;
        }
        
        public Activity DoWork()
        {
            using (this.traceActivity = this.activitySource.StartActivity(this.activityName, this.activityKind, GetActivityContext(this.context), this.attributes))
            {
                this.job();
            }

            return this.traceActivity;
        }

        public Activity StartWork()
        {
            this.traceActivity = this.activitySource.StartActivity(this.activityName, this.activityKind, GetActivityContext(this.context), this.attributes);
            this.job();
            return this.traceActivity;
        }

        public Activity StopWork()
        {
            if (this.traceActivity == null) return null;

            this.traceActivity.Stop();
            return this.traceActivity;
        }
        
        private static ActivityContext GetActivityContext(Context context)
        {
            if (context == null || string.IsNullOrEmpty(context.TraceId) || string.IsNullOrEmpty(context.SpanId)) return default(ActivityContext);
            
            Enum.TryParse(context.TraceFlags, out ActivityTraceFlags flags);
            return new ActivityContext(
                ActivityTraceId.CreateFromString(context.TraceId.ToCharArray()),
                ActivitySpanId.CreateFromString(context.SpanId.ToCharArray()),
                flags,
                context.TraceState);
        }

        public void Dispose()
        {
            this.traceActivity?.Dispose();
        }
    }
}