namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiCommandPipelineOptions
{
    public bool EnableSerializedWriteGate { get; set; } = true;
}

