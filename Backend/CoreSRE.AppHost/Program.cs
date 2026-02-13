var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector").WithImageTag("pg17")
    .WithDataVolume("coresre-vec").WithPgAdmin().WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("coresre");

var minio = builder.AddMinioContainer("minio").WithDataVolume("coresre-s3").WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.CoreSRE>("api")
    .WithReference(postgres)
    .WithReference(minio)
    .WaitFor(minio)
    .WaitFor(postgres)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
