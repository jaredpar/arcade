# Display

- Show the status of jobs that are queued up: have they started executing
- The output for the multiple run is terrible. Need to break it down by 
the assemblies
- Need to raise the errors up so what failed is visible. Today it's completely
hidden.
- Need to surface how long each test run takes for the non-partitioned assemblies
so that I know what I can partition better in the future.

# Helix API Feedback

## IHelixApi
- Should be possible to determine if it's anonymous or not.
- Every parameter named `job` should be called `correlationId` instead
