var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume().WithPgAdmin().WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("coresre");

builder.AddProject<Projects.CoreSRE>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
