using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Pos.Frontend.Application;
using Pos.Frontend.Infrastructure;
using Pos.Frontend.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API runs on another computer: its address is read from
// wwwroot/appsettings.json ("ApiBaseUrl"), editable without recompiling.
string apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5100";
builder.Services.AddPosFrontend(new Uri(apiBaseUrl.TrimEnd('/') + "/"));

var host = builder.Build();
await host.Services.GetRequiredService<IAuthSession>().InitializeAsync();
await host.RunAsync();
