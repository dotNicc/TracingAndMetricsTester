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
        
        // Histogram metric options, used for AppMetrics
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
            ActivitySource.AddActivityListener(GetBatchActivityListener());

            // get the metrics api to generate Histograms
            IMetricsRoot metricsRoot = GetMetricsRoot();
            
            Context context = null;
            
            // instrument first activity
            // sleep for 250ms and add test tag
            List<KeyValuePair<string, object>> batchAttribute = new List<KeyValuePair<string, object>> {new KeyValuePair<string, object>("batch", "1")};
            var worker1 = new InstrumentedWorker("Test activity", ActivityKind.Server, null, batchAttribute, () => Thread.Sleep(250), ActivitySource);
            Activity firstActivity = worker1.DoWork();
            if (firstActivity?.IsAllDataRequested == true)
            {
                firstActivity.SetTag("Test tag", "A value for a custom tag");
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(firstActivity.Duration.Milliseconds));
                context = new Context(firstActivity.Context);
            }

            // instrument second activity
            // sleep for 350 ms
            batchAttribute[0] = new KeyValuePair<string, object>("batch", "2");
            var worker2 = new InstrumentedWorker("Another test activity", ActivityKind.Server, context, batchAttribute, () => Thread.Sleep(350), ActivitySource);
            Activity secondActivity = worker2.DoWork();
            if (secondActivity?.IsAllDataRequested == true)
            {
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(secondActivity.Duration.Milliseconds));
                context = new Context(secondActivity.Context);
            }
            
            // instrument third activity, keep it opened
            // sleep for 500 ms and add events
            batchAttribute[0] = new KeyValuePair<string, object>("batch", "3");
            var worker3 = new InstrumentedWorker("Third test activity", ActivityKind.Internal, context, batchAttribute, () => Thread.Sleep(500), ActivitySource);
            Activity thirdActivity = worker3.StartWork();
            if (thirdActivity?.IsAllDataRequested == true)
            {
                thirdActivity.AddEvent(new ActivityEvent("An event"));
                Thread.Sleep(500);
                thirdActivity.AddEvent(new ActivityEvent("An event 500ms later"));
                context = new Context(thirdActivity.Context);
            }
            
            // add event using current activity
            Activity.Current?.AddEvent(new ActivityEvent("An event added to the current activity"));
            
            // instrument fourth activity as a nested activity
            // sleep for 500 ms
            batchAttribute[0] = new KeyValuePair<string, object>("batch", "4");
            var worker4 = new InstrumentedWorker("Nested activity", ActivityKind.Client, context, batchAttribute, () => Thread.Sleep(500), ActivitySource);
            Activity fourthActivity = worker4.DoWork();
            if (fourthActivity?.IsAllDataRequested == true)
            {
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(fourthActivity.Duration.Milliseconds));
            }

            // stop 3rd activity
            thirdActivity = worker3.StopWork();
            if (thirdActivity != null)
            {
                metricsRoot.Measure.Histogram.Update(SampleHistogram, Convert.ToInt64(thirdActivity.Duration.Milliseconds));
            }

            // export the metrics
            await Task.WhenAll(metricsRoot.ReportRunner.RunAllAsync());
            tracerProvider.Shutdown();
        }

        // Create listener, add callbacks on ActivityStarted and ActivityStopped to log batch activities (batch attribute needs to be there)
        private static ActivityListener GetBatchActivityListener()
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

        // Create tracer provider with JaegerExporter connected to endpoint localhost:6831
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

        // Create metrics root, configure it to output to console
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