using EMaigrator.Connectors.Gmail;
using EMaigrator.Connectors.Graph;
using EMaigrator.Connectors.Imap;
using EMaigrator.Infrastructure;
using EMaigrator.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Engine seams from Infrastructure: the Postgres ledger, Redis rate limiter + control backplane,
// and the secret store. The Workers subsystem brings its own MassTransit/RabbitMQ bus (so it can
// attach the consumers), so Infrastructure is asked NOT to register one (registerBus: false).
builder.Services.AddInfrastructure(builder.Configuration, registerBus: false);

// Provider plugins for the three v1 connectors; the session factory selects the right one by
// ConnectionDescriptor.Provider when it builds a source/destination for a migration.
builder.Services.AddImapConnector();
builder.Services.AddGraphConnector();
builder.Services.AddGmailConnector();

// The four-stage streaming pipeline: control gate, session/copier factories, the five consumers,
// the job orchestrator, the crash-resume hosted service, and the RabbitMQ topology + DLQ retry.
builder.Services.AddEmaigratorWorkers(builder.Configuration);

// The persistence-backed lookups the consumers depend on are implemented in Plan 08 (API), which
// reads the EF entities. Until then they are registered as pending placeholders so the host
// composes and starts idle; invoking one before Plan 08 throws a clear NotImplementedException.
builder.Services.AddPendingWorkerSeams();

var host = builder.Build();
host.Run();
