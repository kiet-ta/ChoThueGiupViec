using Microsoft.Extensions.Configuration.UserSecrets;

// BE-DEV-MIGRATE-01: the project sets GenerateAssemblyInfo=false, so the SDK does not emit the UserSecretsId attribute that
// <UserSecretsId> in CommonService.csproj stands for. Without it the app silently ignores user secrets (for example a
// per-machine connection string). Keep this GUID equal to the csproj one. If GenerateAssemblyInfo is ever turned on,
// delete this line (the SDK would then emit the attribute a second time).
[assembly: UserSecretsId("a6ce7e6f-d08a-4657-a67a-f19fa1cbbf71")]
