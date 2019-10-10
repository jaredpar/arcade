# Display

- Show the status of jobs that are queued up: have they started executing
- The output for the multiple run is terrible. Need to break it down by 
the assemblies
- Need to raise the errors up so what failed is visible. Today it's completely
hidden.
- Need to surface how long each test run takes for the non-partitioned assemblies
so that I know what I can partition better in the future.
- Need RolexStorage to hold a container type and not a raw List<HelixJob>. This lets it have a 
name, abstract away the type of run, etc ...
- Move all the non-user feedback to a logger and a log file that gets dumped to
the application storage directory.
- Need to produce an error when a work item goes through three attempts and just fails
outright.
- The correlation util should be split into a builder (mutable) and a reader (immutable). That will
let the queue code be changed into a fan out pattern. 
- The correlation grouping should be split into two zip files:
    1. DLLs which have a write date within one week. These are likely DLLs which were built by 
       the developer
    2. DLLs which have an older write date. These are likely framework / external references and are
       less likely to change. More chance the correlation payload will stay stable between builds

# Helix API Feedback

## IHelixApi
- Should be possible to determine if it's anonymous or not.
- Every parameter named `job` should be called `correlationId` instead
- Why doesn't `IPayload.UploadAsync` return a `Task<Uri>`.
- Need a `WithPayloadStream` as well

# Quick Runs


dotnet run queue -a P:\roslyn\artifacts\bin\Microsoft.CodeAnalysis.CSharp.Syntax.UnitTests\Debug\net472\Microsoft.CodeAnalysis.CSharp.Syntax.UnitTests.dll
