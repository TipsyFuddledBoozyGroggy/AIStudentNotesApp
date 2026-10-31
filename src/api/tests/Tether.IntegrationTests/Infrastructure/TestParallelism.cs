// Every test class boots its own Host against its own throwaway database, with unpooled connections and its own background services
// (sweepers, workers, reapers). With xunit's default of one parallel class per CPU (28 on the dev machine) the peak exceeded the dev
// Postgres max_connections (100) once the notes classes were added (PO-18d): "FATAL: sorry, too many clients already" at Host startup
// failed whole classes at random. A fixed cap keeps the peak well under the limit on any machine. Raise it only together with
// max_connections (devops owns the compose file).
[assembly: CollectionBehavior(MaxParallelThreads = 12)]
