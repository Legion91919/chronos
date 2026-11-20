using Chronos.Api;

var builder = WebApplication.CreateBuilder(args);
ChronosApp.ConfigureServices(builder);
var app = builder.Build();
ChronosApp.Configure(app);
await app.RunAsync().ConfigureAwait(false);
