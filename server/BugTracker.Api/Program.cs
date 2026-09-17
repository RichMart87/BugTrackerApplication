using System.Text.Json.Serialization;
using BugTracker.Api.Data;
using BugTracker.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Don't advertise the server stack in every response.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// QuestPDF community licence - free for individuals and small companies.
// See https://www.questpdf.com/license/ before shipping commercially.
QuestPDF.Settings.License = LicenseType.Community;

// ---------------------------------------------------------------------------
// Persistence
// ---------------------------------------------------------------------------
// SQLite is the default so the app runs with no local SQL install. Point
// Database:Provider at "SqlServer" (appsettings or the DATABASE__PROVIDER env var)
// to use LocalDB / SQL Server instead - the model and queries are provider-agnostic.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString(provider)
                       ?? "Data Source=bugtracker.db";

builder.Services.AddDbContext<BugTrackerDbContext>(options =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IBugService, BugService>();
builder.Services.AddSingleton<CsvBugExporter>();
builder.Services.AddSingleton<PdfBugExporter>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as their names ("Critical", "Production") so the client never
        // has to keep a copy of the numeric ordering.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

        // Malformed payloads get a generic message instead of the serializer's, which
        // leaks internal type and namespace names ("System.Nullable`1[BugTracker.Api...]").
        options.AllowInputFormatterExceptionMessages = false;
    });

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "BugTracker API", Version = "v1" });
    var xml = Path.Combine(AppContext.BaseDirectory, "BugTracker.Api.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

const string DevCorsPolicy = "spa-dev";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Without this the browser cannot read the download filename off the export responses.
        .WithExposedHeaders("Content-Disposition"));
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database bootstrap
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BugTrackerDbContext>();

    // EnsureCreated keeps the sample runnable with zero setup on either provider.
    // For a real deployment switch to EF migrations (see Sql/schema.sql for the DDL).
    await db.Database.EnsureCreatedAsync();

    // Off unless explicitly enabled (appsettings.Development.json does), so a fresh
    // production database never fills up with demo defects.
    if (app.Configuration.GetValue("Database:SeedSampleData", false))
    {
        await DbSeeder.SeedAsync(db);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BugTracker API v1");
        options.DocumentTitle = "BugTracker API";
    });
}
else
{
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        // Kestrel signals client errors such as an oversized body (413) with
        // BadHttpRequestException. Without this they surface to the caller as 500s.
        // (The middleware still logs them at Error level in .NET 9; rate limiting is
        // the real guard against someone flooding the log.)
        StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError
    });
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    // Swagger UI needs inline script, and Swagger only runs in Development, so the
    // strict policy applies everywhere else. The built Vue client needs nothing inline.
    if (!app.Environment.IsDevelopment())
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; " +
            "form-action 'self'; frame-ancestors 'none'";
    }

    await next();
});

app.UseStatusCodePages();

// Cross-origin access is a development convenience only. In production the API
// serves the SPA from the same origin, so no origin needs to be allowed.
if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// Serves the built Vue client when client/dist has been copied into wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", utc = DateTime.UtcNow }))
   .WithName("Health");

// Any unmatched non-API route falls through to the SPA shell.
app.MapFallbackToFile("index.html");

app.Run();
