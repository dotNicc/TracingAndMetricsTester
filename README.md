# TracingAndMetricsTester
Simple tool that demonstrates the following:
* How to instrument your code with Activity traces
* Generate logs based on activity attributes (on ActivityStart and ActivityEnd) using an ActivityListener
* Export the traces to Jaeger using OpenTelemetry
* Collect trace durations in an AppMetrics Histogram

## How to use
Have Jaeger UI up and running (get the all-in-one version here: https://www.jaegertracing.io/docs/1.13/getting-started/) and start the program. Once the execution is done, go in Jaeger UI to see the trace (http://localhost:16686).
