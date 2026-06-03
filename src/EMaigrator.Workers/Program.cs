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

// The streaming pipeline: control gate, session/copier factories, the consumers (incl. the
// completion consumer), the job orchestrator, the crash-resume hosted service, the real EF/IMAP
// data-seams (AddWorkerDataSeams), and the RabbitMQ topology + DLQ retry.
builder.Services.AddEmaigratorWorkers(builder.Configuration);

var host = builder.Build();
host.Run();
