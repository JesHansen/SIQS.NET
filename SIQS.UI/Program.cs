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
var overlordOptions = builder.Configuration
    .GetSection("Overlord")
    .Get<OverlordOptions>() ?? new OverlordOptions();
// The hosted job coordinator closes admission, drains accepted uploads, cancels both pipelines,
// and joins their workers. If 30 seconds elapse, it logs the affected ids and leaves the last
// atomically persisted state resumable instead of silently extending host termination forever.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ISiqsPipeline>(_ => new SiqsPipeline());
builder.Services.AddSingleton(new RunsDirectory(runsRoot));
builder.Services.AddSingleton<JobWorkspaceResolver>();
builder.Services.AddSingleton<FactorizationJobService>();
builder.Services.AddSingleton<RunParameterValidator>();
builder.Services.AddSingleton<ArtifactBrowser>();
builder.Services.AddSingleton(_ => new OverlordService(runsRoot, overlordOptions));
builder.Services.AddSingleton<JobCatalog>();
builder.Services.AddSingleton<SieveClientCatalog>();
builder.Services.AddHostedService<ApplicationJobLifetime>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseMiddleware<RunsDirectoryBoundaryMiddleware>();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDistributedEndpoints();

app.Run();

public partial class Program;
