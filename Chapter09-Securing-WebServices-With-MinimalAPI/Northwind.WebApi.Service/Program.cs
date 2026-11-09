using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpLogging;
using Packt.Shared;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Net;
using System.Security.Claims;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

Logger logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNorthwindContext();
builder.Services.AddHttpLogging(options =>
{
    // Add the Origin header so it will not be redacted.
    options.RequestHeaders.Add("Origin");
    // Add the rate limiting headers so they will not be redacted. (DoS)
    options.RequestHeaders.Add("X-Client-Id");
    options.RequestHeaders.Add("X-Real-IP");
    options.ResponseHeaders.Add("Retry-After");
    // By default, the response body is not included.
    options.LoggingFields = HttpLoggingFields.All;
});
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "northwindMvc",
        policy =>
        {
            policy.WithOrigins("https://localhost:5092");
        });
});
// Authorization and Authentication using JWT Bearer
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(defaultScheme: "Bearer")
    .AddJwtBearer();


// true: when using built-in rate-limiting feature (ASP.NET Core middleware, using: Microsoft.AspNetCore.RateLimiting)
// false: when using third-party rate limiting feature (nuget: AspNetCoreRateLimit)
bool useMicrosoftRateLimiting = true;

// DoS
// Configure AspNetCoreRateLimit rate limiting middleware
if (!useMicrosoftRateLimiting)
{
    // Add services to store rate limit counters and rules.
    builder.Services.AddMemoryCache();
    builder.Services.AddInMemoryRateLimiting();

    // Load default rate limit options from appsettings.json
    builder.Services.Configure<ClientRateLimitOptions>(
        builder.Configuration.GetSection("ClientRateLimiting"));

    // Load client-specific policies from appsettings.json
    builder.Services.Configure<ClientRateLimitPolicies>(
        builder.Configuration.GetSection("ClientRateLimitPolicies"));

    // Register the configuration
    builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
}

var app = builder.Build();

// loading the policies from the configuration
if (!useMicrosoftRateLimiting)
{
    using IServiceScope scope = app.Services.CreateScope();
    IClientPolicyStore clientPolicyStore = scope.ServiceProvider
        .GetRequiredService<IClientPolicyStore>();
    await clientPolicyStore.SeedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseHttpLogging();

// DoS
if (!useMicrosoftRateLimiting)
{
    app.UseClientRateLimiting();
}
else
{
    // Configure ASP.NET Core rate limiting middleware.
    RateLimiterOptions rateLimiterOptions = new();
    rateLimiterOptions.AddFixedWindowLimiter(
        policyName: "fixed5per10seconds", options =>
        {
            options.PermitLimit = 5;
            options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            options.QueueLimit = 2;
            options.Window = TimeSpan.FromSeconds(10);
        });

    app.UseRateLimiter(rateLimiterOptions);
}

// Enables same CORS policy to the whole web servie.
//app.UseCors(policyName: "northwindMvc");
app.UseCors();

// Authentication and Authorization
app.UseAuthorization();

app.MapGet("/", () => "Hello World!").ExcludeFromDescription();
app.MapGet("/secret", (ClaimsPrincipal user) =>
$"Welcome, {user.Identity?.Name ?? "secure user"}. The secret ingredient is love.")
    .RequireAuthorization()
    .Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/secret2", (ClaimsPrincipal user) =>
$"Welcome, {user.Identity?.Name ?? "secure user"}. Secret for claim myapi:secrets.")
    .RequireAuthorization(p => p.RequireClaim("scope", "myapi:secrets"))
    .Produces(StatusCodes.Status401Unauthorized);

int pageSize = 10;

app.MapGet("api/products", (
Serilog.ILogger logger,
[FromServices] NorthwindContext db,
[FromQuery] int? page) => {
    db.Products.Where(product =>
    (product.UnitsInStock > 0) && (!product.Discontinued))
    .Skip(((page ?? 1) - 1) * pageSize).Take(pageSize);
})
.WithName("GetProducts")
.WithOpenApi(operation =>
{
    operation.Description = "Get products with UnitsInStock > 0 and Discontinued = false.";
    operation.Summary = "Get in-stock products that are not discontinued.";
    return operation;
})
.Produces<Product[]>(StatusCodes.Status200OK)
.RequireRateLimiting(policyName: "fixed5per10seconds");

app.MapGet("api/products/outofstock", ([FromServices] NorthwindContext db, ClaimsPrincipal user) =>
db.Products.Where(product =>
(product.UnitsInStock == 0) && (!product.Discontinued)))
.WithName("GetProductsOutOfStock")
.WithOpenApi()
.RequireAuthorization()
.Produces<Product[]>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("api/products/discontinued", ([FromServices] NorthwindContext db) =>
db.Products.Where(product => product.Discontinued))
.WithName("GetProductsDiscontinued")
.WithOpenApi()
.RequireAuthorization(p => p.RequireClaim("scope", "myapi:secrets"))
.Produces<Product[]>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("api/products/{id:int}",
async Task<Results<Ok<Product>, NotFound>> (
[FromServices] NorthwindContext db,
[FromRoute] int id) =>
await db.Products.FindAsync(id) is Product product ?
TypedResults.Ok(product) : TypedResults.NotFound())
.WithName("GetProductById")
.WithOpenApi()
.Produces<Product>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("api/products/{name}", (
[FromServices] NorthwindContext db, [FromRoute] string name) =>
db.Products.Where(p => p.ProductName.Contains(name)))
.WithName("GetProductsByName")
.WithOpenApi()
.Produces<Product[]>(StatusCodes.Status200OK)
.RequireCors(policyName: "northwindMvc");

app.MapPost("api/products", async ([FromBody] Product product,
[FromServices] NorthwindContext db) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"api/products/{product.ProductId}", product);
}).WithOpenApi()
.Produces<Product>(StatusCodes.Status201Created);

app.MapPut("api/products/{id:int}", async (
[FromRoute] int id,
[FromBody] Product product,
[FromServices] NorthwindContext db) =>
{
    Product? foundProduct = await db.Products.FindAsync(id);
    if (foundProduct is null) return Results.NotFound();
    foundProduct.ProductName = product.ProductName;
    foundProduct.CategoryId = product.CategoryId;
    foundProduct.SupplierId = product.SupplierId;
    foundProduct.QuantityPerUnit = product.QuantityPerUnit;
    foundProduct.UnitsInStock = product.UnitsInStock;
    foundProduct.UnitsOnOrder = product.UnitsOnOrder;
    foundProduct.ReorderLevel = product.ReorderLevel;
    foundProduct.UnitPrice = product.UnitPrice;
    foundProduct.Discontinued = product.Discontinued;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).WithOpenApi()
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status204NoContent);

app.MapDelete("api/products/{id:int}", async (
[FromRoute] int id,
[FromServices] NorthwindContext db) =>
{
    if (await db.Products.FindAsync(id) is Product product)
    {
        db.Products.Remove(product);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    return Results.NotFound();
}).WithOpenApi()
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status204NoContent);

app.Run();