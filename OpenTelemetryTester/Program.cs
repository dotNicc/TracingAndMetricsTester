using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Histogram;
using App.Metrics.ReservoirSampling.Uniform;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpenTelemetryTester
{
    internal static class Program
    {
        private static readonly AssemblyName AssemblyName = typeof(Program).Assembly.GetName();
        private static readonly ActivitySource ActivitySource = new ActivitySource(AssemblyName.Name, AssemblyName.Version.ToString());
        private static TracerProvider tracerProvider;
        
        private static HistogramOptions SampleHistogram => new HistogramOptions
        {
            Name = "My Activity Duration Histogram",
            Reservoir = () => new DefaultAlgorithmRReservoir(),
            MeasurementUnit = Unit.Items
        };
        
        public static async Task Main(string[] args)
        {
            // create the tracer to export the traces
            CreateTracerProvider();
            
            // attach a listener to generate activities
            // the listener has callbacks to log at ActivityStart and ActivityEnd
            ActivitySource.AddActivityListener(GetActivityListener());

            // get the metrics api to generate Histograms
            IMetricsRoot metricsRoot = GetMetricsRoot();
            
            Context context = null;
            List<KeyValuePair<string, object>> batchAttribute = new List<KeyValuePair<string, object>> {new KeyValuePair<string, object>("batch", "1")};
            
            Activity trace = GetActivityFromSleep("Test activity", ActivityKind.Server, default(ActivityContext), batchAttribute, 250);
            if (trace != null && trace.IsAllDataRequested)
            {
                trace.SetTag("Test tag", "A value for a custom tag");
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(trace.Duration.Milliseconds));
                context = new Context(trace.Context);
                trace.Dispose();
            }

            batchAttribute[0] = new KeyValuePair<string, object>("batch", "2");
            
            trace = GetActivityFromSleep("Another test activity", ActivityKind.Server, GetActivityContext(context?.TraceId, context?.SpanId, context?.TraceFlags, context?.TraceState), batchAttribute, 350);
            if (trace != null && trace.IsAllDataRequested)
            {
                context = new Context(trace.Context);
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(trace.Duration.Milliseconds));
                trace.Dispose();
            }
            
            batchAttribute[0] = new KeyValuePair<string, object>("batch", "3");
            
            var activity2 = ActivitySource.StartActivity(
                "Third test activity", 
                ActivityKind.Internal, 
                GetActivityContext(context?.TraceId, context?.SpanId, context?.TraceFlags, context?.TraceState), 
                batchAttribute);
            
            if (activity2 != null && activity2.IsAllDataRequested)
            {
                context = new Context(activity2.Context);
                activity2.AddEvent(new ActivityEvent("An event"));
                Thread.Sleep(500);
                activity2.AddEvent(new ActivityEvent("An event 500ms later"));
            }

            Activity.Current?.AddEvent(new ActivityEvent("An event added to the current activity"));
            
            batchAttribute[0] = new KeyValuePair<string, object>("batch", "4");
            
            trace = GetActivityFromSleep("Nested activity", ActivityKind.Client, GetActivityContext(context?.TraceId, context?.SpanId, context?.TraceFlags, context?.TraceState), batchAttribute, 500);
            if (trace != null && trace.IsAllDataRequested)
            {
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(trace.Duration.Milliseconds));
            }
            
            activity2?.Stop();
            if (activity2 != null) metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(activity2.Duration.Milliseconds));
            
            // export the metrics
            await Task.WhenAll(metricsRoot.ReportRunner.RunAllAsync());
            tracerProvider.Shutdown();
        }

        private static Activity GetActivityFromSleep(string activityName, ActivityKind activityKind, ActivityContext context, List<KeyValuePair<string, object>> attributes, int sleepTime)
        {
            Activity traceActivity;
            using (traceActivity = ActivitySource.StartActivity(
                activityName, 
                activityKind,
                context,
                attributes))
            {
                // do stuff
                Thread.Sleep(sleepTime);
            }
            
            return traceActivity;
        }
        
        private static ActivityContext GetActivityContext(string traceId, string spanId, string traceFlags, string traceState)
        {
            if (string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(spanId)) return default;
            
            Enum.TryParse(traceFlags, out ActivityTraceFlags flags);
            return new ActivityContext(
                ActivityTraceId.CreateFromString(traceId.ToCharArray()),
                ActivitySpanId.CreateFromString(spanId.ToCharArray()),
                flags,
                traceState);

        }
        
        private static ActivityListener GetActivityListener()
        {
            return new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                    Console.WriteLine($"Started batch {activity.Tags.ToList().Find(t => t.Key == "batch").Value} - {activity.ParentId}:{activity.Id} - Start"),
                ActivityStopped = activity =>
                    Console.WriteLine($"Finished batch {activity.Tags.ToList().Find(t => t.Key == "batch").Value} - {activity.ParentId}:{activity.Id} - Stop " +
                                      $"- Duration:{activity.Duration.TotalMilliseconds}ms")
            };
        }

        private static void CreateTracerProvider()
        {
            tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(AssemblyName.Name, AssemblyName.Version.ToString())
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("ConsoleTestNet462"))
                //.AddConsoleExporter()
                .AddJaegerExporter(o =>
                {
                    o.AgentHost = "localhost";
                    o.AgentPort = 6831;
                })
                .Build();
        }

        private static IMetricsRoot GetMetricsRoot()
        {
            return new MetricsBuilder()
                .Configuration.Configure(
                    options =>
                    {
                        options.DefaultContextLabel = "MyContext";
                        options.GlobalTags.Add("myTagKey", "myTagValue");
                        options.Enabled = true;
                        options.ReportingEnabled = true;
                    })
                .Report.ToConsole(
                    options => { options.FlushInterval = TimeSpan.FromSeconds(5); })
                .Build();
        }
    }
}