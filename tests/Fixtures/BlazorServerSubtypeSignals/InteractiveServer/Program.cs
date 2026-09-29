var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.Run();
