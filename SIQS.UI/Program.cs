using SIQS.Pipeline;
using SIQS.Overlord;
using SIQS.UI;
using SIQS.UI.Components;
using SIQS.UI.Services;

var publishedContentRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var options = Directory.Exists(publishedContentRoot)
    ? new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory }
    : new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SIQS application services.
var runsRoot = Path.Combine(builder.Environment.ContentRootPath, "runs");
builder.Services.AddSingleton<ISiqsPipeline>(_ => new SiqsPipeline());
builder.Services.AddSingleton(new RunsDirectory(runsRoot));
builder.Services.AddSingleton<FactorizationJobService>();
builder.Services.AddSingleton<RunParameterValidator>();
builder.Services.AddSingleton<ArtifactBrowser>();
builder.Services.AddSingleton(new OverlordService(runsRoot));
builder.Services.AddSingleton<JobCatalog>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDistributedEndpoints();

app.Run();
