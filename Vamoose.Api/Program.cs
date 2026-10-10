using Amazon;
using Amazon.BedrockAgentCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Infrastructure;
using Vamoose.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("Vamoose")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Vamoose (see README).");
if (builder.Environment.IsDevelopment())
{
    connectionString = LocalDbConnection.Resolve(connectionString);
}
builder.Services.AddDbContext<VamooseDbContext>(options => options.UseSqlServer(connectionString));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "vamoose.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // API clients get status codes, not login-page redirects.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TripAccess>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<AgreementService>();
builder.Services.AddScoped<LifecycleService>();
builder.Services.AddScoped<MagicLinkService>();
builder.Services.AddSingleton<IEmailSender, DevEmailSender>();
builder.Services.AddHostedService<ResponseWindowCloser>();

if (!string.IsNullOrEmpty(builder.Configuration["Agents:RuntimeArn"]))
{
    builder.Services.AddSingleton<IAmazonBedrockAgentCore>(_ => new AmazonBedrockAgentCoreClient(new AmazonBedrockAgentCoreConfig
    {
        RegionEndpoint = RegionEndpoint.GetBySystemName(builder.Configuration["Agents:Region"] ?? "us-east-2"),
        Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Agents:TimeoutSeconds", 180)),
    }));
    builder.Services.AddScoped<IAgentClient, AgentCoreAgentClient>();
}
else
{
    builder.Services.AddHttpClient<IAgentClient, HttpAgentClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Agents:BaseUrl"] ?? "http://localhost:8080/");
        client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Agents:TimeoutSeconds", 180));
    });
}

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Keeps each developer's database on the latest schema, with a demo trip to show.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VamooseDbContext>().Database.Migrate();
    if (app.Configuration.GetValue("Demo:Seed", false))
    {
        await DemoSeeder.SeedAsync(scope.ServiceProvider);
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Lets the API tests start the app with WebApplicationFactory.
public partial class Program;
